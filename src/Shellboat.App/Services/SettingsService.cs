using CommunityToolkit.Mvvm.ComponentModel;
using Shellboat.App.Settings;
using Shellboat.Core.Settings;

namespace Shellboat.App.Services;

/// <summary>
/// The live preferences everyone reads (terminals, shortcuts, the theme), plus saving them. Raises
/// <see cref="ObservableObject.PropertyChanged"/> for <see cref="Current"/> and <see cref="Shortcuts"/> on every
/// change, on the caller's (UI) thread.
/// </summary>
public sealed partial class SettingsService : ObservableObject
{
    private readonly ISettingsStore? _store;

    [ObservableProperty]
    private AppSettings _current;

    [ObservableProperty]
    private ShortcutMap _shortcuts;

    /// <param name="store">Null = in memory only (tests, or a broken settings file the user hasn't fixed).</param>
    public SettingsService(ISettingsStore? store = null, AppSettings? initial = null)
    {
        _store = store;
        _current = (initial ?? AppSettings.Default).Normalize();
        _shortcuts = ShortcutMap.ForCurrentOs(_current.Shortcuts);
    }

    /// <summary>Applies and persists. A failed save keeps the new settings for this run and rethrows.</summary>
    public async Task ApplyAsync(AppSettings next, CancellationToken ct = default)
    {
        next = next.Normalize();
        Shortcuts = ShortcutMap.ForCurrentOs(next.Shortcuts);
        Current = next;
        if (_store is not null)
        {
            await _store.SaveAsync(next, ct);
        }
    }
}
