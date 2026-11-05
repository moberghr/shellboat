using System.Text.Json;
using Shellboat.Core.Connections;

namespace Shellboat.Persistence;

/// <summary>The connection tree as one JSON file (<see cref="ShellboatPaths.ConnectionsFile"/>). No secrets.</summary>
public sealed class JsonConnectionStore(string? path = null) : IConnectionStore
{
    private const int CurrentVersion = 1;
    private readonly string _path = path ?? ShellboatPaths.ConnectionsFile;

    public async Task<ConnectionTreeDocument> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return ConnectionTreeDocument.Empty;
        }

        await using var fs = File.OpenRead(_path);
        FileModel? model;
        try
        {
            model = await JsonSerializer.DeserializeAsync<FileModel>(fs, ShellboatJson.Options, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            // Refuse rather than start empty: the next save would overwrite the user's tree.
            throw new InvalidDataException($"{_path} is not a valid Shellboat connections file: {ex.Message}", ex);
        }

        if (model is null)
        {
            return ConnectionTreeDocument.Empty;
        }

        if (model.Version > CurrentVersion)
        {
            throw new InvalidDataException($"{_path} was written by a newer Shellboat (format {model.Version}).");
        }

        return new ConnectionTreeDocument(model.Folders ?? [], model.Connections ?? []);
    }

    public Task SaveAsync(ConnectionTreeDocument document, CancellationToken ct)
        => ShellboatJson.WriteAtomicAsync(_path, new FileModel(CurrentVersion, [.. document.Folders], [.. document.Connections]), ct);

    private sealed record FileModel(int Version, List<Folder>? Folders, List<ConnectionEntry>? Connections);
}
