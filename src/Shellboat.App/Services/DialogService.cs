using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Shellboat.App.ViewModels;
using Shellboat.App.Views;
using Shellboat.Core.Trust;
using Shellboat.Rdp;

namespace Shellboat.App.Services;

/// <summary>Avalonia implementation of <see cref="IDialogService"/>. Every call hops to the UI thread, so
/// protocol threads (host-key and certificate callbacks) can call it directly.</summary>
public sealed class DialogService : IDialogService
{
    private static Window? Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>The focused window (a detached session window counts), else the main window.</summary>
    private static Window? ActiveOwner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault(w => w.IsActive) ?? Owner;

    public Task<SecretPromptResult?> PromptSecretAsync(string title, string message, bool canRemember) => OnUi(async () =>
    {
        var box = new TextBox { PasswordChar = '•', MinWidth = 320 };
        var remember = new CheckBox { Content = "Save in the system keychain", IsChecked = false, IsVisible = canRemember };
        SecretPromptResult? result = null;
        var window = Dialog(title, Text(message), box, remember);
        AddButtons(window, ("Cancel", false, () => window.Close()), ("Connect", true, () =>
        {
            result = new SecretPromptResult(box.Text ?? "", remember.IsChecked == true);
            window.Close();
        }
        ));
        window.Opened += (_, _) => box.Focus();
        await Show(window);
        return result;
    });

    public Task<bool> ConfirmAsync(string title, string message, string acceptLabel) => OnUi(async () =>
    {
        var accepted = false;
        var window = Dialog(title, Text(message));
        AddButtons(window, ("Cancel", false, () => window.Close()), (acceptLabel, true, () =>
        {
            accepted = true;
            window.Close();
        }
        ));
        await Show(window);
        return accepted;
    });

    public Task ShowMessageAsync(string title, string message) => OnUi(async () =>
    {
        var window = Dialog(title, Text(message));
        AddButtons(window, ("OK", true, () => window.Close()));
        await Show(window);
        return true;
    });

    public Task<string?> PromptTextAsync(string title, string label, string initial) => OnUi(async () =>
    {
        var box = new TextBox { Text = initial, MinWidth = 320 };
        string? result = null;
        var window = Dialog(title, Text(label), box);
        AddButtons(window, ("Cancel", false, () => window.Close()), ("OK", true, () =>
        {
            result = box.Text;
            window.Close();
        }
        ));
        window.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        await Show(window);
        return result;
    });

    public Task<bool> ConfirmHostKeyAsync(TrustPrompt prompt)
        => ConfirmAsync("Unknown host key",
            $"First connection to {prompt.Host}:{prompt.Port}.\n\nHost key fingerprint:\n{prompt.Fingerprint}\n\n"
            + "Check it against the server before trusting it. Trust and remember this key?",
            "Trust");

    public Task<CertificateTrustDecision> ConfirmRdpCertificateAsync(RdpCertificatePrompt prompt) => OnUi(async () =>
    {
        var body = prompt.IsChanged
            ? $"The certificate for {prompt.Host}:{prompt.Port} has CHANGED.\n\nSubject: {prompt.Subject}\nIssuer: {prompt.Issuer}\nFingerprint: {prompt.Fingerprint}\n\nPrevious fingerprint: {prompt.PreviousFingerprint ?? "unknown"}\n\nOnly continue if you expected this change."
            : $"First connection to {prompt.Host}:{prompt.Port}.\n\nCommon name: {prompt.CommonName}\nSubject: {prompt.Subject}\nIssuer: {prompt.Issuer}\nFingerprint: {prompt.Fingerprint}\n\nTrust this certificate?";
        var decision = CertificateTrustDecision.Reject;
        var window = Dialog(prompt.IsChanged ? "Certificate changed" : "Trust certificate", Text(body));
        AddButtons(window,
            ("Reject", false, () => window.Close()),
            ("Trust once", false, () =>
            {
                decision = CertificateTrustDecision.TrustOnce;
                window.Close();
            }
        ),
            ("Trust always", true, () =>
            {
                decision = CertificateTrustDecision.TrustAlways;
                window.Close();
            }
        ));
        await Show(window);
        return decision;
    });

    public Task<bool> EditConnectionAsync(ConnectionEditorViewModel editor) => OnUi(async () =>
    {
        var window = new ConnectionEditorWindow { DataContext = editor };
        return Owner is { } owner ? await window.ShowDialog<bool>(owner) : false;
    });

    public Task<bool> EditSettingsAsync(SettingsViewModel settings) => OnUi(async () =>
    {
        var window = new SettingsWindow { DataContext = settings };
        return ActiveOwner is { } owner ? await window.ShowDialog<bool>(owner) : false;
    });

    public Task<bool> PickIconAsync(IconPickerViewModel picker) => OnUi(async () =>
    {
        var window = new IconPickerWindow { DataContext = picker };
        return Owner is { } owner ? await window.ShowDialog<bool>(owner) : false;
    });

    public Task ShowKeePassImportAsync(KeePassImportViewModel import) => OnUi(async () =>
    {
        var window = new KeePassImportWindow { DataContext = import };
        await Show(window);
        return true;
    });

    public Task<string?> PickFileAsync(string title, string fileTypeName, IReadOnlyList<string> patterns) => OnUi(async () =>
    {
        if (Owner is not { } owner)
        {
            return null;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType(fileTypeName) { Patterns = [.. patterns] },
                Avalonia.Platform.Storage.FilePickerFileTypes.All,
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    });

    public Task SetClipboardTextAsync(string text) => OnUi(async () =>
    {
        if (Owner?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    });

    public Task ClearClipboardIfAsync(string text) => OnUi(async () =>
    {
        if (Owner?.Clipboard is { } clipboard && await clipboard.TryGetTextAsync() == text)
        {
            await clipboard.ClearAsync();
        }
    });

    public Task<string?> PickFolderAsync(string title) => OnUi(async () =>
    {
        if (Owner is not { } owner)
        {
            return null;
        }

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    });

    private static Task<T> OnUi<T>(Func<Task<T>> action)
        => Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);

    private static Task OnUi(Func<Task> action)
        => Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);

    private static async Task Show(Window window)
    {
        if (Owner is { } owner)
        {
            await window.ShowDialog(owner);
        }
    }

    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };

    private static Window Dialog(string title, params Control[] content)
    {
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
        foreach (var c in content)
        {
            panel.Children.Add(c);
        }

        return new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
            MinWidth = 360,
        };
    }

    private static void AddButtons(Window window, params (string Label, bool IsDefault, Action Click)[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var (label, isDefault, click) in buttons)
        {
            var button = new Button { Content = label, IsDefault = isDefault, IsCancel = label is "Cancel" or "Reject" };
            if (isDefault)
            {
                button.Classes.Add("accent");
            }

            button.Click += (_, _) => click();
            row.Children.Add(button);
        }

        ((StackPanel)window.Content!).Children.Add(row);
    }
}

/// <summary>Adapts the dialog service to the SSH layer's host-key prompt contract.</summary>
public sealed class DialogTrustPrompter(IDialogService dialogs) : ITrustPrompter
{
    public Task<bool> ConfirmAsync(TrustPrompt prompt, CancellationToken ct) => dialogs.ConfirmHostKeyAsync(prompt);
}
