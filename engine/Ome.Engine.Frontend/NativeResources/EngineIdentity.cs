namespace Ome.Engine.Frontend.NativeResources;

/// <summary>
/// Open Maid Engine's own identity, which the port's ﾍﾙﾌﾟ > ﾊﾞｰｼﾞｮﾝ情報 shows where AGE shows its own
/// (docs/engine-re.md, "Native menu bar", ﾊﾞｰｼﾞｮﾝ情報). The version is what <c>git describe</c> gave the build
/// (godot/OME.csproj, target OmeVersion); the build stores it in an AssemblyMetadata attribute named
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

    /// <summary>The version as the dialog shows it, from <c>git describe --tags --match "v[0-9]*" --dirty=+ --always</c>
    /// or a <c>-p:OmeVersion=</c> override: a release tag without its "v" ("0.4.0"), a later commit as git describes it
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

    /// <summary>AGERC's ﾊﾞｰｼﾞｮﾝ情報 template (dialog 2) as the port shows it. The game half above the separator keeps
    /// AGE's controls: the game's icon, name, version and copyright. The engine half under ｼｽﾃﾑのﾊﾞｰｼﾞｮﾝ names this
    /// engine: its name, its license, its FFmpeg notice and where its third-party licenses are, in place of
    /// ARCGameEngine, ARC Software Laboratory's copyright, AGE's Ogg Vorbis notice and Xiphophorus' copyright. The two
    /// notices keep AGE's rows: the FFmpeg notice wraps in the 164 × 18-unit label (lines of 248 and 266 of its 287 px)
    /// and the pointer fits the 164 × 10-unit one (259 px). The name's label narrows to the name and the version edit
    /// 0x400, which the dialog fills, starts just after it: AGE's 84 units (147 px) fit "Version 4.24J" but not a
    /// development version such as "Version 0.4.0-32-gabc2403+". The copy protector's プロダクトID row (label and edit
    /// 0x3f0) is left out. AGE's labels have no id, so they are found by class and template position; Himegari's and
    /// Kamidori's templates agree on both.</summary>
    public static DialogTemplate VersionDialogTemplate(DialogTemplate native)
    {
        var items = new List<DialogItem>(native.Items.Count);
        foreach (DialogItem item in native.Items)
        {
            if (item.ClassName == "Edit" && item.Id == ProductIdEdit) continue;
            if (item.ClassName == "Edit" && item.Id == VersionEdit)
            {
                items.Add(item with { X = VersionX, Width = item.X + item.Width - VersionX });
                continue;
            }
            if (Label(item) is not { } at)
            {
                items.Add(item);
                continue;
            }
            switch (at)
            {
                case (7, 66): continue;   // プロダクトID
                case (44, 104): items.Add(item with { Text = Name, Width = NameWidth }); break;
                case (44, 114): items.Add(item with { Text = License }); break;
                case (44, 131): items.Add(item with { Text = FfmpegNotice }); break;
                case (44, 149): items.Add(item with { Text = NoticesPointer }); break;
                default: items.Add(item); break;
            }
        }
        return native with { Items = items };
    }

    private const int ProductIdEdit = 0x3f0, VersionEdit = 0x400;

    // "Open Maid Engine" is 92 px in the dialog font (ＭＳ Ｐゴシック 9 pt, base units 7×12), so a 56-unit (98 px) label
    // holds it. The version edit starts 4 units after that label (44 + 56 + 4) and keeps AGE's right edge (208): 182 px.
    private const int NameWidth = 56, VersionX = 104;

    private static (int X, int Y)? Label(DialogItem item) =>
        item.ClassName == "Static" && item.Id == -1 && item.TypeBits == DialogStyles.SsLeft ? (item.X, item.Y) : null;
}
