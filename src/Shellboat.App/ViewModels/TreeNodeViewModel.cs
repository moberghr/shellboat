using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Shellboat.Core.Connections;

namespace Shellboat.App.ViewModels;

/// <summary>A row in the connection tree: a folder or a connection.</summary>
public abstract partial class TreeNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isDropTarget;

    public abstract Guid Id { get; }

    public abstract string Title { get; }

    public abstract string Subtitle { get; }

    /// <summary>Resource key of the row glyph (Icon.Folder / Icon.Ssh / Icon.Rdp).</summary>
    public abstract string IconKey { get; }

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];
}

public sealed class FolderNodeViewModel(Folder folder) : TreeNodeViewModel
{
    public Folder Folder { get; } = folder;

    public override Guid Id => Folder.Id;

    public override string Title => Folder.Name;

    public override string Subtitle => "";

    public override string IconKey => "Icon.Folder";
}

public sealed class ConnectionNodeViewModel(ConnectionEntry entry) : TreeNodeViewModel
{
    public ConnectionEntry Entry { get; } = entry;

    public override Guid Id => Entry.Id;

    public override string Title => Entry.Name;

    public override string Subtitle => Entry.EffectivePort == ConnectionEntry.DefaultPort(Entry.Kind)
        ? Entry.Host
        : $"{Entry.Host}:{Entry.EffectivePort}";

    public override string IconKey => Entry.Kind == ConnectionKind.Rdp ? "Icon.Rdp" : "Icon.Ssh";

    public bool IsRdp => Entry.Kind == ConnectionKind.Rdp;
}
