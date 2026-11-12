using System.Globalization;
using Shellboat.App.Services;

namespace Shellboat.App.ViewModels;

/// <summary>One row of the SFTP tab's local (left) pane.</summary>
public sealed class LocalFileItemViewModel(LocalFileEntry entry)
{
    public LocalFileEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public bool IsDirectory => Entry.IsDirectory;

    public bool IsFile => !Entry.IsDirectory;

    public string IconKey => Entry.IsDirectory ? "Icon.Folder" : "Icon.File";

    public string IconBrushKey => Entry.IsDirectory ? "Accent.Brand" : "Text.Muted";

    public string SizeText => Entry.IsDirectory ? "" : RemoteFileItemViewModel.FormatSize(Entry.Size);

    public string ModifiedText => Entry.Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
