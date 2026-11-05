namespace Shellboat.Rdp;

public enum RdpSessionErrorKind
{
    None,
    Dns,
    TimeoutOrTransport,
    Authentication,
    AccessDenied,
    CertificateOrTls,
    Gateway,
    Cancelled,
    Unknown
}
