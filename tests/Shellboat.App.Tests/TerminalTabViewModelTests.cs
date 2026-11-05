using Shellboat.App.Services;
using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;
using Shellboat.Core.Secrets;
using Shellboat.Core.Sessions;

namespace Shellboat.App.Tests;

public class TerminalTabViewModelTests
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

    private static (TerminalTabViewModel Vm, List<FakeChannel> Channels) Create(FakeSecrets secrets, FakeDialogs dialogs, Func<SshCredentials, Exception?> outcome)
    {
        var channels = new List<FakeChannel>();
        var vm = new TerminalTabViewModel(Entry, (_, creds) =>
        {
            var ch = new FakeChannel(outcome, creds);
            channels.Add(ch);
            return ch;
        }, new CredentialResolver(secrets, dialogs), dialogs);
        return (vm, channels);
    }

    [Fact]
    public async Task Saved_password_connects_without_prompting_and_bytes_flow_both_ways()
    {
        var secrets = new FakeSecrets();
        secrets.Items[(Entry.Id, SecretPurpose.Password)] = "pw";
        var dialogs = new FakeDialogs();
        var (vm, channels) = Create(secrets, dialogs, _ => null);

        await vm.ConnectAsync();

        Assert.Equal(SessionState.Connected, vm.State);
        Assert.Empty(dialogs.SecretPrompts);
        Assert.Equal("pw", channels.Single().Credentials.Password);

        channels[0].Emit("hello");
        Assert.Equal("hello", vm.Emulator.GetText(vm.Emulator.Snapshot().ViewportTop, 0, vm.Emulator.Snapshot().ViewportTop, 4));
        vm.Emulator.SendInput([(byte)'l', (byte)'s']);
        Assert.Equal("ls"u8.ToArray(), Assert.Single(channels[0].Written));

        vm.Resize(100, 30);
        Assert.Contains((100, 30), channels[0].Resizes);
    }

    [Fact]
    public async Task Rejected_password_reprompts_and_remembers_when_asked()
    {
        var secrets = new FakeSecrets();
        var dialogs = new FakeDialogs();
        dialogs.SecretAnswers.Enqueue(new SecretPromptResult("wrong", false));
        dialogs.SecretAnswers.Enqueue(new SecretPromptResult("right", true));
        var (vm, channels) = Create(secrets, dialogs, c => c.Password == "right" ? null : new AuthenticationFailedException("denied"));

        await vm.ConnectAsync();

        Assert.Equal(SessionState.Connected, vm.State);
        Assert.Equal(2, dialogs.SecretPrompts.Count);
        Assert.Contains("denied", dialogs.SecretPrompts[1]);
        Assert.True(channels[0].Disposed);
        Assert.Equal("right", secrets.Items[(Entry.Id, SecretPurpose.Password)]);
    }

    [Fact]
    public async Task Cancelling_the_prompt_fails_without_opening_a_channel()
    {
        var (vm, channels) = Create(new FakeSecrets(), new FakeDialogs(), _ => null);
        await vm.ConnectAsync();
        Assert.Equal(SessionState.Failed, vm.State);
        Assert.Empty(channels);
    }

    [Fact]
    public async Task Host_key_rejection_is_shown_and_not_retried()
    {
        var secrets = new FakeSecrets();
        secrets.Items[(Entry.Id, SecretPurpose.Password)] = "pw";
        var dialogs = new FakeDialogs();
        var (vm, channels) = Create(secrets, dialogs, _ => new HostKeyRejectedException("CHANGED"));

        await vm.ConnectAsync();

        Assert.Equal(SessionState.Failed, vm.State);
        Assert.Single(channels);
        Assert.Contains("CHANGED", Assert.Single(dialogs.Messages));
    }

    [Fact]
    public async Task Without_a_keychain_a_remembered_password_is_not_stored()
    {
        var secrets = new FakeSecrets(canStore: false);
        var dialogs = new FakeDialogs();
        dialogs.SecretAnswers.Enqueue(new SecretPromptResult("pw", true));
        var (vm, _) = Create(secrets, dialogs, _ => null);

        await vm.ConnectAsync();

        Assert.Equal(SessionState.Connected, vm.State);
        Assert.Empty(secrets.Items);
    }
}
