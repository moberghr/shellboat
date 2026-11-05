using Shellboat.Rdp.Native;

namespace Shellboat.Rdp.Tests;

internal sealed class FakeNativeSession(IntPtr handle) : INativeRdpSession
{
    public IntPtr Handle { get; } = handle;
    public int Frees { get; private set; }
    public List<(int W, int H)> Resizes { get; } = [];
    public List<KeyInput> Keys { get; } = [];

    public void Disconnect() { }

    public void Free() => Frees++;

    public void UpdateResolution(int width, int height) => Resizes.Add((width, height));

    public void SendMouseEvent(ushort flags, ushort x, ushort y) { }

    public void SendKeyboardEvent(ushort flags, ushort code) => Keys.Add(new KeyInput(flags, code));

    public void SetLocalClipboardText(string? text) { }

    public void RequestFullFrame() { }

    public FramePresentResult Present(IntPtr dest, int destStride, int destWidth, int destHeight) => default;

    public bool CopyCursorImage(uint cursorId, IntPtr dest, int destStride, int destWidth, int destHeight) => false;
}
