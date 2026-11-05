using Shellboat.App.ViewModels;
using Shellboat.Core.Trust;
using Shellboat.Rdp;

namespace Shellboat.App.Services;

/// <summary>The answer to a secret prompt. <see cref="Remember"/> = save it to the OS keychain.</summary>
public sealed record SecretPromptResult(string Secret, bool Remember)
{
    public override string ToString() => $"SecretPromptResult {{ Remember = {Remember} }}";
}

/// <summary>
/// Everything a view model needs to ask the user. Implemented by <see cref="DialogService"/> with Avalonia
/// windows; view-model tests use a fake. All methods may be called from any thread.
/// </summary>
public interface IDialogService
{
    Task<SecretPromptResult?> PromptSecretAsync(string title, string message, bool canRemember);

    Task<bool> ConfirmAsync(string title, string message, string acceptLabel);

    Task ShowMessageAsync(string title, string message);

    Task<string?> PromptTextAsync(string title, string label, string initial);

    Task<bool> ConfirmHostKeyAsync(TrustPrompt prompt);

    Task<CertificateTrustDecision> ConfirmRdpCertificateAsync(RdpCertificatePrompt prompt);

    /// <returns>True when the user saved.</returns>
    Task<bool> EditConnectionAsync(ConnectionEditorViewModel editor);

    Task ShowKeePassImportAsync(KeePassImportViewModel import);

    /// <returns>Local path of the chosen file, or null when cancelled.</returns>
    Task<string?> PickFileAsync(string title, string fileTypeName, IReadOnlyList<string> patterns);

    Task SetClipboardTextAsync(string text);

    /// <returns>Local path of the chosen folder, or null when cancelled.</returns>
    Task<string?> PickFolderAsync(string title);
}
