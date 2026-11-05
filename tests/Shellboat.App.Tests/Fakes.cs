using Shellboat.App.Services;
using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;
using Shellboat.Core.Secrets;
using Shellboat.Core.Sessions;
using Shellboat.Core.Trust;
using Shellboat.Rdp;

namespace Shellboat.App.Tests;

internal sealed class FakeSecrets(bool canStore = true) : ISecretStore
{
    public Dictionary<(Guid, SecretPurpose), string> Items { get; } = [];

    public bool CanStore { get; } = canStore;

    public Task SetAsync(Guid id, SecretPurpose p, string s, CancellationToken ct)
    {
        if (!CanStore)
        {
            throw new SecretStorageRefusedException("no");
        }

        Items[(id, p)] = s;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(Guid id, SecretPurpose p, CancellationToken ct)
        => Task.FromResult(Items.TryGetValue((id, p), out var v) ? v : null);

    public Task DeleteAsync(Guid id, SecretPurpose p, CancellationToken ct)
    {
        Items.Remove((id, p));
        return Task.CompletedTask;
    }
}

internal sealed class FakeDialogs : IDialogService
{
    public Queue<SecretPromptResult?> SecretAnswers { get; } = new();
    public List<string> SecretPrompts { get; } = [];
    public List<string> Messages { get; } = [];
    public bool ConfirmAnswer { get; set; } = true;

    public Task<SecretPromptResult?> PromptSecretAsync(string title, string message, bool canRemember)
    {
        SecretPrompts.Add(message);
        return Task.FromResult(SecretAnswers.Count > 0 ? SecretAnswers.Dequeue() : null);
    }

    public Task<bool> ConfirmAsync(string title, string message, string acceptLabel) => Task.FromResult(ConfirmAnswer);

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    public Task<string?> PromptTextAsync(string title, string label, string initial) => Task.FromResult<string?>(null);

    public Task<bool> ConfirmHostKeyAsync(TrustPrompt prompt) => Task.FromResult(ConfirmAnswer);

    public Task<CertificateTrustDecision> ConfirmRdpCertificateAsync(RdpCertificatePrompt prompt) => Task.FromResult(CertificateTrustDecision.Reject);

    public Task<bool> EditConnectionAsync(ConnectionEditorViewModel editor) => Task.FromResult(false);

    public Task ShowKeePassImportAsync(KeePassImportViewModel import) => Task.CompletedTask;

    public string? PickedFile { get; set; }

    public Task<string?> PickFileAsync(string title, string fileTypeName, IReadOnlyList<string> patterns) => Task.FromResult(PickedFile);
}

/// <summary>A shell channel whose connect outcome is scripted per attempt.</summary>
internal sealed class FakeChannel(Func<SshCredentials, Exception?> outcome, SshCredentials creds) : IShellChannel
{
    public List<byte[]> Written { get; } = [];
    public List<(int, int)> Resizes { get; } = [];
    public bool Disposed { get; private set; }
    public SshCredentials Credentials { get; } = creds;

    public event Action<byte[]>? DataReceived;
    public event Action<string?>? Closed;

    public Task ConnectAsync(int cols, int rows, CancellationToken ct)
        => outcome(Credentials) is { } ex ? Task.FromException(ex) : Task.CompletedTask;

    public void Write(byte[] data) => Written.Add(data);

    public void Resize(int cols, int rows) => Resizes.Add((cols, rows));

    public void Emit(string text) => DataReceived?.Invoke(System.Text.Encoding.UTF8.GetBytes(text));

    public void Close(string? reason) => Closed?.Invoke(reason);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class MemoryConnectionStore : IConnectionStore
{
    public ConnectionTreeDocument Saved { get; private set; } = ConnectionTreeDocument.Empty;

    public int Saves { get; private set; }

    public Task<ConnectionTreeDocument> LoadAsync(CancellationToken ct) => Task.FromResult(Saved);

    public Task SaveAsync(ConnectionTreeDocument document, CancellationToken ct)
    {
        Saved = document;
        Saves++;
        return Task.CompletedTask;
    }
}
