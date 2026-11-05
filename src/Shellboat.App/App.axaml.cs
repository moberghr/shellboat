using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Shellboat.Core.Settings;
using Shellboat.App.Services;
using Shellboat.App.ViewModels;
using Shellboat.App.Views;
using Shellboat.Persistence;
using Shellboat.Persistence.Import;
using Shellboat.Persistence.Secrets;
using Shellboat.Ssh;

namespace Shellboat.App;

/// <summary>Composition root: everything is wired by hand here (no container), same as Bearing (§2.4).</summary>
public partial class App : Application
{
    private readonly MainMenu.AppMenu _appMenu = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (OperatingSystem.IsMacOS())
        {
            NativeMenu.SetMenu(this, _appMenu.Menu); // before any window, or AppKit keeps the default app menu
        }
    }

    /// <summary>settings.json, or defaults plus a status-bar note when it can't be read (saving later rewrites it).</summary>
    private static (SettingsService Settings, string? Problem) LoadSettings()
    {
        var store = new JsonSettingsStore();
        try
        {
            // JsonSettingsStore awaits with ConfigureAwait(false) throughout, so blocking here can't deadlock.
            return (new SettingsService(store, store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult()), null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return (new SettingsService(store), $"Settings not loaded, using defaults: {ex.Message}");
        }
    }

    private void ApplyTheme(ThemeMode mode) => RequestedThemeVariant = mode switch
    {
        ThemeMode.Light => ThemeVariant.Light,
        ThemeMode.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Settings first (a small local file), so the window opens in the right theme.
            var (settings, settingsProblem) = LoadSettings();
            ApplyTheme(settings.Current.Theme);
            settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsService.Current))
                {
                    ApplyTheme(settings.Current.Theme);
                }
            };

            var window = new MainWindow();
            desktop.MainWindow = window;
            base.OnFrameworkInitializationCompleted();

            var dialogs = new DialogService();
            var secrets = await SecretStoreFactory.CreateAsync();
            var trust = new JsonTrustStore();
            var prompter = new DialogTrustPrompter(dialogs);
            var tree = new ConnectionTreeViewModel(new JsonConnectionStore(), secrets);
            var vm = new MainWindowViewModel(
                tree,
                secrets,
                dialogs,
                (connection, credentials) => new SshShellChannel(connection, credentials, trust, prompter),
                (connection, credentials) => new SshFileChannel(connection, credentials, trust, prompter),
                new KeePassXcCliImporter(),
                settings: settings);
            window.DataContext = vm;
            _appMenu.Attach(vm);
            if (settingsProblem is not null)
            {
                vm.StatusText = settingsProblem;
            }

            try
            {
                await tree.LoadAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                vm.StatusText = $"Could not load connections: {ex.Message}";
            }

            desktop.ShutdownRequested += async (_, e) =>
            {
                // ⌘Q / app menu Quit: same "still connected?" question as closing the window.
                if (!vm.QuitApproved && vm.NeedsCloseConfirmation(vm.AllSessions))
                {
                    e.Cancel = true;
                    if (await vm.ConfirmCloseAsync(vm.AllSessions))
                    {
                        vm.QuitApproved = true;
                        desktop.Shutdown();
                    }

                    return;
                }

                foreach (var tab in vm.Tabs.ToList())
                {
                    await tab.DisposeAsync();
                }
            };
            return;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
