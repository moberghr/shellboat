using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Shellboat.App.Connections;
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

    /// <summary>Theme brush key of the glyph's tint (muted for folders, the kind colour for connections), or the
    /// user's "#RRGGBB".</summary>
    public abstract string IconBrushKey { get; }

    /// <summary>What the user picked, or null for the default.</summary>
    public abstract NodeIcon? Icon { get; }

    /// <summary>The catalog glyph that matches the default look (picking it = no override).</summary>
    public abstract string DefaultGlyphName { get; }

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

    public override NodeIcon? Icon => Folder.Icon;

    public override string DefaultGlyphName => ConnectionGlyph.FolderGlyphName;

    public override string IconKey => IconCatalog.GlyphKey(Folder.Icon, ConnectionGlyph.FolderIcon);

    public override string IconBrushKey => IconCatalog.BrushKey(Folder.Icon, ConnectionGlyph.FolderBrush);
}

public sealed class ConnectionNodeViewModel(ConnectionEntry entry) : TreeNodeViewModel
{
    public ConnectionEntry Entry { get; } = entry;

    public override Guid Id => Entry.Id;

    public override string Title => Entry.Name;

    public override string Subtitle => Entry.EffectivePort == ConnectionEntry.DefaultPort(Entry.Kind)
        ? Entry.Host
        : $"{Entry.Host}:{Entry.EffectivePort}";

    public override NodeIcon? Icon => Entry.Icon;

    public override string DefaultGlyphName => ConnectionGlyph.GlyphName(Entry.Kind);

    public override string IconKey => IconCatalog.GlyphKey(Entry.Icon, ConnectionGlyph.IconKey(Entry.Kind));

    public override string IconBrushKey => IconCatalog.BrushKey(Entry.Icon, ConnectionGlyph.BrushKey(Entry.Kind));

    public bool IsRdp => Entry.Kind == ConnectionKind.Rdp;

    public override bool IsSsh => Entry.Kind == ConnectionKind.Ssh;
}
