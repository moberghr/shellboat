using Avalonia.Threading;
using Shellboat.App.Services;
using Shellboat.Core.Connections;
using Shellboat.Core.Sessions;
using Shellboat.Terminal;

namespace Shellboat.App.ViewModels;

/// <summary>Creates the shell channel for one connect attempt (SSH in production, a fake in tests).</summary>
public delegate IShellChannel ShellChannelFactory(ConnectionEntry connection, SshCredentials credentials);

/// <summary>
/// An SSH terminal tab: wires a <see cref="TerminalEmulator"/> to an <see cref="IShellChannel"/>, resolves
/// credentials (keychain → prompt, re-prompt on rejection) and survives reconnects with the same emulator,
/// so the scrollback stays.
/// </summary>
public sealed class TerminalTabViewModel : SessionTabViewModel
{
    private readonly ShellChannelFactory _channelFactory;
    private readonly CredentialResolver _credentials;
    private readonly IDialogService _dialogs;
    private IShellChannel? _channel;
    private CancellationTokenSource? _connectCts;

    public TerminalTabViewModel(ConnectionEntry connection, ShellChannelFactory channelFactory, CredentialResolver credentials, IDialogService dialogs)
        : base(connection)
    {
        _channelFactory = channelFactory;
        _credentials = credentials;
        _dialogs = dialogs;
        Emulator = new TerminalEmulator();
        Emulator.Output += bytes => _channel?.Write(bytes);
        Emulator.TitleChanged += title => Dispatcher.UIThread.Post(() => Title = string.IsNullOrWhiteSpace(title) ? Connection.Name : $"{Connection.Name} — {title}");
    }

    public TerminalEmulator Emulator { get; }

    /// <summary>The terminal stays visible after a disconnect so its output can still be read and copied.</summary>
    public override bool IsOverlayVisible => State is SessionState.Connecting && _channel is null;

    /// <summary>Called by the terminal control whenever its cell grid changes.</summary>
    public void Resize(int cols, int rows)
    {
        Emulator.Resize(cols, rows);
        _channel?.Resize(cols, rows);
    }

    public override async Task ConnectAsync()
    {
        if (State == SessionState.Connected)
        {
            return;
        }

        await TearDownAsync();
        _connectCts = new CancellationTokenSource();
        var ct = _connectCts.Token;
        State = SessionState.Connecting;
        Error = null;

        var result = await SshConnectFlow.ConnectAsync(
            Connection,
            _credentials,
            creds =>
            {
                var channel = _channelFactory(Connection, creds);
                channel.DataReceived += OnData;
                channel.Closed += OnClosed;
                return channel;
            },
            (channel, token) => channel.ConnectAsync(Emulator.Cols, Emulator.Rows, token),
            async channel => await DisposeChannelAsync(channel),
            ct);

        if (result.Channel is { } connected)
        {
            _channel = connected;
            State = SessionState.Connected;
            return;
        }

        Fail(result.Error ?? "Connection failed.");
        if (result.HostKeyRejected)
        {
            await _dialogs.ShowMessageAsync("Host key rejected", result.Error ?? "");
        }
    }

    private void OnData(byte[] data) => Emulator.Feed(data, data.Length);

    private void OnClosed(string? reason)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (State != SessionState.Connected)
            {
                return;
            }

            Error = reason is null ? "Session ended." : $"Connection lost: {reason}";
            State = SessionState.Disconnected;
            Emulator.Feed($"\r\n\x1b[2m[{Error} Press Reconnect to start a new session.]\x1b[0m\r\n");
        });
    }

    private void Fail(string message)
    {
        Error = message;
        State = SessionState.Failed;
        Emulator.Feed($"\r\n\x1b[31m{message}\x1b[0m\r\n");
    }

    private async Task TearDownAsync()
    {
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = null;
        var channel = _channel;
        _channel = null;
        if (channel is not null)
        {
            await DisposeChannelAsync(channel);
        }
    }

    private async Task DisposeChannelAsync(IShellChannel channel)
    {
        channel.DataReceived -= OnData;
        channel.Closed -= OnClosed;
        await channel.DisposeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await TearDownAsync();
    }
}
