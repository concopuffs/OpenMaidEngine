using Avalonia.Data.Converters;

namespace Ome.Launcher.Views;

/// <summary>Text converters for the views.</summary>
public static class Text
{
    /// <summary>A heading in capitals, as the mock's <c>text-transform: uppercase</c> sets it; Japanese is unchanged.</summary>
    public static FuncValueConverter<string?, string?> Upper { get; } = new(text => text?.ToUpperInvariant());
}
