using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shellboat.App.Connections;
using Shellboat.Core.Connections;

namespace Shellboat.App.ViewModels;

/// <summary>A colour swatch in the picker. <see cref="Hex"/> null = "Default" (the kind's own colour).</summary>
public sealed record ColorOption(string? Hex, string Label, string BrushKey);

/// <summary>
/// "Change icon…" for one tree row: pick a glyph from <see cref="IconCatalog.Glyphs"/> and a colour.
/// <see cref="Result"/> is null when everything is back at the default, so nothing is stored.
/// </summary>
public sealed partial class IconPickerViewModel : ObservableObject
{
    private readonly string _defaultGlyph;
    private readonly string _defaultBrushKey;
    private readonly List<ColorOption> _colors;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewIconKey), nameof(Result))]
    private GlyphChoice? _selectedGlyph;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBrushKey), nameof(Result))]
    private ColorOption? _selectedColor;

    public IconPickerViewModel(string itemName, string defaultGlyph, string defaultBrushKey, NodeIcon? current)
    {
        Title = $"Icon for \"{itemName}\"";
        ItemName = itemName;
        _defaultGlyph = defaultGlyph;
        _defaultBrushKey = defaultBrushKey;
        _colors = [new ColorOption(null, "Default", defaultBrushKey), .. IconCatalog.Colors.Select(c => new ColorOption(c.Hex, c.Label, c.Hex))];
        Select(current);
    }

    public string Title { get; }

    public string ItemName { get; }

    public IReadOnlyList<GlyphChoice> Glyphs => IconCatalog.Glyphs;

    public IReadOnlyList<ColorOption> Colors => _colors;

    public string PreviewIconKey => (SelectedGlyph ?? DefaultGlyph).ResourceKey;

    public string PreviewBrushKey => SelectedColor?.BrushKey ?? _defaultBrushKey;

    /// <summary>What to store: null = default icon.</summary>
    public NodeIcon? Result => IconCatalog.Normalize(SelectedGlyph?.Name, SelectedColor?.Hex, _defaultGlyph);

    private GlyphChoice DefaultGlyph => IconCatalog.Glyphs.First(g => g.Name == _defaultGlyph);

    [RelayCommand]
    private void Reset() => Select(null);

    private void Select(NodeIcon? icon)
    {
        SelectedGlyph = IconCatalog.Glyphs.FirstOrDefault(g => g.Name == icon?.Glyph) ?? DefaultGlyph;
        SelectedColor = Colors.FirstOrDefault(c => c.Hex is not null && string.Equals(c.Hex, icon?.Color, StringComparison.OrdinalIgnoreCase))
            ?? (IconCatalog.IsValidColor(icon?.Color) ? AddCustom(icon!.Color!) : Colors[0]);
    }

    /// <summary>A stored colour that isn't in the palette (hand-edited file) stays selectable instead of being lost.</summary>
    private ColorOption AddCustom(string hex)
    {
        var custom = new ColorOption(hex.ToUpperInvariant(), "Custom", hex.ToUpperInvariant());
        _colors.Add(custom);
        return custom;
    }
}
