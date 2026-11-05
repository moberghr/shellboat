using CommunityToolkit.Mvvm.Input;
using Shellboat.Core.Connections;

namespace Shellboat.App.ViewModels;

/// <summary>The tab header's context menu. Title and colour are session-only; the icon is the connection's.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>The tab's connection as currently saved (edits since the tab opened), or the tab's copy when it was
    /// deleted from the tree.</summary>
    private ConnectionEntry CurrentEntry(SessionTabViewModel tab)
        => Tree.Document.Connections.FirstOrDefault(c => c.Id == tab.Connection.Id) ?? tab.Connection;

    /// <summary>A second session of the same kind (terminal, desktop or file browser) to the same connection.</summary>
    [RelayCommand]
    private async Task DuplicateTabAsync(SessionTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        var entry = CurrentEntry(tab);
        await OpenTabAsync(tab is SftpTabViewModel ? NewSftpTab(entry) : NewSessionTab(entry));
    }

    [RelayCommand]
    private async Task OpenSftpForTabAsync(SessionTabViewModel? tab)
    {
        if (tab is { IsSsh: true })
        {
            await OpenTabAsync(NewSftpTab(CurrentEntry(tab)));
        }
    }

    [RelayCommand]
    private async Task CopyTabPasswordAsync(SessionTabViewModel? tab)
    {
        if (tab is not null)
        {
            await CopyPasswordOfAsync(CurrentEntry(tab));
        }
    }

    /// <summary>Renames the tab only; an empty name goes back to the connection's.</summary>
    [RelayCommand]
    private async Task RenameTabAsync(SessionTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        var name = await _dialogs.PromptTextAsync("Rename tab", "Tab title (empty = connection name)", tab.Title);
        if (name is null)
        {
            return;
        }

        tab.Title = string.IsNullOrWhiteSpace(name) ? CurrentEntry(tab).Name : name.Trim();
    }

    /// <summary>Changes the connection's icon (same as the tree's "Change icon…"), so every tab of it follows.</summary>
    [RelayCommand]
    private async Task ChangeTabIconAsync(SessionTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        var entry = CurrentEntry(tab);
        if (Tree.Document.Connections.All(c => c.Id != entry.Id))
        {
            StatusText = $"\"{entry.Name}\" is no longer in the connection list.";
            return;
        }

        var picker = new IconPickerViewModel(entry.Name, ConnectionGlyph.GlyphName(entry.Kind), ConnectionGlyph.BrushKey(entry.Kind), entry.Icon);
        if (await _dialogs.PickIconAsync(picker))
        {
            await SetIconAsync(entry.Id, picker.Result);
        }
    }

    [RelayCommand]
    private void ShowTabInTree(SessionTabViewModel? tab)
    {
        if (tab is not null && !Tree.Reveal(tab.Connection.Id))
        {
            StatusText = $"\"{tab.Connection.Name}\" is no longer in the connection list.";
        }
    }

    [RelayCommand]
    private async Task CloseOtherTabsAsync(SessionTabViewModel? tab)
    {
        foreach (var other in Tabs.Where(t => !ReferenceEquals(t, tab)).ToList())
        {
            await CloseTabAsync(other);
        }
    }

    /// <summary>Stores a node's icon and updates the open tabs of that connection.</summary>
    private async Task SetIconAsync(Guid id, NodeIcon? icon)
    {
        await Tree.SetIconAsync(id, icon);
        foreach (var tab in Tabs.Where(t => t.Connection.Id == id))
        {
            tab.Icon = icon;
        }
    }
}
