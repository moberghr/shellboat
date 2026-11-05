using System.Text.Json;
using Shellboat.Core.Trust;

namespace Shellboat.Persistence;

/// <summary>Accepted SSH host keys and RDP certificates, as <c>known_hosts.json</c>. Fingerprints only.</summary>
public sealed class JsonTrustStore(string? path = null) : ITrustStore
{
    private readonly string _path = path ?? ShellboatPaths.TrustFile;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<TrustVerdict> CheckAsync(TrustKind kind, string host, int port, string fingerprint, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync(ct).ConfigureAwait(false);
            var match = entries.FirstOrDefault(e => Same(e, kind, host, port));
            if (match is null)
            {
                return TrustVerdict.Unknown;
            }

            return string.Equals(match.Fingerprint, fingerprint, StringComparison.Ordinal) ? TrustVerdict.Known : TrustVerdict.Changed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TrustAsync(TrustKind kind, string host, int port, string fingerprint, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync(ct).ConfigureAwait(false);
            entries.RemoveAll(e => Same(e, kind, host, port));
            entries.Add(new Entry(kind, host.ToLowerInvariant(), port, fingerprint, DateTimeOffset.UtcNow));
            await ShellboatJson.WriteAtomicAsync(_path, entries, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool Same(Entry e, TrustKind kind, string host, int port)
        => e.Kind == kind && e.Port == port && string.Equals(e.Host, host, StringComparison.OrdinalIgnoreCase);

    private async Task<List<Entry>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var fs = File.OpenRead(_path);
        try
        {
            return await JsonSerializer.DeserializeAsync<List<Entry>>(fs, ShellboatJson.Options, ct).ConfigureAwait(false) ?? [];
        }
        catch (JsonException ex)
        {
            // Never treat a corrupt trust file as "nothing trusted, accept anything silently": surface it.
            throw new InvalidDataException($"{_path} is corrupt: {ex.Message}", ex);
        }
    }

    private sealed record Entry(TrustKind Kind, string Host, int Port, string Fingerprint, DateTimeOffset AddedUtc);
}
