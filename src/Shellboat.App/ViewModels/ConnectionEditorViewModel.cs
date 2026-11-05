using CommunityToolkit.Mvvm.ComponentModel;
using Shellboat.Core.Connections;

namespace Shellboat.App.ViewModels;

/// <summary>
/// The add/edit connection form. Produces a <see cref="ConnectionEntry"/> plus the secrets the user typed
/// (only those — empty secret fields mean "keep what is saved").
/// </summary>
public sealed partial class ConnectionEditorViewModel : ObservableObject
{
    private readonly ConnectionEntry? _original;
    private readonly Guid? _folderId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSsh), nameof(IsRdp), nameof(PortWatermark), nameof(ShowPassword), nameof(ShowKey))]
    private int _kindIndex;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _host = "";

    [ObservableProperty]
    private string _port = "";

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPassword), nameof(ShowKey))]
    private int _authIndex;

    [ObservableProperty]
    private string _privateKeyPath = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _keyPassphrase = "";

    [ObservableProperty]
    private bool _rememberSecret = true;

    [ObservableProperty]
    private string _domain = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixedResolution))]
    private int _resolutionIndex;

    [ObservableProperty]
    private string _width = "1920";

    [ObservableProperty]
    private string _height = "1080";

    [ObservableProperty]
    private int _colorDepthIndex = 2;

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private string? _error;

    public ConnectionEditorViewModel(ConnectionEntry? original, Guid? folderId, bool canRememberSecrets, bool hasSavedSecret)
    {
        _original = original;
        _folderId = original?.FolderId ?? folderId;
        CanRememberSecrets = canRememberSecrets;
        HasSavedSecret = hasSavedSecret;
        if (original is null)
        {
            return;
        }

        KindIndex = original.Kind == ConnectionKind.Rdp ? 1 : 0;
        Name = original.Name;
        Host = original.Host;
        Port = original.Port > 0 ? original.Port.ToString() : "";
        UserName = original.UserName;
        Notes = original.Notes;
        if (original.Ssh is { } ssh)
        {
            AuthIndex = ssh.Auth == SshAuthMethod.PrivateKey ? 1 : 0;
            PrivateKeyPath = ssh.PrivateKeyPath ?? "";
        }

        if (original.Rdp is { } rdp)
        {
            Domain = rdp.Domain;
            ResolutionIndex = rdp.Resolution == RdpResolutionMode.Fixed ? 1 : 0;
            Width = rdp.Width.ToString();
            Height = rdp.Height.ToString();
            ColorDepthIndex = rdp.ColorDepth switch { 16 => 0, 24 => 1, _ => 2 };
        }
    }

    public string Title => _original is null ? "New connection" : $"Edit {_original.Name}";

    public bool CanRememberSecrets { get; }

    /// <summary>Editing a connection that already has a saved secret: an empty field keeps it.</summary>
    public bool HasSavedSecret { get; }

    public string SecretWatermark => HasSavedSecret ? "(saved — leave empty to keep)" : "(ask when connecting)";

    public IReadOnlyList<string> Kinds { get; } = ["SSH", "RDP"];

    public IReadOnlyList<string> AuthMethods { get; } = ["Password", "Private key"];

    public IReadOnlyList<string> ResolutionModes { get; } = ["Fit to tab (dynamic)", "Fixed"];

    public IReadOnlyList<string> ColorDepths { get; } = ["16-bit", "24-bit", "32-bit"];

    public bool IsSsh => KindIndex == 0;

    public bool IsRdp => KindIndex == 1;

    public bool ShowPassword => IsRdp || AuthIndex == 0;

    public bool ShowKey => IsSsh && AuthIndex == 1;

    public bool IsFixedResolution => ResolutionIndex == 1;

    public string PortWatermark => IsRdp ? "3389" : "22";

    public ConnectionKind Kind => IsRdp ? ConnectionKind.Rdp : ConnectionKind.Ssh;

    /// <summary>Validate and build the entry. Null (with <see cref="Error"/> set) when the form is invalid.</summary>
    public ConnectionEntry? TryBuild()
    {
        Error = null;
        var host = Host.Trim();
        if (host.Length == 0)
        {
            Error = "Host is required.";
            return null;
        }

        var port = 0;
        if (Port.Trim().Length > 0 && (!int.TryParse(Port.Trim(), out port) || port is < 1 or > 65535))
        {
            Error = "Port must be a number between 1 and 65535.";
            return null;
        }

        if (ShowKey && PrivateKeyPath.Trim().Length == 0)
        {
            Error = "Choose a private key file.";
            return null;
        }

        var width = 1920;
        var height = 1080;
        if (IsRdp && IsFixedResolution && (!int.TryParse(Width, out width) || !int.TryParse(Height, out height) || width < 200 || height < 200))
        {
            Error = "Resolution must be at least 200×200.";
            return null;
        }

        return new ConnectionEntry
        {
            Id = _original?.Id ?? Guid.NewGuid(),
            Name = Name.Trim().Length > 0 ? Name.Trim() : host,
            FolderId = _folderId,
            SortOrder = _original?.SortOrder ?? 0,
            Kind = Kind,
            Host = host,
            Port = port,
            UserName = UserName.Trim(),
            Notes = Notes,
            Ssh = IsSsh ? new SshSettings(AuthIndex == 1 ? SshAuthMethod.PrivateKey : SshAuthMethod.Password, ShowKey ? PrivateKeyPath.Trim() : null) : null,
            Rdp = IsRdp ? new RdpSettings(Domain.Trim(), IsFixedResolution ? RdpResolutionMode.Fixed : RdpResolutionMode.Dynamic, width, height, ColorDepthIndex switch { 0 => 16, 1 => 24, _ => 32 }) : null,
        };
    }
}
