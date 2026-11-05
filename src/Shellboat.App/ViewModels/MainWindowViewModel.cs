using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shellboat.App.Connections;
using Shellboat.App.Services;
using Shellboat.Core.Connections;
using Shellboat.Core.Import;
using Shellboat.Core.Secrets;
using Shellboat.Persistence.Import;

namespace Shellboat.App.ViewModels;

/// <summary>
/// The shell: connection tree on the left, session tabs on the right. Keep this a coordinator — tree rules
/// live in <see cref="ConnectionTreeViewModel"/>/<see cref="ConnectionTreeEditor"/>, session logic in the tab
/// view models.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ISecretStore _secrets;
    private readonly IDialogService _dialogs;
    private readonly CredentialResolver _credentials;
    private readonly ShellChannelFactory _shellFactory;
    private readonly FileChannelFactory _fileFactory;
    private readonly ICredentialImporter _importer;
    private readonly Func<string, CancellationToken, Task<RoyalImportResult>> _royalReader;
    private readonly Func<string, CancellationToken, Task<AsbruImportResult>> _asbruReader;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTabs))]
    private SessionTabViewModel? _selectedTab;

    [ObservableProperty]
    private string _statusText = "";

    public MainWindowViewModel(
        ConnectionTreeViewModel tree,
        ISecretStore secrets,
        IDialogService dialogs,
        ShellChannelFactory shellFactory,
        FileChannelFactory fileFactory,
        ICredentialImporter importer,
        Func<string, CancellationToken, Task<RoyalImportResult>>? royalReader = null,
        Func<string, CancellationToken, Task<AsbruImportResult>>? asbruReader = null,
        SettingsService? settings = null)
    {
        Settings = settings ?? new SettingsService();
        _royalReader = royalReader ?? RoyalTsImporter.ReadFileAsync;
        _asbruReader = asbruReader ?? AsbruImporter.ReadFileAsync;
        Tree = tree;
        _secrets = secrets;
        _dialogs = dialogs;
        _shellFactory = shellFactory;
        _fileFactory = fileFactory;
        _importer = importer;
        _credentials = new CredentialResolver(secrets, dialogs);
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTabs));
        if (!secrets.CanStore)
        {
            StatusText = $"No system keychain — passwords will be asked for on connect and not saved. ({secrets.UnavailableReason})";
        }
    }

    public ConnectionTreeViewModel Tree { get; }

    /// <summary>Live preferences (theme, terminal, shortcuts, confirmations).</summary>
    public SettingsService Settings { get; }

    public ObservableCollection<SessionTabViewModel> Tabs { get; } = [];

    public bool HasTabs => Tabs.Count > 0;

    [RelayCommand]
    private async Task ConnectAsync(TreeNodeViewModel? node)
    {
        if ((node ?? Tree.Selected) is not ConnectionNodeViewModel c)
        {
            return;
        }

        await OpenTabAsync(NewSessionTab(c.Entry));
    }

    private SessionTabViewModel NewSessionTab(ConnectionEntry entry) => entry.Kind == ConnectionKind.Rdp
        ? new RdpTabViewModel(entry, _credentials, _dialogs)
        : new TerminalTabViewModel(entry, _shellFactory, _credentials, _dialogs, Settings);

    private SftpTabViewModel NewSftpTab(ConnectionEntry entry) => new(entry, _fileFactory, _credentials, _dialogs);

    /// <summary>File browser (SFTP) for an SSH connection, in its own tab.</summary>
    [RelayCommand]
    private async Task OpenSftpAsync(TreeNodeViewModel? node)
    {
        if ((node ?? Tree.Selected) is not ConnectionNodeViewModel { IsSsh: true } c)
        {
            return;
        }

        await OpenTabAsync(NewSftpTab(c.Entry));
    }

    /// <summary>How long a copied password stays on the clipboard before it is wiped.</summary>
    /// <summary>Settings → Sessions; tests pin it with an init value.</summary>
    public TimeSpan PasswordClipboardLifetime
    {
        get => _passwordClipboardLifetime ?? TimeSpan.FromSeconds(Settings.Current.PasswordClipboardSeconds);
        init => _passwordClipboardLifetime = value;
    }

    private readonly TimeSpan? _passwordClipboardLifetime;

    /// <summary>The pending clipboard wipe of the last copied password (awaited by tests).</summary>
    internal Task PendingClipboardClear { get; private set; } = Task.CompletedTask;

    /// <summary>Saved password to the clipboard (e.g. for sudo), wiped after <see cref="PasswordClipboardLifetime"/>
    /// unless the user has copied something else since. The secret never reaches the status bar.</summary>
    [RelayCommand]
    private async Task CopyPasswordAsync(TreeNodeViewModel? node)
    {
        if ((node ?? Tree.Selected) is ConnectionNodeViewModel c)
        {
            await CopyPasswordOfAsync(c.Entry);
        }
    }

    private async Task CopyPasswordOfAsync(ConnectionEntry entry)
    {
        // Key-auth connections never store a login password, but sudo still wants one: ask once, and with
        // "remember" it lands in the (otherwise unused) Password slot for next time.
        var password = await _credentials.GetSavedAsync(entry, SecretPurpose.Password, CancellationToken.None)
            ?? await _credentials.PromptAsync(entry, SecretPurpose.Password,
                "No password is saved for this connection. Enter it to copy it (e.g. for sudo).", CancellationToken.None);
        if (string.IsNullOrEmpty(password))
        {
            StatusText = $"No password copied for \"{entry.Name}\".";
            return;
        }

        await _dialogs.SetClipboardTextAsync(password);
        StatusText = $"Password for \"{entry.Name}\" copied — the clipboard clears in {PasswordClipboardLifetime.TotalSeconds:0} s.";
        PendingClipboardClear = ClearPasswordLaterAsync(password);
    }

    private async Task ClearPasswordLaterAsync(string password)
    {
        await Task.Delay(PasswordClipboardLifetime);
        await _dialogs.ClearClipboardIfAsync(password);
    }

    private async Task OpenTabAsync(SessionTabViewModel tab)
    {
        // The session ended by itself (`exit`, server closed it): no "are you sure?".
        tab.CloseRequested += async (_, _) => await EndSessionAsync(tab);
        Tabs.Add(tab);
        SelectedTab = tab;
        await tab.ConnectAsync();
    }

    /// <summary>The user closes a tab (✕, menu, shortcut): asks first when it is still connected and Settings say
    /// so, then ends it wherever it lives (tab strip or detached window).</summary>
    [RelayCommand]
    private async Task CloseTabAsync(SessionTabViewModel? tab)
    {
        if (tab is not null && await ConfirmCloseAsync([tab]))
        {
            await EndSessionAsync(tab);
        }
    }

    /// <summary>Ends a session without asking: disposes it and removes its tab or window entry.</summary>
    public async Task EndSessionAsync(SessionTabViewModel tab)
    {
        if (!(RemoveFromStrip(tab) || DetachedTabs.Remove(tab)))
        {
            return;
        }

        await tab.DisposeAsync();
    }

    /// <summary>True when closing these may go ahead: nothing is connected, Settings don't ask, or the user said
    /// yes. One question for any number of sessions.</summary>
    public async Task<bool> ConfirmCloseAsync(IReadOnlyCollection<SessionTabViewModel> tabs)
    {
        var live = tabs.Where(t => t.IsConnected).ToList();
        if (live.Count == 0 || !Settings.Current.ConfirmCloseLiveSessions)
        {
            return true;
        }

        var what = live.Count == 1 ? $"\"{live[0].Title}\" is still connected." : $"{live.Count} sessions are still connected.";
        return await _dialogs.ConfirmAsync("Close session", $"{what} Close anyway?", live.Count == 1 ? "Close" : "Close all");
    }

    /// <summary>Takes a tab out of the strip (session untouched) and selects its neighbour. False if it isn't there.</summary>
    private bool RemoveFromStrip(SessionTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return false;
        }

        Tabs.RemoveAt(index);
        if (ReferenceEquals(SelectedTab, tab) || SelectedTab is null)
        {
            SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        }

        return true;
    }

    [RelayCommand]
    private async Task NewConnectionAsync()
    {
        var editor = new ConnectionEditorViewModel(null, Tree.TargetFolderId, _secrets.CanStore, hasSavedSecret: false);
        await EditAsync(editor);
    }

    [RelayCommand]
    private async Task EditAsync(TreeNodeViewModel? node)
    {
        node ??= Tree.Selected;
        if (node is FolderNodeViewModel folder)
        {
            await RenameFolderAsync(folder);
            return;
        }

        if (node is not ConnectionNodeViewModel c)
        {
            return;
        }

        var purpose = c.Entry.Ssh?.Auth == SshAuthMethod.PrivateKey ? SecretPurpose.KeyPassphrase : SecretPurpose.Password;
        var saved = await _credentials.GetSavedAsync(c.Entry, purpose, CancellationToken.None) is not null;
        await EditAsync(new ConnectionEditorViewModel(c.Entry, null, _secrets.CanStore, saved));
    }

    [RelayCommand]
    private async Task DuplicateAsync(TreeNodeViewModel? node)
    {
        if ((node ?? Tree.Selected) is not ConnectionNodeViewModel c)
        {
            return;
        }

        await Tree.UpsertAsync(c.Entry with { Id = Guid.NewGuid(), Name = c.Entry.Name + " (copy)" });
    }

    [RelayCommand]
    private async Task ChangeIconAsync(TreeNodeViewModel? node)
    {
        node ??= Tree.Selected;
        if (node is null)
        {
            return;
        }

        var defaultBrush = node is ConnectionNodeViewModel c ? ConnectionGlyph.BrushKey(c.Entry.Kind) : ConnectionGlyph.FolderBrush;
        var picker = new IconPickerViewModel(node.Title, node.DefaultGlyphName, defaultBrush, node.Icon);
        if (await _dialogs.PickIconAsync(picker))
        {
            await SetIconAsync(node.Id, picker.Result);
        }
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        var name = await _dialogs.PromptTextAsync("New folder", "Folder name", "");
        if (!string.IsNullOrWhiteSpace(name))
        {
            await Tree.AddFolderAsync(name.Trim(), Tree.TargetFolderId);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(TreeNodeViewModel? node)
    {
        node ??= Tree.Selected;
        switch (node)
        {
            case ConnectionNodeViewModel c when await _dialogs.ConfirmAsync("Delete connection", $"Delete \"{c.Title}\" and its saved password?", "Delete"):
                await Tree.DeleteConnectionAsync(c.Id);
                break;
            case FolderNodeViewModel f when await _dialogs.ConfirmAsync("Delete folder", $"Delete folder \"{f.Title}\" and everything in it?", "Delete"):
                await Tree.DeleteFolderAsync(f.Id);
                break;
        }
    }

    [RelayCommand]
    private async Task ImportKeePassAsync()
    {
        var import = new KeePassImportViewModel(_importer, _secrets.CanStore);
        await _dialogs.ShowKeePassImportAsync(import);
        if (import.Accepted is not { Count: > 0 } rows)
        {
            return;
        }

        var (doc, root) = ConnectionTreeEditor.EnsureFolderPath(Tree.Document, null, ["Imported from KeePass"]);
        var saved = 0;
        foreach (var row in rows)
        {
            (doc, var folderId) = ConnectionTreeEditor.EnsureFolderPath(doc, root, row.Entry.GroupPath);
            var entry = row.Preview with { FolderId = folderId };
            doc = ConnectionTreeEditor.Upsert(doc, entry);
            if (_secrets.CanStore && !string.IsNullOrEmpty(row.Entry.Password))
            {
                try
                {
                    await _secrets.SetAsync(entry.Id, SecretPurpose.Password, row.Entry.Password, CancellationToken.None);
                    saved++;
                }
                catch (Exception)
                {
                    // Counted below: the connection imports, its password is asked for on connect.
                }
            }
        }

        await Tree.ApplyAsync(doc, root);
        StatusText = $"Imported {rows.Count} connection(s); {saved} password(s) saved to the keychain.";
    }

    [RelayCommand]
    private async Task ImportRoyalAsync()
    {
        var path = await _dialogs.PickFileAsync("Import Royal TS / Royal TSX document", "Royal document", ["*.rtsz", "*.rtsx"]);
        if (path is null)
        {
            return;
        }

        RoyalImportResult result;
        try
        {
            result = await _royalReader(path, CancellationToken.None);
        }
        catch (Exception ex) when (ex is CredentialImportException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Import failed", ex.Message);
            return;
        }

        var (doc, root) = ConnectionTreeEditor.EnsureFolderPath(Tree.Document, null, ["Imported from Royal TSX"]);
        var (merged, added, updated) = ConnectionTreeEditor.Merge(doc, result.Tree, root);
        await Tree.ApplyAsync(merged, root);

        var notes = new List<string> { $"{added} new, {updated} updated connection(s)." };
        if (result.EncryptedPasswordsNotImported > 0)
        {
            notes.Add($"{result.EncryptedPasswordsNotImported} saved password(s) were not imported — Royal encrypts them. You'll be asked on first connect (tick \"save\"), or import them from KeePass.");
        }

        if (result.SkippedUnsupported > 0)
        {
            notes.Add($"{result.SkippedUnsupported} connection(s) of a type Shellboat doesn't support yet (telnet, serial, VNC, web…) were skipped.");
        }

        if (result.SkippedTemplatesAndTrash > 0)
        {
            notes.Add($"{result.SkippedTemplatesAndTrash} template(s) / trashed item(s) were skipped.");
        }

        StatusText = notes[0];
        await _dialogs.ShowMessageAsync("Royal TSX import", string.Join("\n\n", notes));
    }

    [RelayCommand]
    private async Task ImportAsbruAsync()
    {
        var path = await _dialogs.PickFileAsync("Import Ásbrú Connection Manager export", "Ásbrú export", ["*.yml", "*.yaml"]);
        if (path is null)
        {
            return;
        }

        AsbruImportResult result;
        try
        {
            result = await _asbruReader(path, CancellationToken.None);
        }
        catch (Exception ex) when (ex is CredentialImportException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Import failed", ex.Message);
            return;
        }

        var outcome = await AsbruImportApplier.ApplyAsync(Tree, _secrets, result, CancellationToken.None);
        StatusText = outcome.Notes[0];
        await _dialogs.ShowMessageAsync("Ásbrú import", string.Join("\n\n", outcome.Notes));
    }

    public Task MoveAsync(TreeNodeViewModel dragged, FolderNodeViewModel? target) => dragged switch
    {
        ConnectionNodeViewModel c => Tree.MoveConnectionAsync(c.Id, target?.Id),
        FolderNodeViewModel f => Tree.MoveFolderAsync(f.Id, target?.Id),
        _ => Task.CompletedTask,
    };

    private async Task RenameFolderAsync(FolderNodeViewModel folder)
    {
        var name = await _dialogs.PromptTextAsync("Rename folder", "Folder name", folder.Title);
        if (!string.IsNullOrWhiteSpace(name))
        {
            await Tree.RenameFolderAsync(folder.Id, name.Trim());
        }
    }

    private async Task EditAsync(ConnectionEditorViewModel editor)
    {
        if (!await _dialogs.EditConnectionAsync(editor) || editor.TryBuild() is not { } entry)
        {
            return;
        }

        await Tree.UpsertAsync(entry);
        await SaveSecretsAsync(editor, entry);
    }

    private async Task SaveSecretsAsync(ConnectionEditorViewModel editor, ConnectionEntry entry)
    {
        var secret = editor.ShowKey ? editor.KeyPassphrase : editor.Password;
        var purpose = editor.ShowKey ? SecretPurpose.KeyPassphrase : SecretPurpose.Password;
        if (string.IsNullOrEmpty(secret) || !editor.RememberSecret || !_secrets.CanStore)
        {
            return;
        }

        try
        {
            await _secrets.SetAsync(entry.Id, purpose, secret, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Not saved", $"The connection was saved, but the secret could not be stored in the keychain: {ex.Message}");
        }
    }
}
