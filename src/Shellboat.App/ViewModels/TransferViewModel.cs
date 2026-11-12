using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shellboat.App.ViewModels;

public enum TransferDirection
{
    Upload,
    Download,
}

public enum TransferState
{
    Queued,
    Running,
    Done,
    Failed,
    Cancelled,
}

/// <summary>One row of the SFTP tab's transfer list: what goes where, how far it is, how fast, and how it ended.</summary>
public sealed partial class TransferViewModel : ObservableObject
{
    private readonly Stopwatch _clock = new();
    private readonly CancellationTokenSource _cts = new();

    public TransferViewModel(TransferDirection direction, string title, string from, string to)
    {
        Direction = direction;
        Title = title;
        From = from;
        To = to;
    }

    public TransferDirection Direction { get; }

    public string Title { get; }

    public string From { get; }

    public string To { get; }

    public string Route => $"{From} → {To}";

    public string Arrow => Direction == TransferDirection.Upload ? "↑" : "↓";

    public string Tooltip => $"{(Direction == TransferDirection.Upload ? "Upload" : "Download")}: {Route}";

    public CancellationToken Token => _cts.Token;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(StatusText), nameof(IsIndeterminate))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private TransferState _state = TransferState.Queued;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsIndeterminate))]
    private long? _totalBytes;

    [ObservableProperty]
    private int? _totalFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Percent), nameof(StatusText))]
    private long _bytesDone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _filesDone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _currentName = "";

    /// <summary>The result line (on Done) or the error (on Failed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _outcome = "";

    public bool IsActive => State is TransferState.Queued or TransferState.Running;

    public bool IsIndeterminate => State == TransferState.Running && TotalBytes is null;

    public double Percent => TotalBytes is > 0 ? Math.Min(100, BytesDone * 100.0 / TotalBytes.Value) : State == TransferState.Done ? 100 : 0;

    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>Bytes per second since the transfer started, or 0 before the first second.</summary>
    public double BytesPerSecond => _clock.Elapsed.TotalSeconds >= 0.5 ? BytesDone / _clock.Elapsed.TotalSeconds : 0;

    public string StatusText => State switch
    {
        TransferState.Queued => "Queued",
        TransferState.Running when TotalBytes is null => "Counting files…",
        TransferState.Running => Running(),
        TransferState.Done => string.Create(CultureInfo.InvariantCulture, $"Done — {Files(FilesDone)}, {Size(BytesDone)} in {Duration(Elapsed)}"),
        TransferState.Failed => $"Failed — {Outcome}",
        _ => string.Create(CultureInfo.InvariantCulture, $"Cancelled — {Size(BytesDone)} copied"),
    };

    public void Start()
    {
        State = TransferState.Running;
        _clock.Restart();
    }

    public void Finish(TransferState state, string outcome)
    {
        _clock.Stop();
        Outcome = outcome;
        State = state;
    }

    [RelayCommand(CanExecute = nameof(IsActive))]
    public void Cancel() => _cts.Cancel();

    private string Running()
    {
        var parts = new List<string> { string.Create(CultureInfo.InvariantCulture, $"{Percent:0}%") };
        parts.Add(TotalBytes is { } total ? $"{Size(BytesDone)} of {Size(total)}" : Size(BytesDone));
        if (TotalFiles is > 1)
        {
            parts.Add($"file {Math.Min(FilesDone + 1, TotalFiles.Value)} of {TotalFiles}");
        }

        if (BytesPerSecond > 0)
        {
            parts.Add($"{Size((long)BytesPerSecond)}/s");
            if (TotalBytes is { } t && t > BytesDone)
            {
                parts.Add($"{Duration(TimeSpan.FromSeconds((t - BytesDone) / BytesPerSecond))} left");
            }
        }

        var line = string.Join(" · ", parts);
        return CurrentName.Length > 0 ? $"{line} — {CurrentName}" : line;
    }

    private static string Size(long bytes) => RemoteFileItemViewModel.FormatSize(bytes);

    private static string Files(int n) => n == 1 ? "1 file" : $"{n} files";

    public static string Duration(TimeSpan t) => t.TotalSeconds < 1
        ? "<1 s"
        : t.TotalMinutes < 1
            ? $"{(int)t.TotalSeconds} s"
            : t.TotalHours < 1
                ? $"{(int)t.TotalMinutes} min {t.Seconds} s"
                : $"{(int)t.TotalHours} h {t.Minutes} min";
}
