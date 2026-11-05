using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Shellboat.App.Controls;

/// <summary>Resource key (e.g. "Kind.Ssh") → the brush registered under it in Themes/Tokens.axaml, or a literal
/// "#RRGGBB" (a user-picked icon colour) → a solid brush.</summary>
public sealed class ResourceBrush : IValueConverter
{
    public static ResourceBrush Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key)
        {
            return null;
        }

        if (key.StartsWith('#'))
        {
            return Color.TryParse(key, out var color) ? new SolidColorBrush(color) : null;
        }

        return Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var res) == true && res is IBrush b ? b : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
