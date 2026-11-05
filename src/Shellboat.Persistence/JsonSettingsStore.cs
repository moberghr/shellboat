using System.Text.Json;
using Shellboat.Core.Settings;

namespace Shellboat.Persistence;

/// <summary>Preferences as one JSON file (<see cref="ShellboatPaths.SettingsFile"/>), written atomically. No secrets.</summary>
public sealed class JsonSettingsStore(string? path = null) : ISettingsStore
{
    private const int CurrentVersion = 1;
    private readonly string _path = path ?? ShellboatPaths.SettingsFile;

    /// <summary>A corrupt or newer file throws <see cref="InvalidDataException"/> (the caller falls back to
    /// defaults and says so) rather than silently resetting the user's preferences.</summary>
    public async Task<AppSettings> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return AppSettings.Default;
        }

        await using var fs = File.OpenRead(_path);
        FileModel? model;
        try
        {
            model = await JsonSerializer.DeserializeAsync<FileModel>(fs, ShellboatJson.Options, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{_path} is not a valid Shellboat settings file: {ex.Message}", ex);
        }

        if (model?.Settings is null)
        {
            return AppSettings.Default;
        }

        if (model.Version > CurrentVersion)
        {
            throw new InvalidDataException($"{_path} was written by a newer Shellboat (format {model.Version}).");
        }

        return model.Settings.Normalize();
    }

    public Task SaveAsync(AppSettings settings, CancellationToken ct)
        => ShellboatJson.WriteAtomicAsync(_path, new FileModel(CurrentVersion, settings.Normalize()), ct);

    private sealed record FileModel(int Version, AppSettings? Settings);
}
