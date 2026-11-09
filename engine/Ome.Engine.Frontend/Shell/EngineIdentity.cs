namespace Ome.Engine.Frontend.Shell;

/// <summary>
/// Open Maid Engine's own identity, which the options panel's 情報 section shows where AGE's ﾊﾞｰｼﾞｮﾝ情報 names AGE
/// (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §5.5). The version is what <c>git describe</c> gave the
/// build (godot/OME.csproj, target OmeVersion); the build stores it in an AssemblyMetadata attribute named
/// <see cref="VersionMetadataKey"/>.
/// </summary>
public static class EngineIdentity
{
    public const string Name = "Open Maid Engine";
    public const string License = "Released under the MIT License";
    /// <summary>FFmpeg's license checklist asks the about box to name FFmpeg, its license and where its source is
    /// (docs/platform-portability.md, item 10). FFmpeg-SOURCE.txt, beside the libraries in every package, links to the
    /// exact source.</summary>
    public const string FfmpegNotice =
        "This software uses FFmpeg, licensed under the LGPLv2.1; FFmpeg-SOURCE.txt links to its source.";
    public const string NoticesFile = "THIRD_PARTY_NOTICES.md";
    public const string NoticesPointer = "Third-party licenses: " + NoticesFile;
    public const string VersionMetadataKey = "OmeVersion";

    /// <summary>The version as 情報 shows it, from <c>git describe --tags --match "v[0-9]*" --dirty=+ --always</c> or a
    /// <c>-p:OmeVersion=</c> override: a release tag without its "v" ("0.4.0"), a later commit as git describes it
    /// ("0.4.0-66-gabc2403"), "+" after uncommitted changes, and "dev-" before a bare abbreviated commit, which a clone
    /// without tags gives. No version reads "unknown".</summary>
    public static string DisplayVersion(string? described)
    {
        string text = described?.Trim() ?? "";
        if (text.Length == 0) return "unknown";
        if (text.Length > 1 && text[0] == 'v' && char.IsAsciiDigit(text[1])) return text[1..];
        string commit = text.TrimEnd('+');
        if (commit.Length >= 7 && commit.All(char.IsAsciiHexDigit)) return "dev-" + text;
        return text;
    }
}
