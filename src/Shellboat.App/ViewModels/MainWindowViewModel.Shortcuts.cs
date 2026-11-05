using CommunityToolkit.Mvvm.Input;
using Shellboat.App.Settings;
using Shellboat.Core.Settings;

namespace Shellboat.App.ViewModels;

/// <summary>Where a command came from: a key press in a window, or a menu item (the macOS menu bar may answer the
/// same key press too).</summary>
public enum CommandSource
{
    Keyboard,
    Menu,
}

/// <summary>The commands behind keyboard shortcuts and the main menu, all dispatched by action id.</summary>
public sealed partial class MainWindowViewModel
{
    private const double DefaultFontSize = 13;
    private (string Id, CommandSource Source, DateTime At)? _lastRun;

    /// <summary>"Filter connections": the view moves keyboard focus to the filter box.</summary>
    public event EventHandler? FocusFilterRequested;

    /// <summary>For tests: the dispatcher's clock.</summary>
    internal Func<DateTime> Clock { get; init; } = () => DateTime.UtcNow;

    /// <summary>
    /// Runs a catalog action (<see cref="ShortcutCatalog"/>) against <paramref name="context"/> — the tab of the
    /// window the key was pressed in (null = the main window's selected tab). One key press can reach us twice on
    /// macOS (window key handler and menu key equivalent), so the same action from the other source within
    /// 300 ms is dropped. Returns false for unknown ids.
    /// </summary>
    public async Task<bool> RunActionAsync(string id, SessionTabViewModel? context = null, CommandSource source = CommandSource.Keyboard)
    {
        var now = Clock();
        if (_lastRun is { } last && last.Id == id && last.Source != source && now - last.At < TimeSpan.FromMilliseconds(300))
        {
            return true;
        }

        _lastRun = (id, source, now);
        var tab = context ?? SelectedTab;
        switch (id)
        {
            case ShortcutCatalog.NewConnection:
                await NewConnectionCommand.ExecuteAsync(null);
                break;
            case ShortcutCatalog.NewFolder:
                await NewFolderCommand.ExecuteAsync(null);
                break;
            case ShortcutCatalog.FocusFilter:
                FocusFilterRequested?.Invoke(this, EventArgs.Empty);
                break;
            case ShortcutCatalog.Settings:
                await OpenSettingsCommand.ExecuteAsync(null);
                break;
            case ShortcutCatalog.CloseTab:
                await CloseTabCommand.ExecuteAsync(tab);
                break;
            case ShortcutCatalog.NextTab:
                CycleTab(+1);
                break;
            case ShortcutCatalog.PreviousTab:
                CycleTab(-1);
                break;
            case ShortcutCatalog.DuplicateTab:
                await DuplicateTabCommand.ExecuteAsync(tab);
                break;
            case ShortcutCatalog.DetachTab:
                DetachTabCommand.Execute(tab);
                break;
            case ShortcutCatalog.RenameTab:
                await RenameTabCommand.ExecuteAsync(tab);
                break;
            case ShortcutCatalog.ShowInTree:
                ShowTabInTreeCommand.Execute(tab);
                break;
            case ShortcutCatalog.OpenSftp:
                await OpenSftpForTabCommand.ExecuteAsync(tab);
                break;
            case ShortcutCatalog.Reconnect:
                if (tab is { CanReconnect: true })
                {
                    await tab.ConnectAsync();
                }

                break;
            case ShortcutCatalog.CopyPassword:
                await CopyTabPasswordCommand.ExecuteAsync(tab);
                break;
            case ShortcutCatalog.ZoomIn:
                await ZoomAsync(+1);
                break;
            case ShortcutCatalog.ZoomOut:
                await ZoomAsync(-1);
                break;
            case ShortcutCatalog.ZoomReset:
                await ZoomAsync(0);
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>Selects the next/previous tab in the strip, wrapping around.</summary>
    private void CycleTab(int step)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        var index = SelectedTab is null ? -1 : Tabs.IndexOf(SelectedTab);
        SelectedTab = Tabs[((index + step) % Tabs.Count + Tabs.Count) % Tabs.Count];
    }

    /// <summary>Terminal font size ±1 (0 = back to the default), saved like any other setting.</summary>
    private Task ZoomAsync(int step)
    {
        var size = step == 0 ? DefaultFontSize : Settings.Current.TerminalFontSize + step;
        return ApplySettingsAsync(Settings.Current with { TerminalFontSize = size });
    }

    [RelayCommand]
    private Task SetThemeAsync(ThemeMode theme) => ApplySettingsAsync(Settings.Current with { Theme = theme });

    /// <param name="section">Settings page to open on, e.g. <see cref="SettingsViewModel.KeyboardSection"/>.</param>
    [RelayCommand]
    private async Task OpenSettingsAsync(string? section)
    {
        var editor = new SettingsViewModel(Settings.Current, OperatingSystem.IsMacOS(), section);
        if (await _dialogs.EditSettingsAsync(editor))
        {
            await ApplySettingsAsync(editor.Build());
        }
    }

    [RelayCommand]
    private Task ShowAboutAsync()
    {
        var version = typeof(MainWindowViewModel).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "dev";
        return _dialogs.ShowMessageAsync("About Shellboat", $"Shellboat {version.Split('+')[0]}\nSSH and RDP connection manager.");
    }

    /// <summary>"Quit" from a menu that isn't the main window's (Windows/Linux detached windows); set by the view.</summary>
    public Action? QuitFromMenu { get; set; }

    /// <summary>Every open session: tabs and detached windows.</summary>
    public IReadOnlyList<SessionTabViewModel> AllSessions => [.. Tabs, .. DetachedTabs];

    /// <summary>Set once the user agreed to quit with live sessions, so the main window and the app shutdown
    /// don't ask twice.</summary>
    public bool QuitApproved { get; set; }

    /// <summary>Whether closing these needs the "still connected" question (sync, for Closing handlers).</summary>
    public bool NeedsCloseConfirmation(IEnumerable<SessionTabViewModel> tabs)
        => Settings.Current.ConfirmCloseLiveSessions && tabs.Any(t => t.IsConnected);

    /// <summary>Applies and saves; a failed save keeps the change for this run and says so.</summary>
    private async Task ApplySettingsAsync(AppSettings next)
    {
        try
        {
            await Settings.ApplyAsync(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Settings applied but not saved: {ex.Message}";
        }
    }
}
