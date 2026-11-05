using Shellboat.Core.Trust;

namespace Shellboat.Ssh.Tests;

internal sealed class MemoryTrustStore : ITrustStore
{
    public Dictionary<(string, int), string> Known { get; } = [];

    public Task<TrustVerdict> CheckAsync(TrustKind kind, string host, int port, string fingerprint, CancellationToken ct)
        => Task.FromResult(!Known.TryGetValue((host, port), out var fp) ? TrustVerdict.Unknown
            : fp == fingerprint ? TrustVerdict.Known : TrustVerdict.Changed);

    public Task TrustAsync(TrustKind kind, string host, int port, string fingerprint, CancellationToken ct)
    {
        Known[(host, port)] = fingerprint;
        return Task.CompletedTask;
    }
}

internal sealed class ScriptedPrompter(bool answer) : ITrustPrompter
{
    public List<TrustPrompt> Prompts { get; } = [];

    public Task<bool> ConfirmAsync(TrustPrompt prompt, CancellationToken ct)
    {
        Prompts.Add(prompt);
        return Task.FromResult(answer);
    }
}
