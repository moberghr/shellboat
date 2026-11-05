using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Shellboat.Rdp;

namespace Shellboat.App.Rdp;

/// <summary>
/// Turns the native cursor descriptors reported by <c>CursorCallback</c> into Avalonia
/// <see cref="Cursor"/> instances, caching them by the session-unique id the wrapper assigns.
///
/// Building a <see cref="Cursor"/> creates a platform cursor handle, which is far more expensive
/// than the pixel copy itself, so the cache is what keeps hovering across a toolbar cheap: only the
/// first sighting of a shape touches native memory at all.
///
/// UI-thread only. The pixel pull is injected so tests do not need a native session.
/// </summary>
internal sealed class RemoteCursorCache : IDisposable
{
    /// <summary>Mirrors <c>rdp_session_copy_cursor_image</c>.</summary>
    public delegate bool CopyCursorImageDelegate(uint cursorId, IntPtr dest, int destStride, int destWidth, int destHeight);

    // Matches CURSOR_CACHE_CAPACITY in the wrapper: past this the native side has already dropped
    // the image, so a larger managed cache would only hold cursors that can never be refreshed.
    private const int MaxCachedCursors = 64;

    private readonly CopyCursorImageDelegate _copyCursorImage;
    // The source bitmap is kept beside its cursor purely so both can be released deterministically
    // on eviction; the platform cursor handle itself no longer reads from it.
    private readonly Dictionary<uint, (Cursor Cursor, Bitmap Bitmap)> _cursors = [];
    private readonly Queue<uint> _insertionOrder = new();
    private Cursor? _hiddenCursor;
    private bool _disposed;

    private double _scale = 1.0;

    public RemoteCursorCache(CopyCursorImageDelegate copyCursorImage)
    {
        _copyCursorImage = copyCursorImage;
    }

    /// <summary>
    /// Remote pixels per local cursor unit. macOS sizes a custom cursor in points (a 64 px image becomes 64 pt =
    /// 128 physical px on Retina), while the remote session — sized in physical pixels with its own DPI scale —
    /// already drew the arrow at physical size; so on macOS this is the display's render scaling and the image is
    /// shrunk by it. Windows and X11 take cursor bitmaps in physical pixels: 1. Changing it drops cached shapes.
    /// </summary>
    public double Scale
    {
        get => _scale;
        set
        {
            var next = value > 0 ? value : 1.0;
            if (Math.Abs(next - _scale) < 0.001)
            {
                return;
            }

            _scale = next;
            ClearCursors();
        }
    }

    /// <summary>Pure: the cursor image size and hotspot after dividing by <paramref name="scale"/> (never below
    /// 1 px, hotspot kept inside the image).</summary>
    public static (int Width, int Height, int HotX, int HotY) ScaleShape(int width, int height, int hotX, int hotY, double scale)
    {
        if (scale <= 1.0)
        {
            return (width, height, Math.Clamp(hotX, 0, width - 1), Math.Clamp(hotY, 0, height - 1));
        }

        var w = Math.Max(1, (int)Math.Round(width / scale));
        var h = Math.Max(1, (int)Math.Round(height / scale));
        return (w, h, Math.Clamp((int)Math.Round(hotX / scale), 0, w - 1), Math.Clamp((int)Math.Round(hotY / scale), 0, h - 1));
    }

    /// <summary>
    /// Resolves a cursor descriptor. Returns <c>null</c> when the shape could not be produced (the
    /// native side already freed it, or the descriptor is malformed); callers should keep whatever
    /// cursor they are currently showing rather than flashing back to the default arrow.
    /// </summary>
    public Cursor? Resolve(RemoteCursorKind kind, uint cursorId, int width, int height, int hotX, int hotY)
    {
        if (_disposed)
        {
            return null;
        }

        switch (kind)
        {
            case RemoteCursorKind.Hidden:
                return _hiddenCursor ??= new Cursor(StandardCursorType.None);
            case RemoteCursorKind.Default:
                return Cursor.Default;
            case RemoteCursorKind.Bitmap:
                break;
            default:
                return null;
        }

        if (_cursors.TryGetValue(cursorId, out var cached))
        {
            return cached.Cursor;
        }

        var built = BuildCursor(cursorId, width, height, hotX, hotY);
        if (built == null)
        {
            return null;
        }

        _cursors[cursorId] = built.Value;
        _insertionOrder.Enqueue(cursorId);
        TrimToCapacity();
        return built.Value.Cursor;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearCursors();
        _hiddenCursor?.Dispose();
        _hiddenCursor = null;
    }

    private void ClearCursors()
    {
        foreach (var entry in _cursors.Values)
        {
            entry.Cursor.Dispose();
            entry.Bitmap.Dispose();
        }

        _cursors.Clear();
        _insertionOrder.Clear();
    }

    private (Cursor Cursor, Bitmap Bitmap)? BuildCursor(uint cursorId, int width, int height, int hotX, int hotY)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        WriteableBitmap? bitmap = null;
        try
        {
            // Unpremul, unlike the desktop framebuffer: freerdp_image_copy_from_pointer_data
            // produces straight alpha from the AND/XOR masks.
            bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);

            bool copied;
            using (var locked = bitmap.Lock())
            {
                copied = _copyCursorImage(cursorId, locked.Address, locked.RowBytes, width, height);
            }

            if (!copied)
            {
                bitmap.Dispose();
                return null;
            }

            // Hotspots are in remote desktop pixels, i.e. bitmap pixels; ScaleShape keeps them on the same spot
            // of the shrunk image when Scale > 1 (macOS, see Scale).
            var (w, h, hx, hy) = ScaleShape(width, height, hotX, hotY, _scale);
            Bitmap image = bitmap;
            if (w != width || h != height)
            {
                image = bitmap.CreateScaledBitmap(new PixelSize(w, h), BitmapInterpolationMode.HighQuality);
                bitmap.Dispose();
            }

            return (new Cursor(image, new PixelPoint(hx, hy)), image);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[cursor] failed to build cursor {cursorId} ({width}x{height}): {ex.Message}");
            bitmap?.Dispose();
            return null;
        }
    }

    private void TrimToCapacity()
    {
        while (_insertionOrder.Count > MaxCachedCursors)
        {
            var evictedId = _insertionOrder.Dequeue();
            if (_cursors.Remove(evictedId, out var evicted))
            {
                evicted.Cursor.Dispose();
                evicted.Bitmap.Dispose();
            }
        }
    }
}
