using Shellboat.Core.Secrets;

namespace Shellboat.Persistence.Tests;

/// <summary>Records every CLI invocation and answers from a script.</summary>
internal sealed class FakeCliRunner(Func<string, IReadOnlyList<string>, string?, CliResult> respond) : ICliRunner
{
    public List<(string File, IReadOnlyList<string> Args, string? Stdin)> Calls { get; } = [];

    public Task<CliResult> RunAsync(string file, IReadOnlyList<string> args, string? stdin, CancellationToken ct)
    {
        Calls.Add((file, args, stdin));
        return Task.FromResult(respond(file, args, stdin));
    }
}

internal sealed class FakeSecretStore : ISecretStore
{
    public Dictionary<(Guid, SecretPurpose), string> Items { get; } = [];

    public Func<string, string>? ReadTransform { get; set; }

    public bool CanStore => true;

    public Task SetAsync(Guid connectionId, SecretPurpose purpose, string secret, CancellationToken ct)
    {
        Items[(connectionId, purpose)] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(Guid connectionId, SecretPurpose purpose, CancellationToken ct)
        => Task.FromResult(Items.TryGetValue((connectionId, purpose), out var v) ? ReadTransform?.Invoke(v) ?? v : null);

    public Task DeleteAsync(Guid connectionId, SecretPurpose purpose, CancellationToken ct)
    {
        Items.Remove((connectionId, purpose));
        return Task.CompletedTask;
    }
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shellboat-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
