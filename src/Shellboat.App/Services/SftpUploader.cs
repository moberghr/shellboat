using Shellboat.Core.Sessions;

namespace Shellboat.App.Services;

/// <summary>Progress of a running upload: the file being copied, files finished, bytes copied overall.</summary>
public sealed record UploadProgress(string CurrentName, int FilesDone, long BytesDone);

/// <summary>What a finished upload did. <see cref="SkippedLinks"/> = symlinked local folders not followed.</summary>
public sealed record UploadSummary(int Files, long Bytes, int SkippedLinks, IReadOnlyList<string> CreatedPaths);

/// <summary>
/// Copies local files and folders (recursively) into a remote folder. Never overwrites: a taken name becomes
/// "report (1).pdf" (see <see cref="UniqueRemotePathAsync"/>), and the channel's exclusive create guards the race.
/// A file that fails or is cancelled half-way is removed by the channel, not left truncated. Symlinked local folders
/// are not followed, so a link loop can't make it run forever.
/// </summary>
public sealed class SftpUploader(IFileChannel channel)
{
    private int _files;
    private long _bytes;
    private int _skippedLinks;
    private readonly List<string> _created = [];

    public async Task<UploadSummary> UploadAsync(IReadOnlyList<string> localPaths, string remoteFolder, IProgress<UploadProgress>? progress, CancellationToken ct)
    {
        foreach (var path in localPaths)
        {
            await CopyAsync(path, remoteFolder, progress, ct);
        }

        return new UploadSummary(_files, _bytes, _skippedLinks, _created);
    }

    /// <summary>A path in <paramref name="folder"/> for <paramref name="name"/> that is not taken yet:
    /// "report.pdf", then "report (1).pdf", "report (2).pdf", ….</summary>
    public static async Task<string> UniqueRemotePathAsync(IFileChannel channel, string folder, string name, CancellationToken ct)
    {
        var candidate = RemotePath.Combine(folder, name);
        var dot = name.LastIndexOf('.');
        var (stem, ext) = dot > 0 ? (name[..dot], name[dot..]) : (name, "");
        for (var i = 1; await channel.ExistsAsync(candidate, ct); i++)
        {
            candidate = RemotePath.Combine(folder, $"{stem} ({i}){ext}");
        }

        return candidate;
    }

    private async Task CopyAsync(string localPath, string remoteFolder, IProgress<UploadProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var trimmed = Path.TrimEndingDirectorySeparator(localPath);
        var name = Path.GetFileName(trimmed);
        if (Directory.Exists(trimmed))
        {
            if (new DirectoryInfo(trimmed).LinkTarget is not null)
            {
                _skippedLinks++;
                return;
            }

            var dir = await UniqueRemotePathAsync(channel, remoteFolder, name, ct);
            await channel.CreateDirectoryAsync(dir, ct);
            _created.Add(dir);
            var children = Directory.EnumerateFileSystemEntries(trimmed).Order(StringComparer.Ordinal).ToList();
            foreach (var child in children)
            {
                await CopyAsync(child, dir, progress, ct);
            }

            return;
        }

        var target = await UniqueRemotePathAsync(channel, remoteFolder, name, ct);
        var before = _bytes;
        progress?.Report(new UploadProgress(name, _files, _bytes));
        await using (var file = new FileStream(trimmed, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var fileProgress = progress is null ? null : new InlineProgress<long>(n => progress.Report(new UploadProgress(name, _files, before + n)));
            await channel.UploadAsync(file, target, fileProgress, ct);
            _bytes = before + file.Length;
        }

        _files++;
        _created.Add(target);
        progress?.Report(new UploadProgress(name, _files, _bytes));
    }

    /// <summary>Forwards immediately on the reporting thread (the outer progress decides where it lands).</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
