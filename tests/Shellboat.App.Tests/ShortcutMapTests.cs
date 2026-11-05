using Avalonia.Input;
using Shellboat.App.Settings;

namespace Shellboat.App.Tests;

public class ShortcutMapTests
{
    private static readonly Dictionary<string, string> NoOverrides = [];

    [Fact]
    public void Every_default_parses_on_both_platforms()
    {
        foreach (var action in ShortcutCatalog.Actions)
        {
            foreach (var text in new[] { action.MacDefault, action.OtherDefault })
            {
                Assert.True(text == "" || ShortcutMap.TryParse(text) is not null, $"{action.Id}: '{text}'");
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Defaults_have_no_conflicts(bool isMac)
    {
        var map = new ShortcutMap(NoOverrides, isMac);
        var gestures = ShortcutCatalog.Actions.ToDictionary(a => a.Id, a => map.GestureFor(a.Id));

        Assert.Empty(ShortcutMap.Conflicts(gestures));
    }

    [Fact]
    public void Windows_and_linux_defaults_leave_plain_ctrl_letters_and_terminal_copy_paste_to_the_shell()
    {
        var map = new ShortcutMap(NoOverrides, isMac: false);
        foreach (var action in ShortcutCatalog.Actions)
        {
            if (map.GestureFor(action.Id) is not { } g)
            {
                continue;
            }

            Assert.False(g.KeyModifiers == KeyModifiers.Control && g.Key is >= Key.A and <= Key.Z, action.Id);
            Assert.False(g.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && g.Key is Key.C or Key.V, action.Id);
        }
    }

    [Fact]
    public void Mac_defaults_never_take_cmd_c_or_cmd_v()
    {
        var map = new ShortcutMap(NoOverrides, isMac: true);

        Assert.Null(map.Match(Key.C, KeyModifiers.Meta));
        Assert.Null(map.Match(Key.V, KeyModifiers.Meta));
    }

    [Fact]
    public void Match_finds_the_default_for_the_platform()
    {
        Assert.Equal(ShortcutCatalog.CloseTab, new ShortcutMap(NoOverrides, isMac: true).Match(Key.W, KeyModifiers.Meta));
        Assert.Equal(ShortcutCatalog.CloseTab, new ShortcutMap(NoOverrides, isMac: false).Match(Key.W, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.Null(new ShortcutMap(NoOverrides, isMac: false).Match(Key.W, KeyModifiers.Control));
    }

    [Fact]
    public void An_override_replaces_the_default_and_an_empty_one_unbinds()
    {
        var map = new ShortcutMap(new Dictionary<string, string>
        {
            [ShortcutCatalog.CloseTab] = "Meta+Shift+K",
            [ShortcutCatalog.Reconnect] = "",
        }, isMac: true);

        Assert.Null(map.Match(Key.W, KeyModifiers.Meta));
        Assert.Equal(ShortcutCatalog.CloseTab, map.Match(Key.K, KeyModifiers.Meta | KeyModifiers.Shift));
        Assert.Null(map.GestureFor(ShortcutCatalog.Reconnect));
    }

    [Fact]
    public void A_garbage_override_counts_as_unbound_instead_of_throwing()
        => Assert.Null(new ShortcutMap(new Dictionary<string, string> { [ShortcutCatalog.CloseTab] = "Meta+Banana" }, true).GestureFor(ShortcutCatalog.CloseTab));

    [Theory]
    [InlineData(Key.A, KeyModifiers.None, false)]
    [InlineData(Key.A, KeyModifiers.Shift, false)]
    [InlineData(Key.LeftCtrl, KeyModifiers.Control, false)]
    [InlineData(Key.A, KeyModifiers.Control, true)]
    [InlineData(Key.A, KeyModifiers.Meta | KeyModifiers.Shift, true)]
    [InlineData(Key.F5, KeyModifiers.None, true)]
    public void Only_chords_and_function_keys_are_assignable(Key key, KeyModifiers mods, bool expected)
        => Assert.Equal(expected, ShortcutMap.IsAssignable(key, mods));

    [Fact]
    public void Display_uses_mac_symbols_or_words()
    {
        var g = ShortcutMap.TryParse("Meta+Shift+D");

        Assert.Equal("⇧⌘D", ShortcutMap.Display(g, isMac: true));
        Assert.Equal("Ctrl+Shift+,", ShortcutMap.Display(ShortcutMap.TryParse("Ctrl+Shift+OemComma"), isMac: false));
        Assert.Equal("—", ShortcutMap.Display(null, isMac: true));
    }
}
