using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shellboat.App.Services;

namespace Shellboat.App.ViewModels;

/// <summary>
/// The local (left) pane of the SFTP tab: browse this computer's folders, open files with the OS, rename, create
/// folders. Transfers between the panes live in <see cref="SftpTabViewModel"/>; this pane only reports through
/// <paramref name="report"/> (the tab's status line).
/// </summary>
public sealed partial class LocalPaneViewModel(IDialogService dialogs, Action<string> report, string initialFolder) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoUp))]
    [NotifyCanExecuteChangedFor(nameof(UpCommand))]
    private string _currentPath = "";

    [ObservableProperty]
    private string _pathInput = "";

    public ObservableCollection<LocalFileItemViewModel> Items { get; } = [];

    /// <summary>Bound to the list's multi-selection.</summary>
    public ObservableCollection<LocalFileItemViewModel> SelectedItems { get; } = [];

    public bool CanGoUp => LocalFiles.Parent(CurrentPath) is not null;

    /// <summary>Lists <see cref="CurrentPath"/> (or the initial folder the first time).</summary>
    public void Load() => Navigate(string.IsNullOrEmpty(CurrentPath) ? initialFolder : CurrentPath);

    [RelayCommand]
    public void Navigate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        path = path.Trim();
        if (path.StartsWith('~'))
        {
            path = LocalFiles.HomeFolder() + path[1..];
        }

        try
        {
            path = Path.GetFullPath(path);
            var entries = LocalFiles.List(path);
            Items.Clear();
            SelectedItems.Clear();
            foreach (var e in entries)
            {
                Items.Add(new LocalFileItemViewModel(e));
            }

            CurrentPath = path;
            PathInput = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            PathInput = CurrentPath;
            report(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void Up() => Navigate(LocalFiles.Parent(CurrentPath));

    [RelayCommand]
    private void Refresh() => Navigate(CurrentPath);

    [RelayCommand]
    private void Home() => Navigate(LocalFiles.HomeFolder());

    /// <summary>Double-click / Enter: open a folder in the pane, a file with its default app.</summary>
    [RelayCommand]
    private async Task OpenAsync(LocalFileItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            Navigate(item.Entry.FullPath);
            return;
        }

        await dialogs.OpenLocalAsync(item.Entry.FullPath);
    }

    /// <summary>Opens the row's folder (or the file's folder; or the current folder when no row) in the OS file manager.</summary>
    [RelayCommand]
    private Task ShowInFileManagerAsync(LocalFileItemViewModel? item)
    {
        var folder = item is null ? CurrentPath : item.IsDirectory ? item.Entry.FullPath : Path.GetDirectoryName(item.Entry.FullPath)!;
        return dialogs.OpenLocalAsync(folder);
    }

    [RelayCommand]
    private async Task CopyPathAsync(LocalFileItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var paths = Targets(item).Select(i => i.Entry.FullPath).ToList();
        await dialogs.SetClipboardTextAsync(string.Join("\n", paths));
        report(paths.Count == 1 ? $"Copied {paths[0]}" : $"Copied {paths.Count} paths");
    }

    [RelayCommand]
    private async Task RenameAsync(LocalFileItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var input = await dialogs.PromptTextAsync("Rename", $"New name for \"{item.Name}\"", item.Name);
        if (input is null || input == item.Name)
        {
            return;
        }

        if (LocalFiles.ValidateName(input) is { } problem)
        {
            await dialogs.ShowMessageAsync("Rename", problem);
            return;
        }

        try
        {
            LocalFiles.Rename(item.Entry.FullPath, input);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report(ex.Message);
            return;
        }

        Refresh();
        report($"Renamed {item.Name} → {input}");
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        var input = await dialogs.PromptTextAsync("New folder", $"Name of the new folder in {CurrentPath}", "New folder");
        if (input is null)
        {
            return;
        }

        if (LocalFiles.ValidateName(input) is { } problem)
        {
            await dialogs.ShowMessageAsync("New folder", problem);
            return;
        }

        try
        {
            LocalFiles.CreateFolder(CurrentPath, input);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report(ex.Message);
            return;
        }

        Refresh();
        report($"Created {input}");
    }

    [RelayCommand]
    private async Task PropertiesAsync(LocalFileItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await dialogs.ShowMessageAsync($"Properties — {item.Name}", FormatProperties(item.Entry));
    }

    public static string FormatProperties(LocalFileEntry entry)
    {
        var kind = entry.IsSymbolicLink
            ? (entry.IsDirectory ? "Link to folder" : "Link to file")
            : entry.IsDirectory ? "Folder" : "File";
        var lines = new List<string>
        {
            $"Path:  {entry.FullPath}",
            $"Type:  {kind}",
        };
        if (entry.IsDirectory)
        {
            var (files, bytes) = LocalFiles.Totals([entry.FullPath]);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Contains:  {files:N0} file(s), {RemoteFileItemViewModel.FormatSize(bytes)}"));
        }
        else
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Size:  {RemoteFileItemViewModel.FormatSize(entry.Size)} ({entry.Size:N0} bytes)"));
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"Modified:  {entry.Modified.ToLocalTime():yyyy-MM-dd HH:mm:ss}"));
        return string.Join("\n", lines);
    }

    /// <summary>The right-clicked / dragged row, or the whole selection when that row is part of it.</summary>
    public IReadOnlyList<LocalFileItemViewModel> Targets(LocalFileItemViewModel item)
        => SelectedItems.Contains(item) ? [.. SelectedItems] : [item];
}
