using Shellboat.Core.Settings;
using Shellboat.Persistence;

namespace Shellboat.Persistence.Tests;

public class JsonSettingsStoreTests
{
    [Fact]
    public async Task A_missing_file_loads_the_defaults()
    {
        using var dir = new TempDir();

        var loaded = await new JsonSettingsStore(dir.File("settings.json")).LoadAsync(CancellationToken.None);

        Assert.Equal(AppSettings.Default.Theme, loaded.Theme);
        Assert.Equal(AppSettings.Default.TerminalFontSize, loaded.TerminalFontSize);
    }

    [Fact]
    public async Task Saved_settings_round_trip_including_shortcut_overrides()
    {
        using var dir = new TempDir();
        var store = new JsonSettingsStore(dir.File("settings.json"));
        var settings = AppSettings.Default with
        {
            Theme = ThemeMode.Light,
            TerminalColors = TerminalColorMode.Dark,
            TerminalBackground = "#2B2B2B",
            TerminalFontFamily = "Fira Code",
            TerminalFontSize = 15,
            CopyOnSelect = false,
            RightClickPaste = false,
            ScrollbackLines = 20000,
            PasswordClipboardSeconds = 60,
            ConfirmCloseLiveSessions = false,
            Shortcuts = new Dictionary<string, string> { ["close-tab"] = "Meta+Shift+K", ["reconnect"] = "" },
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(settings with { Shortcuts = loaded.Shortcuts }, loaded);
        Assert.Equal("Meta+Shift+K", loaded.Shortcuts["close-tab"]);
        Assert.Equal("", loaded.Shortcuts["reconnect"]);
        Assert.Contains("\"Light\"", await File.ReadAllTextAsync(dir.File("settings.json"))); // enums as names
    }

    [Fact]
    public async Task A_corrupt_file_throws_instead_of_silently_resetting()
    {
        using var dir = new TempDir();
        await File.WriteAllTextAsync(dir.File("settings.json"), "{ not json");

        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonSettingsStore(dir.File("settings.json")).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_file_from_a_newer_version_is_refused()
    {
        using var dir = new TempDir();
        await File.WriteAllTextAsync(dir.File("settings.json"), "{\"version\":99,\"settings\":{}}");

        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonSettingsStore(dir.File("settings.json")).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Out_of_range_hand_edits_are_clamped_on_load()
    {
        using var dir = new TempDir();
        await File.WriteAllTextAsync(dir.File("settings.json"),
            "{\"version\":1,\"settings\":{\"terminalFontSize\":200,\"scrollbackLines\":-5,\"passwordClipboardSeconds\":0,\"terminalFontFamily\":\"  \"}}");

        var loaded = await new JsonSettingsStore(dir.File("settings.json")).LoadAsync(CancellationToken.None);

        Assert.Equal(AppSettings.MaxFontSize, loaded.TerminalFontSize);
        Assert.Equal(AppSettings.MinScrollback, loaded.ScrollbackLines);
        Assert.Equal(AppSettings.MinClipboardSeconds, loaded.PasswordClipboardSeconds);
        Assert.Null(loaded.TerminalFontFamily);
    }
}
