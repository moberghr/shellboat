using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
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
                new KeePassXcCliImporter());
            window.DataContext = vm;

            try
            {
                await tree.LoadAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                vm.StatusText = $"Could not load connections: {ex.Message}";
            }

            desktop.ShutdownRequested += async (_, _) =>
            {
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
