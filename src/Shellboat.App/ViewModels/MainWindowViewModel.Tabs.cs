using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Shellboat.Core.Connections;

namespace Shellboat.App.ViewModels;

/// <summary>The tab header's context menu. Title and colour are session-only; the icon is the connection's.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Sessions living in their own window. The view opens one window per entry and closes it when the
    /// entry goes; the session itself is the same object, so detaching never reconnects.</summary>
    public ObservableCollection<SessionTabViewModel> DetachedTabs { get; } = [];

    /// <summary>Drag-to-reorder: puts <paramref name="tab"/> at <paramref name="index"/> (clamped) and keeps it
    /// selected.</summary>
    public void MoveTab(SessionTabViewModel tab, int index)
    {
        var from = Tabs.IndexOf(tab);
        if (from < 0)
        {
            return;
        }

        var to = Math.Clamp(index, 0, Tabs.Count - 1);
        if (from != to)
        {
            Tabs.Move(from, to);
        }

        SelectedTab = tab;
    }

    /// <summary>Moves a tab out of the strip into its own window, keeping the session.</summary>
    [RelayCommand]
    private void DetachTab(SessionTabViewModel? tab)
    {
        if (tab is not null && RemoveFromStrip(tab))
        {
            DetachedTabs.Add(tab);
        }
    }

    /// <summary>Brings a detached session back as the last tab and selects it.</summary>
    [RelayCommand]
    private void AttachTab(SessionTabViewModel? tab)
    {
        if (tab is not null && DetachedTabs.Remove(tab))
        {
            Tabs.Add(tab);
            SelectedTab = tab;
        }
    }

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
        var others = Tabs.Where(t => !ReferenceEquals(t, tab)).ToList();
        if (!await ConfirmCloseAsync(others))
        {
            return;
        }

        foreach (var other in others)
        {
            await EndSessionAsync(other);
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
