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

    /// <summary>Resource key of the glyph's tint (muted for folders, the kind colour for connections).</summary>
    public abstract string IconBrushKey { get; }

    /// <summary>SSH connection rows get the "Open SFTP" menu item.</summary>
    public virtual bool IsSsh => false;

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];
}

public sealed class FolderNodeViewModel(Folder folder) : TreeNodeViewModel
{
    public Folder Folder { get; } = folder;

    public override Guid Id => Folder.Id;

    public override string Title => Folder.Name;

    public override string Subtitle => "";

    public override string IconKey => ConnectionGlyph.FolderIcon;

    public override string IconBrushKey => ConnectionGlyph.FolderBrush;
}

public sealed class ConnectionNodeViewModel(ConnectionEntry entry) : TreeNodeViewModel
{
    public ConnectionEntry Entry { get; } = entry;

    public override Guid Id => Entry.Id;

    public override string Title => Entry.Name;

    public override string Subtitle => Entry.EffectivePort == ConnectionEntry.DefaultPort(Entry.Kind)
        ? Entry.Host
        : $"{Entry.Host}:{Entry.EffectivePort}";

    public override string IconKey => ConnectionGlyph.IconKey(Entry.Kind);

    public override string IconBrushKey => ConnectionGlyph.BrushKey(Entry.Kind);

    public bool IsRdp => Entry.Kind == ConnectionKind.Rdp;

    public override bool IsSsh => Entry.Kind == ConnectionKind.Ssh;
}
