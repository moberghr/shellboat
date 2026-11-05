using System.Text;
using Shellboat.App.Services;
using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;
using Shellboat.Core.Secrets;
using Shellboat.Core.Sessions;

namespace Shellboat.App.Tests;

public sealed class SftpTabViewModelTests : IDisposable
{
    private static readonly ConnectionEntry Entry = new()
    {
        Id = Guid.NewGuid(),
        Name = "web",
        Kind = ConnectionKind.Ssh,
        Host = "web.example",
        UserName = "u",
        Ssh = new SshSettings(),
    };

    private readonly string _downloads = Directory.CreateTempSubdirectory("shellboat-sftp-").FullName;

    public void Dispose() => Directory.Delete(_downloads, recursive: true);

    private (SftpTabViewModel Vm, FakeFileChannel Server, FakeDialogs Dialogs) Create(Func<SshCredentials, Exception?>? outcome = null)
    {
        var secrets = new FakeSecrets();
        secrets.Items[(Entry.Id, SecretPurpose.Password)] = "pw";
        var dialogs = new FakeDialogs();
        FakeFileChannel? server = null;
        var vm = new SftpTabViewModel(Entry, (_, creds) =>
        {
            server = new FakeFileChannel(outcome ?? (_ => null), creds);
            Seed(server);
            return server;
        }, new CredentialResolver(secrets, dialogs), dialogs, () => _downloads);
        return (vm, server!, dialogs);

        static void Seed(FakeFileChannel s)
        {
            s.AddDir("/home");
            s.AddDir("/home/u");
            s.AddFile("/home/u/notes.txt", "hello");
            s.AddDir("/home/u/logs");
            s.AddFile("/home/u/logs/app.log", "line1\nline2");
            s.AddDir("/home/u/logs/old");
            s.AddFile("/home/u/logs/old/app.1.log", "older");
            s.AddDir("/home/u/loop", isLink: true);
            s.AddFile("/home/u/evil", "pwned", hostileName: "../evil");
        }
    }

    private static FakeFileChannel Server(SftpTabViewModel vm)
        => (FakeFileChannel)typeof(SftpTabViewModel).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;

    [Fact]
    public async Task Connect_lists_home_with_folders_first_and_up_goes_to_the_parent()
    {
        var (vm, _, _) = Create();

        await vm.ConnectAsync();

        Assert.Equal(SessionState.Connected, vm.State);
        Assert.Equal("/home/u", vm.CurrentPath);
        Assert.Equal(["logs", "loop", "../evil", "notes.txt"], vm.Items.Select(i => i.Name));
        Assert.Equal("web — SFTP", vm.Title);

        await vm.UpCommand.ExecuteAsync(null);
        Assert.Equal("/home", vm.CurrentPath);
        Assert.Equal(["u"], vm.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Opening_a_file_downloads_it_to_the_default_folder_without_overwriting()
    {
        var (vm, _, _) = Create();
        await vm.ConnectAsync();
        var notes = vm.Items.Single(i => i.Name == "notes.txt");

        await vm.OpenCommand.ExecuteAsync(notes);
        await vm.OpenCommand.ExecuteAsync(notes);

        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(_downloads, "notes.txt")));
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(_downloads, "notes (1).txt")));
    }

    [Fact]
    public async Task Opening_a_folder_navigates_into_it()
    {
        var (vm, _, _) = Create();
        await vm.ConnectAsync();

        await vm.OpenCommand.ExecuteAsync(vm.Items.Single(i => i.Name == "logs"));

        Assert.Equal("/home/u/logs", vm.CurrentPath);
        Assert.Equal(["old", "app.log"], vm.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Selected_folders_download_recursively_and_symlinked_folders_are_not_followed()
    {
        var (vm, _, dialogs) = Create();
        await vm.ConnectAsync();
        var target = Path.Combine(_downloads, "picked");
        dialogs.PickedFolder = target;
        vm.SelectedItems.Add(vm.Items.Single(i => i.Name == "logs"));
        vm.SelectedItems.Add(vm.Items.Single(i => i.Name == "loop"));

        await vm.DownloadToCommand.ExecuteAsync(null);

        Assert.Equal("line1\nline2", await File.ReadAllTextAsync(Path.Combine(target, "logs", "app.log")));
        Assert.Equal("older", await File.ReadAllTextAsync(Path.Combine(target, "logs", "old", "app.1.log")));
        Assert.False(Directory.Exists(Path.Combine(target, "loop")));
        Assert.Equal(2, vm.LastDownload!.Files);
        Assert.Equal(1, vm.LastDownload.SkippedLinks);
        Assert.Contains("1 linked folder(s) not followed", vm.Message);
    }

    [Fact]
    public async Task A_hostile_remote_name_stays_inside_the_download_folder()
    {
        var (vm, _, _) = Create();
        await vm.ConnectAsync();

        await vm.OpenCommand.ExecuteAsync(vm.Items.Single(i => i.Name == "../evil"));

        var saved = Assert.Single(vm.LastDownload!.SavedPaths);
        Assert.Equal(_downloads, Path.GetDirectoryName(saved));
        Assert.Equal("pwned", await File.ReadAllTextAsync(saved));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_downloads)!, "evil")));
    }

    [Fact]
    public async Task A_failed_download_leaves_no_partial_file()
    {
        var (vm, _, _) = Create();
        await vm.ConnectAsync();
        Server(vm).FailDownloadOf = "/home/u/notes.txt";

        await vm.OpenCommand.ExecuteAsync(vm.Items.Single(i => i.Name == "notes.txt"));

        Assert.Empty(Directory.GetFiles(_downloads));
        Assert.Equal("transfer failed", vm.Message);
        Assert.Equal(SessionState.Connected, vm.State);
        Assert.False(vm.IsTransferring);
    }

    [Fact]
    public async Task A_dropped_connection_turns_the_tab_into_reconnect()
    {
        var (vm, _, _) = Create();
        await vm.ConnectAsync();
        Server(vm).IsConnected = false;

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(SessionState.Disconnected, vm.State);
        Assert.True(vm.CanReconnect);

        await vm.ConnectAsync();
        Assert.Equal(SessionState.Connected, vm.State);
        Assert.Equal("/home/u", vm.CurrentPath);
    }

    [Fact]
    public async Task Rejected_host_key_fails_without_retrying_and_tells_the_user()
    {
        var attempts = 0;
        var (vm, _, dialogs) = Create(_ =>
        {
            attempts++;
            return new HostKeyRejectedException("key changed");
        });

        await vm.ConnectAsync();

        Assert.Equal(SessionState.Failed, vm.State);
        Assert.Equal(1, attempts);
        Assert.Equal("key changed", Assert.Single(dialogs.Messages));
    }

    [Fact]
    public async Task Download_needs_a_selection()
    {
        var (vm, _, _) = Create();
        await vm.ConnectAsync();

        Assert.False(vm.DownloadCommand.CanExecute(null));
        vm.SelectedItems.Add(vm.Items[0]);
        Assert.True(vm.DownloadCommand.CanExecute(null));
    }
}

public sealed class SftpContextMenuTests : IDisposable
{
    private static readonly ConnectionEntry Entry = new()
    {
        Id = Guid.NewGuid(),
        Name = "web",
        Kind = ConnectionKind.Ssh,
        Host = "web.example",
        UserName = "u",
        Ssh = new SshSettings(),
    };

    private readonly string _downloads = Directory.CreateTempSubdirectory("shellboat-sftp-menu-").FullName;
    private FakeFileChannel? _server;

    public void Dispose() => Directory.Delete(_downloads, recursive: true);

    private async Task<(SftpTabViewModel Vm, FakeDialogs Dialogs)> ConnectAsync()
    {
        var secrets = new FakeSecrets();
        secrets.Items[(Entry.Id, SecretPurpose.Password)] = "pw";
        var dialogs = new FakeDialogs();
        var vm = new SftpTabViewModel(Entry, (_, creds) =>
        {
            _server = new FakeFileChannel(_ => null, creds);
            _server.AddDir("/home");
            _server.AddDir("/home/u");
            _server.AddFile("/home/u/a.txt", "AAA");
            _server.AddFile("/home/u/b.txt", "BBB");
            _server.AddFile("/home/u/c.txt", "CCC");
            return _server;
        }, new CredentialResolver(secrets, dialogs), dialogs, () => _downloads);
        await vm.ConnectAsync();
        return (vm, dialogs);
    }

    private static RemoteFileItemViewModel Item(SftpTabViewModel vm, string name) => vm.Items.Single(i => i.Name == name);

    [Fact]
    public async Task Download_on_an_unselected_row_takes_just_that_row()
    {
        var (vm, _) = await ConnectAsync();
        vm.SelectedItems.Add(Item(vm, "a.txt"));

        await vm.DownloadItemCommand.ExecuteAsync(Item(vm, "c.txt"));

        Assert.Equal(["c.txt"], Directory.GetFiles(_downloads).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Download_on_a_selected_row_takes_the_whole_selection()
    {
        var (vm, dialogs) = await ConnectAsync();
        vm.SelectedItems.Add(Item(vm, "a.txt"));
        vm.SelectedItems.Add(Item(vm, "b.txt"));
        var target = Path.Combine(_downloads, "picked");
        dialogs.PickedFolder = target;

        await vm.DownloadItemToCommand.ExecuteAsync(Item(vm, "b.txt"));

        Assert.Equal(["a.txt", "b.txt"], Directory.GetFiles(target).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Copy_path_puts_the_full_remote_path_on_the_clipboard()
    {
        var (vm, dialogs) = await ConnectAsync();

        await vm.CopyPathCommand.ExecuteAsync(Item(vm, "a.txt"));
        Assert.Equal("/home/u/a.txt", dialogs.Clipboard);

        vm.SelectedItems.Add(Item(vm, "a.txt"));
        vm.SelectedItems.Add(Item(vm, "c.txt"));
        await vm.CopyPathCommand.ExecuteAsync(Item(vm, "c.txt"));
        Assert.Equal("/home/u/a.txt\n/home/u/c.txt", dialogs.Clipboard);
    }

    [Fact]
    public async Task Rename_renames_in_the_same_folder_and_refreshes()
    {
        var (vm, dialogs) = await ConnectAsync();
        dialogs.TextAnswers.Enqueue("renamed.txt");

        await vm.RenameCommand.ExecuteAsync(Item(vm, "a.txt"));

        Assert.Equal(("/home/u/a.txt", "/home/u/renamed.txt"), Assert.Single(_server!.Renames));
        Assert.Contains(vm.Items, i => i.Name == "renamed.txt");
        Assert.DoesNotContain(vm.Items, i => i.Name == "a.txt");
    }

    [Fact]
    public async Task Rename_onto_an_existing_name_is_refused_and_reported()
    {
        var (vm, dialogs) = await ConnectAsync();
        dialogs.TextAnswers.Enqueue("b.txt");

        await vm.RenameCommand.ExecuteAsync(Item(vm, "a.txt"));

        Assert.Empty(_server!.Renames);
        Assert.Contains("already exists", vm.Message);
        Assert.Equal("BBB", System.Text.Encoding.UTF8.GetString(_server.Files["/home/u/b.txt"]));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("sub/dir.txt")]
    [InlineData("..")]
    [InlineData("   ")]
    public async Task Rename_rejects_names_that_would_move_the_file(string name)
    {
        var (vm, dialogs) = await ConnectAsync();
        dialogs.TextAnswers.Enqueue(name);

        await vm.RenameCommand.ExecuteAsync(Item(vm, "a.txt"));

        Assert.Empty(_server!.Renames);
        Assert.Single(dialogs.Messages);
    }

    [Fact]
    public async Task Cancelled_or_unchanged_rename_does_nothing()
    {
        var (vm, dialogs) = await ConnectAsync();
        dialogs.TextAnswers.Enqueue(null);
        dialogs.TextAnswers.Enqueue("a.txt");

        await vm.RenameCommand.ExecuteAsync(Item(vm, "a.txt"));
        await vm.RenameCommand.ExecuteAsync(Item(vm, "a.txt"));

        Assert.Empty(_server!.Renames);
        Assert.Empty(dialogs.Messages);
    }

    [Fact]
    public async Task Properties_shows_path_size_permissions_and_owner()
    {
        var (vm, dialogs) = await ConnectAsync();
        _server!.Properties["/home/u/a.txt"] = new RemoteFileProperties(
            "/home/u/a.txt", false, false, 1536, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Convert.ToInt32("644", 8), 1000, 1000);

        await vm.PropertiesCommand.ExecuteAsync(Item(vm, "a.txt"));

        var text = Assert.Single(dialogs.Messages);
        Assert.Contains("Path:  /home/u/a.txt", text);
        Assert.Contains("Type:  File", text);
        Assert.Contains("1.5 KB (1,536 bytes)", text);
        Assert.Contains("-rw-r--r-- (0644)", text);
        Assert.Contains("uid 1000, gid 1000", text);
    }

    [Theory]
    [InlineData("755", true, false, "drwxr-xr-x (0755)")]
    [InlineData("4755", false, false, "-rwsr-xr-x (4755)")]
    [InlineData("1777", true, false, "drwxrwxrwt (1777)")]
    [InlineData("2644", false, false, "-rw-r-Sr-- (2644)")]
    [InlineData("777", false, true, "lrwxrwxrwx (0777)")]
    public void Mode_formats_like_ls(string octal, bool dir, bool link, string expected)
        => Assert.Equal(expected, RemoteFileItemViewModel.FormatMode(Convert.ToInt32(octal, 8), dir, link));
}
