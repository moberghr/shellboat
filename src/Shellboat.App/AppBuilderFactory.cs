using Avalonia;

namespace Shellboat.App;

/// <summary>Shared Avalonia configuration for the desktop entry point and headless UI tests; the caller adds the
/// windowing platform, everything else is identical (same pattern as Bearing).</summary>
public static class AppBuilderFactory
{
    public const string WindowClass = "Shellboat";

    public static AppBuilder Configure() =>
        AppBuilder.Configure<App>()
            .WithInterFont()
            .LogToTrace();

    public static AppBuilder BuildAvaloniaApp() =>
        Configure()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = WindowClass });
}
