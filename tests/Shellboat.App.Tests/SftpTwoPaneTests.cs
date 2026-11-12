using System.Text;
using Shellboat.App.Services;
using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;
using Shellboat.Core.Secrets;
using Shellboat.Core.Sessions;

namespace Shellboat.App.Tests;

/// <summary>The SFTP tab's local (left) pane, transfers between the panes, and the transfer queue.</summary>
public sealed class SftpTwoPaneTests : IDisposable
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

    private readonly string _local = Directory.CreateTempSubdirectory("shellboat-2pane-").FullName;

    public void Dispose() => Directory.Delete(_local, recursive: true);

    private async Task<(SftpTabViewModel Vm, FakeFileChannel Server, FakeDialogs Dialogs)> ConnectedAsync()
    {
        var secrets = new FakeSecrets();
        secrets.Items[(Entry.Id, SecretPurpose.Password)] = "pw";
        var dialogs = new FakeDialogs();
        FakeFileChannel? server = null;
        var vm = new SftpTabViewModel(Entry, (_, creds) =>
        {
            server = new FakeFileChannel(_ => null, creds);
            server.AddDir("/home");
            server.AddDir("/home/u");
            server.AddFile("/home/u/notes.txt", "hello");
            server.AddDir("/home/u/logs");
            server.AddFile("/home/u/logs/app.log", "line1\nline2");
            server.AddDir("/home/u/www");
            return server;
        }, new CredentialResolver(secrets, dialogs), dialogs, () => _local);
        await vm.ConnectAsync();
        return (vm, server!, dialogs);
    }

    private string Local(string relative, string content = "")
    {
        var path = Path.Combine(_local, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task The_left_pane_starts_in_the_default_folder_with_folders_first_and_navigates()
    {
        Local("b.txt", "bb");
        Local(Path.Combine("sub", "inner.txt"));
        Local("A.txt", "a");
        var (vm, _, dialogs) = await ConnectedAsync();

        Assert.Equal(_local, vm.Local.CurrentPath);
        Assert.Equal(["sub", "A.txt", "b.txt"], vm.Local.Items.Select(i => i.Name));
        Assert.Equal("2 B", vm.Local.Items.Single(i => i.Name == "b.txt").SizeText);

        await vm.Local.OpenCommand.ExecuteAsync(vm.Local.Items.Single(i => i.Name == "sub"));
        Assert.Equal(Path.Combine(_local, "sub"), vm.Local.CurrentPath);
        Assert.Equal(["inner.txt"], vm.Local.Items.Select(i => i.Name));

        await vm.Local.OpenCommand.ExecuteAsync(vm.Local.Items.Single());
        Assert.Equal([Path.Combine(_local, "sub", "inner.txt")], dialogs.Opened);

        vm.Local.UpCommand.Execute(null);
        Assert.Equal(_local, vm.Local.CurrentPath);
    }

    [Fact]
    public async Task A_missing_local_folder_keeps_the_current_one_and_says_why()
    {
        var (vm, _, _) = await ConnectedAsync();

        vm.Local.Navigate(Path.Combine(_local, "nope"));

        Assert.Equal(_local, vm.Local.CurrentPath);
        Assert.Equal(_local, vm.Local.PathInput);
        Assert.Contains("nope", vm.Message);
    }

    [Fact]
    public async Task Local_rename_and_new_folder_never_replace_an_existing_entry()
    {
        Local("a.txt", "a");
        Local("b.txt", "b");
        var (vm, _, dialogs) = await ConnectedAsync();

        dialogs.TextAnswers.Enqueue("b.txt");
        await vm.Local.RenameCommand.ExecuteAsync(vm.Local.Items.Single(i => i.Name == "a.txt"));
        Assert.Equal("a", File.ReadAllText(Path.Combine(_local, "a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(_local, "b.txt")));
        Assert.Contains("already exists", vm.Message);

        dialogs.TextAnswers.Enqueue("c.txt");
        await vm.Local.RenameCommand.ExecuteAsync(vm.Local.Items.Single(i => i.Name == "a.txt"));
        Assert.Equal("a", File.ReadAllText(Path.Combine(_local, "c.txt")));
        Assert.Contains(vm.Local.Items, i => i.Name == "c.txt");

        dialogs.TextAnswers.Enqueue("photos");
        await vm.Local.NewFolderCommand.ExecuteAsync(null);
        Assert.True(Directory.Exists(Path.Combine(_local, "photos")));
        Assert.Equal("photos", vm.Local.Items.First().Name);

        dialogs.TextAnswers.Enqueue("../escape");
        await vm.Local.NewFolderCommand.ExecuteAsync(null);
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(_local)!, "escape")));
    }

    [Fact]
    public async Task Dragging_server_rows_onto_the_left_pane_downloads_into_its_folder_and_refreshes_it()
    {
        var (vm, _, _) = await ConnectedAsync();

        await vm.DropOnLocalAsync([vm.Items.Single(i => i.Name == "notes.txt"), vm.Items.Single(i => i.Name == "logs")], null);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(_local, "notes.txt")));
        Assert.Equal("line1\nline2", File.ReadAllText(Path.Combine(_local, "logs", "app.log")));
        Assert.Contains(vm.Local.Items, i => i.Name == "notes.txt");
        Assert.Contains(vm.Local.Items, i => i.Name == "logs" && i.IsDirectory);

        var t = Assert.Single(vm.Transfers);
        Assert.Equal(TransferDirection.Download, t.Direction);
        Assert.Equal("2 items", t.Title);
        Assert.Equal(TransferState.Done, t.State);
        Assert.Equal(2, t.TotalFiles);
        Assert.Equal(16, t.TotalBytes);
        Assert.Equal(_local, t.To);
        Assert.StartsWith("Done — 2 files", t.StatusText);
    }

    [Fact]
    public async Task Dropping_onto_a_folder_row_copies_into_that_folder()
    {
        Directory.CreateDirectory(Path.Combine(_local, "inbox"));
        var page = Local("index.html", "<h1>");
        var (vm, server, _) = await ConnectedAsync();
        var inbox = vm.Local.Items.Single(i => i.Name == "inbox");

        await vm.DropOnLocalAsync([vm.Items.Single(i => i.Name == "notes.txt")], inbox);
        await vm.DropOnRemoteAsync([vm.Local.Items.Single(i => i.Name == "index.html")], vm.Items.Single(i => i.Name == "www"));

        Assert.Equal("hello", File.ReadAllText(Path.Combine(_local, "inbox", "notes.txt")));
        Assert.False(File.Exists(Path.Combine(_local, "notes.txt")));
        Assert.Equal("<h1>", Encoding.UTF8.GetString(server.Files["/home/u/www/index.html"]));
        Assert.False(server.Files.ContainsKey("/home/u/index.html"));
        Assert.True(File.Exists(page)); // an upload copies, never moves
    }

    [Fact]
    public async Task Upload_from_the_left_pane_menu_sends_the_selection_into_the_server_folder()
    {
        Local("a.txt", "alpha");
        Local("b.txt", "beta");
        var (vm, server, _) = await ConnectedAsync();
        vm.Local.SelectedItems.Add(vm.Local.Items.Single(i => i.Name == "a.txt"));
        vm.Local.SelectedItems.Add(vm.Local.Items.Single(i => i.Name == "b.txt"));

        await vm.UploadLocalItemCommand.ExecuteAsync(vm.Local.Items.Single(i => i.Name == "b.txt"));

        Assert.Equal("alpha", Encoding.UTF8.GetString(server.Files["/home/u/a.txt"]));
        Assert.Equal("beta", Encoding.UTF8.GetString(server.Files["/home/u/b.txt"]));
        Assert.Contains(vm.Items, i => i.Name == "a.txt");
        var t = Assert.Single(vm.Transfers);
        Assert.Equal(TransferDirection.Upload, t.Direction);
        Assert.Equal("/home/u", t.To);
        Assert.Equal(9, t.TotalBytes);
    }

    [Fact]
    public async Task Download_to_left_pane_folder_uses_the_folder_the_left_pane_shows()
    {
        Directory.CreateDirectory(Path.Combine(_local, "here"));
        var (vm, _, _) = await ConnectedAsync();
        vm.Local.Navigate(Path.Combine(_local, "here"));

        await vm.DownloadItemToLocalCommand.ExecuteAsync(vm.Items.Single(i => i.Name == "notes.txt"));

        Assert.Equal("hello", File.ReadAllText(Path.Combine(_local, "here", "notes.txt")));
        Assert.Equal(["notes.txt"], vm.Local.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Transfers_queue_one_after_another_and_a_queued_one_can_be_cancelled()
    {
        var (vm, server, _) = await ConnectedAsync();
        server.DownloadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notes = vm.Items.Single(i => i.Name == "notes.txt");

        var first = vm.DropOnLocalAsync([notes], null);
        var second = vm.DropOnLocalAsync([vm.Items.Single(i => i.Name == "logs")], null);
        var third = vm.DropOnLocalAsync([notes], null);

        Assert.True(vm.IsTransferring);
        Assert.Equal(3, vm.Transfers.Count);
        var queuedThird = vm.Transfers[0];
        Assert.Equal(TransferState.Queued, queuedThird.State);
        queuedThird.CancelCommand.Execute(null);

        server.DownloadGate.SetResult();
        await Task.WhenAll(first, second, third);

        Assert.False(vm.IsTransferring);
        Assert.Equal([TransferState.Cancelled, TransferState.Done, TransferState.Done], vm.Transfers.Select(t => t.State));
        Assert.True(File.Exists(Path.Combine(_local, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(_local, "notes (1).txt")));
        Assert.True(File.Exists(Path.Combine(_local, "logs", "app.log")));

        vm.ClearFinishedTransfersCommand.Execute(null);
        Assert.Empty(vm.Transfers);
        Assert.False(vm.HasTransfers);
    }

    [Fact]
    public async Task Cancelling_the_running_transfer_marks_it_cancelled_and_leaves_no_partial_file()
    {
        var (vm, server, _) = await ConnectedAsync();
        server.DownloadGate = new TaskCompletionSource();

        var running = vm.DropOnLocalAsync([vm.Items.Single(i => i.Name == "notes.txt")], null);
        await WaitUntil(() => vm.Transfers.Single().State == TransferState.Running);
        vm.CancelTransferCommand.Execute(null);
        await running;

        Assert.Equal(TransferState.Cancelled, vm.Transfers.Single().State);
        Assert.Equal("Download cancelled.", vm.Message);
        Assert.False(File.Exists(Path.Combine(_local, "notes.txt")));
    }

    [Fact]
    public async Task A_failed_transfer_shows_the_error_on_its_row()
    {
        var (vm, server, _) = await ConnectedAsync();
        server.FailDownloadOf = "/home/u/notes.txt";

        await vm.DropOnLocalAsync([vm.Items.Single(i => i.Name == "notes.txt")], null);

        var t = vm.Transfers.Single();
        Assert.Equal(TransferState.Failed, t.State);
        Assert.Equal("Failed — transfer failed", t.StatusText);
        Assert.False(t.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task Remote_new_folder_is_created_in_the_folder_shown()
    {
        var (vm, server, dialogs) = await ConnectedAsync();

        dialogs.TextAnswers.Enqueue("backup");
        await vm.NewFolderCommand.ExecuteAsync(null);
        dialogs.TextAnswers.Enqueue("backup");
        await vm.NewFolderCommand.ExecuteAsync(null);

        Assert.True(server.Directories.ContainsKey("/home/u/backup"));
        Assert.Contains(vm.Items, i => i.Name == "backup" && i.IsDirectory);
        Assert.Contains("already exists", vm.Message);
    }

    [Fact]
    public async Task Totals_count_files_and_bytes_without_following_linked_folders()
    {
        Local(Path.Combine("t", "a.bin"), "12345");
        Local(Path.Combine("t", "deep", "b.bin"), "678");
        Directory.CreateSymbolicLink(Path.Combine(_local, "t", "loop"), Path.Combine(_local, "t"));
        Assert.Equal((2, 8L), LocalFiles.Totals([Path.Combine(_local, "t")]));

        var (vm, server, _) = await ConnectedAsync();
        server.AddDir("/home/u/link", isLink: true);
        var entries = vm.Items.Select(i => i.Entry).ToList();
        Assert.Equal((2, 16L), await SftpDownloader.TotalsAsync(server, entries, default));
    }

    [Fact]
    public void Running_status_shows_percent_size_and_file_position()
    {
        var t = new TransferViewModel(TransferDirection.Upload, "x", "/a", "/b");
        Assert.Equal("Queued", t.StatusText);
        t.Start();
        Assert.Equal("Counting files…", t.StatusText);
        Assert.True(t.IsIndeterminate);
        t.TotalFiles = 3;
        t.TotalBytes = 2048;
        t.BytesDone = 1024;
        t.FilesDone = 1;
        t.CurrentName = "b.bin";
        Assert.Equal(50, t.Percent);
        Assert.StartsWith("50% · 1 KB of 2 KB · file 2 of 3", t.StatusText);
        Assert.EndsWith("— b.bin", t.StatusText);
        Assert.Equal("2 min 5 s", TransferViewModel.Duration(TimeSpan.FromSeconds(125)));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
