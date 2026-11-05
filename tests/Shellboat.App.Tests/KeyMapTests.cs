using Avalonia.Input;
using Shellboat.App.Rdp;
using Shellboat.App.Terminal;
using Shellboat.Terminal;

namespace Shellboat.App.Tests;

public class KeyMapTests
{
    [Fact]
    public void Terminal_key_and_chord_mapping()
    {
        Assert.Equal(TerminalKey.Up, AvaloniaKeyMap.ToTerminalKey(Key.Up));
        Assert.Null(AvaloniaKeyMap.ToTerminalKey(Key.A));
        Assert.Equal("c", AvaloniaKeyMap.ChordCharacter(Key.C));
        Assert.Equal(KeyMods.Control | KeyMods.Shift, AvaloniaKeyMap.ToMods(KeyModifiers.Control | KeyModifiers.Shift));
    }

    [Fact]
    public void Plain_ctrl_c_is_never_the_copy_chord()
        => Assert.False(AvaloniaKeyMap.IsCopy(Key.C, KeyModifiers.Control));

    [Fact]
    public void Rdp_extended_keys_carry_the_extended_flag()
    {
        Assert.True(RdpKeyboardInputMapper.TryMapKey(Key.Right, out var sc));
        var flags = RdpKeyboardInputMapper.BuildKeyFlags(sc, isRelease: true, out var code);
        Assert.Equal((ushort)0x8100, flags);
        Assert.Equal((ushort)0x4D, code);
    }
}
