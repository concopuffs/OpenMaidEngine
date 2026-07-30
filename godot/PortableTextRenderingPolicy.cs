using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public sealed class PortableTextRenderingPolicy
{
    public const string ResourcePath = "res://config/himegari-text-rendering.json";

    public string Id { get; set; } = "";
    public int FontCacheCapacity { get; set; } = 8;
    public int GlyphMaskCacheCapacity { get; set; } = 2048;
    public PortableFontRasterPolicy Raster { get; set; } = new();
    public string[] DefaultSubstitutes { get; set; } = [];
    public PortableFontFamilyPolicy[] Families { get; set; } = [];

    public static PortableTextRenderingPolicy Load()
    {
        string json = Godot.FileAccess.GetFileAsString(ResourcePath);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException(
                $"Portable text policy '{ResourcePath}' is missing or empty.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        PortableTextRenderingPolicy policy =
            JsonSerializer.Deserialize<PortableTextRenderingPolicy>(json, options)
            ?? throw new InvalidOperationException(
                $"Portable text policy '{ResourcePath}' decoded to null.");
        policy.Validate();
        return policy;
    }

    public string[] ResolveFamilies(string requestedFace)
    {
        string requested = requestedFace?.Trim() ?? "";
        PortableFontFamilyPolicy? mapped = Families.FirstOrDefault(
            family => family.Requested.Any(
                alias => alias.Equals(requested, StringComparison.OrdinalIgnoreCase)));
        IEnumerable<string> candidates = mapped?.Substitutes ?? DefaultSubstitutes;
        // The authored face remains the last candidate. This preserves a useful match on systems
        // that happen to provide it without making that Windows font a portable dependency.
        return candidates
            .Append(requested)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidOperationException("Portable text policy requires a non-empty id.");
        if (FontCacheCapacity <= 0)
            throw new InvalidOperationException("Portable text fontCacheCapacity must be positive.");
        if (GlyphMaskCacheCapacity <= 0)
            throw new InvalidOperationException(
                "Portable text glyphMaskCacheCapacity must be positive.");
        if (DefaultSubstitutes.Length == 0)
            throw new InvalidOperationException(
                "Portable text policy requires at least one default substitute.");
        Raster.Validate();
        foreach (PortableFontFamilyPolicy family in Families)
        {
            if (family.Requested.Length == 0 || family.Substitutes.Length == 0)
                throw new InvalidOperationException(
                    "Every portable font family mapping requires requested aliases and substitutes.");
        }
    }
}

public sealed class PortableFontFamilyPolicy
{
    public string[] Requested { get; set; } = [];
    public string[] Substitutes { get; set; } = [];
}

public sealed class PortableFontRasterPolicy
{
    public string Antialiasing { get; set; } = "gray";
    public string Hinting { get; set; } = "normal";
    public string SubpixelPositioning { get; set; } = "disabled";
    public bool AllowSystemFallback { get; set; } = true;
    public bool MultichannelSignedDistanceField { get; set; }
    public int RegularWeight { get; set; } = 400;
    public int BoldWeight { get; set; } = 700;
    public int BoldThreshold { get; set; } = 700;

    public void Validate()
    {
        if (Antialiasing is not ("none" or "gray" or "lcd"))
            throw new InvalidOperationException(
                "Portable raster antialiasing must be none, gray, or lcd.");
        if (Hinting is not ("none" or "light" or "normal"))
            throw new InvalidOperationException(
                "Portable raster hinting must be none, light, or normal.");
        if (SubpixelPositioning is not ("disabled" or "auto" or "one-half" or "one-quarter"))
            throw new InvalidOperationException(
                "Portable raster subpixelPositioning has an unsupported value.");
        if (RegularWeight is < 100 or > 999
            || BoldWeight is < 100 or > 999
            || BoldThreshold is < 0 or > 1000)
            throw new InvalidOperationException(
                "Portable raster weights are outside their valid ranges.");
    }
}
