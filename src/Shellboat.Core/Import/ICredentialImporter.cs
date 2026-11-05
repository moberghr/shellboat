namespace Shellboat.Core.Import;

/// <summary>One entry read from an external password database (KeePass via keepassxc-cli).</summary>
/// <param name="GroupPath">Group names from the database root down, root itself excluded.</param>
public sealed record ImportedEntry(
    string Title,
    string UserName,
    string Password,
    string Url,
    string Notes,
    IReadOnlyList<string> GroupPath)
{
    /// <summary>Never print the password — records print all members by default.</summary>
    public override string ToString() => $"{nameof(ImportedEntry)} {{ Title = {Title}, UserName = {UserName}, Url = {Url} }}";
}

/// <summary>Why an import could not run, typed so the UI can give the right advice.</summary>
public enum ImportFailureKind
{
    ToolNotFound,
    WrongPasswordOrUnreadable,
    UnexpectedOutput,
}

public sealed class CredentialImportException(ImportFailureKind kind, string message) : Exception(message)
{
    public ImportFailureKind Kind { get; } = kind;
}

public interface ICredentialImporter
{
    Task<IReadOnlyList<ImportedEntry>> ReadAsync(string databasePath, string masterPassword, string? keyFilePath, CancellationToken ct);
}
