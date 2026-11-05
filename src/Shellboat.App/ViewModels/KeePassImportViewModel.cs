using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shellboat.App.Connections;
using Shellboat.Core.Connections;
using Shellboat.Core.Import;

namespace Shellboat.App.ViewModels;

/// <summary>One KeePass entry offered for import, with the connection it would become.</summary>
public sealed partial class ImportRowViewModel(ImportedEntry entry, ConnectionEntry preview) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = !string.IsNullOrWhiteSpace(preview.Host);

    public ImportedEntry Entry { get; } = entry;

    public ConnectionEntry Preview { get; } = preview;

    public string Group => string.Join(" / ", Entry.GroupPath);

    public string Kind => Preview.Kind.ToString().ToUpperInvariant();

    public string Target => string.IsNullOrEmpty(Preview.UserName) ? Preview.Host : $"{Preview.UserName}@{Preview.Host}";
}

/// <summary>
/// Import from a KeePass database via keepassxc-cli: unlock, review, tick, import. Groups become folders
/// under "Imported from KeePass"; passwords go to the OS keychain, never to connections.json.
/// </summary>
public sealed partial class KeePassImportViewModel : ObservableObject
{
    private readonly ICredentialImporter _importer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _databasePath = "";

    [ObservableProperty]
    private string _masterPassword = "";

    [ObservableProperty]
    private string _keyFilePath = "";

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isBusy;

    public KeePassImportViewModel(ICredentialImporter importer, bool canStoreSecrets)
    {
        _importer = importer;
        CanStoreSecrets = canStoreSecrets;
    }

    public bool CanStoreSecrets { get; }

    public ObservableCollection<ImportRowViewModel> Rows { get; } = [];

    /// <summary>Set when the user confirms; the caller applies it to the tree.</summary>
    public IReadOnlyList<ImportRowViewModel>? Accepted { get; private set; }

    private bool CanUnlock => !IsBusy && DatabasePath.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        IsBusy = true;
        Status = "Reading database…";
        Rows.Clear();
        try
        {
            var entries = await _importer.ReadAsync(DatabasePath.Trim(), MasterPassword,
                string.IsNullOrWhiteSpace(KeyFilePath) ? null : KeyFilePath.Trim(), CancellationToken.None);
            foreach (var e in entries)
            {
                Rows.Add(new ImportRowViewModel(e, KeePassImportMapper.ToConnection(e, null)));
            }

            Status = $"{Rows.Count} entries. Tick the ones to import as connections.";
        }
        catch (CredentialImportException ex)
        {
            Status = ex.Message;
        }
        finally
        {
            MasterPassword = "";
            IsBusy = false;
        }
    }

    public void Accept() => Accepted = [.. Rows.Where(r => r.IsSelected)];
}
