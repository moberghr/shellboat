using Avalonia;
using Shellboat.App;

namespace Shellboat.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
        => AppBuilderFactory.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
