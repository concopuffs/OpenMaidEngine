using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;
using Ome.Engine.Profiles;
using Ome.Engine.Text;

namespace Ome.Engine.Vm;

/// <summary>Every rule an engine build gives the natives (docs/superpowers/specs/2026-11-12-engine-layering-design.md
/// §7.3). The rule types live beside the natives that read them.</summary>
public sealed record EngineRules(TextRules Text, GfxRules Gfx, AudioRules Audio, SettingsRules Settings,
                                 SaveRules Saves, ShellRules Shell);

/// <summary>
/// One build of native AGE whose behaviour the port reproduces (spec §7.1). <see cref="Id"/> is the version the build
/// names itself by, and <see cref="AbiId"/> the opcode table it runs. Both are read only where the engine is put
/// together: code below that point takes <see cref="Rules"/> (spec §7.6).
/// </summary>
public sealed record EngineBuild(string Id, string AbiId, EngineRules Rules);

/// <summary>
/// The engine builds the port reproduces, each declared as its parent plus changes (spec §7.4). A change names the
/// behaviour, never the build, and its comment points to the evidence. Adding a game: spec §7.8.
/// </summary>
public static class EngineBuilds
{
    /// <summary>AGE 4.24J, Himegari's (its version resource and the <c>424J</c> stamp in every save it writes;
    /// docs/engine-re.md, "Comparing engine builds").</summary>
    public static EngineBuild Age424J { get; } = new(
        Id: "4.24J",
        AbiId: "SYS4422",
        Rules: new EngineRules(
            Text: new TextRules(
                GlyphCoverage: GlyphCoverageAlpha.Sixteenths,
                Ruby: RubyRule.FixedOffsets,
                FuriganaArmsGlyphTimer: false,
                HistoryLookupTestsSign: false,
                LineEnd: LineEndRule.FixedThree,
                ExtendedGlyphFace: null,
                AspectCalibration: AspectCalibration.BlackBoxOverCellIncrement,
                PublisherGrowsGlyphCells: false),
            Gfx: new GfxRules(UnboundObjectReadsAbsent: false),
            Audio: new AudioRules(PlayComparesStartMode: false),
            Settings: SettingsRules.LoaderKeys,
            Saves: new SaveRules(versionStamp: "424J", numberedGfxRecordSize: NativeGfxSaveLayout.BaseRecordSize),
            Shell: new ShellRules(AutoMessagePage: false)));

    /// <summary>AGE 4.33G, Kamidori's clean install (docs/engine-re.md, "Kamidori engine builds"). The profile runs this
    /// build whichever executable the player has (spec §7.5).</summary>
    public static EngineBuild Age433G { get; } = Age424J with
    {
        Id = "4.33G",
        AbiId = "SYS4433",
        Rules = Age424J.Rules with
        {
            Text = Age424J.Rules.Text with
            {
                // text_blit_glyph_bitmap_sys4433@0x45cff0 divides coverage by 17.
                GlyphCoverage = GlyphCoverageAlpha.Seventeenths,
                // adv_text_draw_ruby_over_row_sys4433@0x4590d0 reads message:RubyShiftMode and set:DrawMode.
                Ruby = RubyRule.ShiftModes,
                // Its op 0x196 handler re-arms the glyph timer as op 0x6e does.
                FuriganaArmsGlyphTimer = true,
                // text_history_find_typed_value@0x4501b0 and text_history_find_pair@0x450270 return for a negative
                // record index.
                HistoryLookupTestsSign = true,
                // adv_text_publish_next_glyph@0x4509f0 and adv_text_publish_layout@0x4503b0 add the effect's vertical
                // offset to the bottom of the rectangle they bind.
                PublisherGrowsGlyphCells = true,
            },
            // Worker 0x486120 tests the record's bound bit before it returns the slot.
            Gfx = Age424J.Rules.Gfx with { UnboundObjectReadsAbsent = true },
            // bgm_play_track@0x469e90 returns early only for the retained track with the retained start mode.
            Audio = Age424J.Rules.Audio with { PlayComparesStartMode = true },
            // The SYS4REG.INI loader 0x472530 binds two more keys, and a native exit writes both as 0.
            Settings = Age424J.Rules.Settings
                .BindingAfter("LimitAero", new Sys4RegIniKey("display", "VirtualFullScreenType", "0"))
                .BindingAfter("UseAntiFont", new Sys4RegIniKey("message", "AntiFontLevel", "0")),
            // Numbered saves append a rotation cache to every graphics record. context_state_serialize@0x40d1e0 and
            // save_data_deserialize_and_begin_restore@0x40faa0 copy the float globals and the float pointers when the
            // save's minor version is above 0x13; 4.24J's have no such branch.
            Saves = new SaveRules(versionStamp: "433G", numberedGfxRecordSize: NativeGfxSaveLayout.RotationCacheRecordSize,
                                  floatSectionsFromMinor: 20),
        },
    };

    /// <summary>AGE 4.38D, 創刻のアテリアル's (its version resource; docs/engine-re.md, "Comparing engine builds"). Its
    /// changes are the real rows of the revision diff's triage (docs/engine-re.md, "Kamidori against Arterial,
    /// triaged"); the ones the port does not have yet are in the game's item in docs/backlog.md.</summary>
    public static EngineBuild Age438D { get; } = Age433G with
    {
        Id = "4.38D",
        AbiId = "SYS4438",
        Rules = Age433G.Rules with
        {
            Text = Age433G.Rules.Text with
            {
                // adv_text_append_horizontal_glyph_records@0x466000 asks text_is_justify_character@0x457950, whose
                // list is the catalog trailer's JUSTIFYCHAR values.
                LineEnd = LineEndRule.CatalogList,
                // text_draw_glyph_string_plain@0x4692f0 and text_draw_glyph_string_effect@0x46b1d0 select the font
                // text_rebuild_primary_font_resources@0x456800 creates with this face for codes 0xF040 to 0xF9FC.
                ExtendedGlyphFace = "AGE Extend",
                // text_calibrate_mode1_glyph_font@0x454ab0 divides the two black box widths.
                AspectCalibration = AspectCalibration.BlackBoxRatio,
            },
            // engine_settings_save@0x47d1d0 writes four more keys; a SYS4REG.INI native 4.38D wrote has them in
            // these places.
            Settings = Age433G.Rules.Settings
                .BindingAfter("VirtualFullScreenType", new Sys4RegIniKey("display", "MultiSample", "0"))
                .BindingAfter("AutoMessageTime1", new Sys4RegIniKey("message", "AutoMessagePitch0", "0"))
                .BindingAfter("AutoMessagePitch0", new Sys4RegIniKey("message", "AutoMessagePitch1", "0"))
                .BindingAfter("AutoMessagePitch1", new Sys4RegIniKey("message", "AutoMessageOption", "0")),
            // The stamp follows the version, as 4.24J's and 4.33G's do; no 4.38D save has been read. The record size
            // is 4.33G's, unproven: the profile's writes stay off until a native save is read.
            // The float sections are 4.33G's, read in 0x40d5a0 and 0x40fe90.
            Saves = new SaveRules(versionStamp: "438D", numberedGfxRecordSize: NativeGfxSaveLayout.RotationCacheRecordSize,
                                  floatSectionsFromMinor: 20),
            // Its AGERC menu 110 and 設定 dialog, read from the running game.
            Shell = new ShellRules(AutoMessagePage: true),
        },
    };

    /// <summary>Every build, parents first.</summary>
    public static IReadOnlyList<EngineBuild> All { get; } = [Age424J, Age433G, Age438D];

    public static EngineBuild? Find(string id)
        => All.FirstOrDefault(build => string.Equals(build.Id, id, StringComparison.Ordinal));

    /// <summary>The build a profile names. Its manifest also names the ABI, for the tools that read manifests; the
    /// two must agree.</summary>
    public static EngineBuild ForProfile(GameProfileManifest profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        EngineBuild build = Find(profile.EngineBuildId)
            ?? throw new InvalidDataException(
                $"profile '{profile.Id}' names engine build '{profile.EngineBuildId}', which is not declared; "
                + $"builds: {string.Join(", ", All.Select(known => known.Id))}");
        if (!string.Equals(build.AbiId, profile.EngineAbiId, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"profile '{profile.Id}' names ABI '{profile.EngineAbiId}', but its engine build {build.Id} runs "
                + $"'{build.AbiId}'");
        return build;
    }

    /// <summary>
    /// The build a VM runs over <paramref name="table"/>: <paramref name="build"/> when the composition named one (it
    /// must run that table's ABI), else, for tests and tools that run a bare table, the first build declared for the
    /// table's ABI, or <see cref="Age424J"/> for a table of no build's ABI (the opcode catalog, a test's own table).
    /// </summary>
    public static EngineBuild ForVm(EngineBuild? build, OpcodeTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (build == null)
            return All.FirstOrDefault(known => string.Equals(known.AbiId, table.AbiId, StringComparison.Ordinal))
                   ?? Age424J;
        if (!string.Equals(build.AbiId, table.AbiId, StringComparison.Ordinal))
            throw new ArgumentException(
                $"engine build {build.Id} runs ABI '{build.AbiId}', not the opcode table's '{table.AbiId}'",
                nameof(build));
        return build;
    }
}
