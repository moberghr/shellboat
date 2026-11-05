namespace Shellboat.Rdp;

/// <summary>RDP keyboard event: flags (release 0x8000, extended 0x0100) + scancode.</summary>
public readonly record struct KeyInput(ushort Flags, ushort ScanCode);

/// <summary>RDP pointer event in remote desktop pixels.</summary>
public readonly record struct PointerInput(ushort Flags, ushort X, ushort Y);

public readonly record struct FramePresentResult(
    bool Presented, int DirtyX, int DirtyY, int DirtyWidth, int DirtyHeight, int FramebufferWidth, int FramebufferHeight);

/// <summary>Mirror of the native <c>rdp_cursor_kind</c>; values cross the ABI as int.</summary>
public enum RemoteCursorKind
{
    Hidden = 0,
    Default = 1,
    Bitmap = 2,
}

public readonly record struct RemoteCursorDescriptor(RemoteCursorKind Kind, uint CursorId, int Width, int Height, int HotX, int HotY);
