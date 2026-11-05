using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Controls;
using Avalonia.Media;

namespace Shellboat.App.Controls;

/// <summary>Resource key (e.g. "Icon.Folder") → the Geometry registered under it in Themes/Tokens.axaml.</summary>
public sealed class ResourceGeometry : IValueConverter
{
    public static ResourceGeometry Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key && Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var res) == true && res is Geometry g ? g : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
