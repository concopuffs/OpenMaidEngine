using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ome.Mods.Localization.Import;

/// <summary>A bounded list: the total count and the first <see cref="ImportReport.SampleLimit"/> items.</summary>
public sealed class Sampled<T>
{
    public int Count { get; private set; }
    public List<T> Samples { get; } = [];

    public void Add(T item)
    {
        Count++;
        if (Samples.Count < ImportReport.SampleLimit) Samples.Add(item);
    }
}

public sealed record NotAlignedScript(string Script, AlignmentFailure Failure);
public sealed record ScriptTargetDisagreement(string Script, TargetDisagreement Disagreement);
public sealed record ReportedLiteral(string Script, int Offset, string Opcode, string Base, string Translated);
public sealed record ReportedShortening(string Script, int Offset, string Opcode, string Base, string Translated,
                                        string Class);
public sealed record ReportedComposed(string Script, int Offset, string Source, string Text);
public sealed record ReportedSkip(string Script, int Offset, string Reason);
public sealed record ReportedText(string Script, int Offset, string Source, string Translated);
public sealed record ReportedVariants(string Script, string Source, IReadOnlyList<string> Translations);
public sealed record ReportedConflict(string Source, string Global, string Script, int Offset, string Translated);
public sealed record ReportedMismatch(string Script, int Offset, string Kind, string Source, string Expected,
                                      string Presented);

/// <summary>The scripts of the two installs and how they compared.</summary>
public sealed class ScriptCounts
{
    public int Base { get; set; }
    public int Identical { get; set; }
    public int Changed { get; set; }
    public int Aligned { get; set; }
    public List<string> BaseOnly { get; } = [];
    public List<string> TranslatedOnly { get; } = [];
    public List<NotAlignedScript> NotAligned { get; } = [];
    public List<string> Unreadable { get; } = [];
}

public sealed class AlignmentCounts
{
    public int InsertedShowText { get; set; }
    public int InsertedEndTextLine { get; set; }
    public Sampled<ScriptTargetDisagreement> TargetDisagreements { get; } = new();
}

/// <summary>Messages of the compared scripts (spec §6.2), counted per occurrence.</summary>
public sealed class MessageCounts
{
    public int Total { get; set; }
    public int Literal { get; set; }
    public int WithVariables { get; set; }
    /// <summary>Messages whose every run reads a variable: they have no text to translate.</summary>
    public int VariableOnly { get; set; }
    public int Translated { get; set; }
    public int Untranslated { get; set; }
    public int WithAuthoredBreaks { get; set; }
}

/// <summary>The line edge of one script's layout (review R1 of <c>docs/localization-import-test.md</c>): the width,
/// in half cells, that 99 of every 100 of its translated lines fit. <see cref="From"/> is <c>install</c> when the
/// script has too few lines and the edge of the whole install's lines applies. <see cref="Wider"/> counts its
/// lines wider than the edge, which the engine wraps where they cross it.</summary>
public sealed record ReportedEdge(string Script, string Layout, int Edge, string From, int Lines, int Widest, int Wider);

/// <summary>The translated messages' line breaks, each a wrap or deliberate (review R1 of
/// <c>docs/localization-import-test.md</c>). Widths are in half cells, as the engine measures text.</summary>
public sealed class BreakCounts
{
    /// <summary>Breaks that only wrap: the next word did not fit the widest translated line of the layout.</summary>
    public int Wraps { get; set; }

    /// <summary>Breaks kept as <c>\n</c> in the entry.</summary>
    public int Deliberate { get; set; }

    public int MessagesWithDeliberate { get; set; }

    /// <summary>Wraps that would be deliberate if the script's widest line were its edge: the class of these
    /// depends on the edge chosen.</summary>
    public int Uncertain { get; set; }

    /// <summary>Breaks the width makes wraps that stay deliberate because the message already has as many wraps as
    /// the translation added breaks.</summary>
    public int KeptByCount { get; set; }

    /// <summary>The messages of <see cref="KeptByCount"/>, for a spot check.</summary>
    public Sampled<ReportedText> KeptByCountSamples { get; } = new();

    public List<ReportedEdge> Edges { get; } = [];

    /// <summary>Translated lines wider than their script's edge. The engine wraps them where they cross it, and some
    /// carry spaces that push a word past the edge.</summary>
    public Sampled<ReportedText> WiderThanEdge { get; } = new();

    /// <summary>For a spot check: the base breaks the message itself, and the translation has no deliberate
    /// break.</summary>
    public Sampled<ReportedText> AuthoredWithoutDeliberate { get; } = new();

    /// <summary>For a spot check: the translation has a deliberate break where the base has none.</summary>
    public Sampled<ReportedText> DeliberateWithoutAuthored { get; } = new();
}

/// <summary>Which scope served each converted message (decision 2: the key is the source text).</summary>
public sealed class ScopeCounts
{
    public int GlobalEntries { get; set; }
    public int ScriptEntries { get; set; }
    public int SiteEntries { get; set; }
    public int PatternEntries { get; set; }
    public int MessagesAtGlobal { get; set; }
    public int MessagesAtScript { get; set; }
    public int MessagesAtSite { get; set; }
    public int MessagesUntranslated { get; set; }
    public int DistinctSources { get; set; }
    public int SourcesWithOneTranslation { get; set; }
    public int SourcesWithSeveralTranslations { get; set; }

    /// <summary>A source translated more than one way inside one script: what needs site scope.</summary>
    public Sampled<ReportedVariants> TranslatedSeveralWaysInOneScript { get; } = new();
}

/// <summary>Messages the catalog could not carry as one entry (spec §6.2).</summary>
public sealed class UnjoinedCounts
{
    /// <summary>A message whose translated span is empty: its text moved to a neighbouring message.</summary>
    public Sampled<ReportedText> Emptied { get; } = new();

    /// <summary>Added text between two messages that only a branch target or a layout splits, so which of the two
    /// it belongs to is a guess (it is given to the first).</summary>
    public Sampled<ReportedText> AmbiguousBoundary { get; } = new();

    /// <summary>Added text instructions that fall in no message's span.</summary>
    public Sampled<ReportedText> OrphanInsertions { get; } = new();

    /// <summary>A translated text operand that does not decode as CP932 even leniently.</summary>
    public Sampled<ReportedText> DamagedStrings { get; } = new();

    /// <summary>An added text instruction that reads a variable, which no base run accounts for.</summary>
    public Sampled<ReportedText> InsertedVariables { get; } = new();

    /// <summary>A message or literal that does not make a valid entry.</summary>
    public Sampled<ReportedText> Unrepresentable { get; } = new();
}

/// <summary>Changed string literals outside messages, by what consumes them (spec §18.1).</summary>
public sealed class LiteralCounts
{
    public Dictionary<string, int> ChangedByOpcode { get; } = new(StringComparer.Ordinal);

    /// <summary>A literal stored into a frame local string, translated differently from the global choice. No
    /// record names the store (review R2 of <c>docs/localization-import-test.md</c>), so the catalog cannot scope
    /// it.</summary>
    public Sampled<ReportedConflict> UnscopableConflicts { get; } = new();

    /// <summary>A stored literal the install left untranslated where the catalog translates it: an untranslated
    /// copy gets no entry, so the catalog presents more than the install did.</summary>
    public Sampled<ReportedConflict> UntranslatedInInstall { get; } = new();

    /// <summary>Literals compared or switched on (spec §5.1): the base literal stays, so no entry is needed.</summary>
    public List<ReportedLiteral> FeedCompares { get; } = [];

    /// <summary>An empty literal the translation filled with text, with no line before it to fold into: an empty
    /// source is no key, so the catalog cannot carry it.</summary>
    public Sampled<ReportedLiteral> EmptyFilled { get; } = new();

    /// <summary>An empty stored line the translation filled with text, folded after <c>\n</c> into the entry of
    /// the line stored to the cell before it (review R2), such as a stage description's fourth line.</summary>
    public Sampled<ReportedLiteral> EmptyFolded { get; } = new();

    /// <summary>Literals fed to <c>concat</c>: candidates for patterns (spec §6.3).</summary>
    public Sampled<ReportedLiteral> FeedConcat { get; } = new();

    /// <summary>Changed literals of every other opcode, which nothing presents.</summary>
    public Sampled<ReportedLiteral> Other { get; } = new();
}

/// <summary>Strings the scripts compose with <c>concat</c> and <c>toString</c> (review R4 of
/// <c>docs/localization-import-test.md</c>), each a pattern with the composing script's scope.</summary>
public sealed class ComposedCounts
{
    /// <summary>Stores and draws of a composed string with at least one template.</summary>
    public int Stores { get; set; }

    /// <summary>Distinct templates, one per path that builds a different one.</summary>
    public int Templates { get; set; }

    /// <summary>Templates whose translation is the source.</summary>
    public int Untranslated { get; set; }

    public int Entries { get; set; }

    /// <summary>The templates with literal text and their translations, for review.</summary>
    public Sampled<ReportedComposed> Patterns { get; } = new();

    /// <summary>Stores whose walk back gave up, with the reason: <c>paths</c> (too many), <c>steps</c> (too far),
    /// <c>loop</c>, or <c>function entry</c> (no write starts the value inside the function). A store that also
    /// gave templates is listed too: some of its paths are missing.</summary>
    public Sampled<ReportedSkip> Skipped { get; } = new();

    /// <summary>Paths whose template cannot be written: an operand used twice, or a literal that does not
    /// decode.</summary>
    public Sampled<ReportedComposed> Unreadable { get; } = new();
}

/// <summary>Differences in the translated install's strings that are not translations. The importer keeps the
/// source for them, and gate 11's run of the translated install shows them as they are.</summary>
public sealed class DamageCounts
{
    /// <summary>A string equal to its source with characters missing and nothing added. The translated install's
    /// scripts lose characters from many strings the translators left in Japanese (<c>savemesskip</c> reads
    /// <c>avemesskip</c>, <c>キャンセル</c> reads <c>ャンル</c>, <c>週送り。</c> reads three ways at three sites).</summary>
    public Sampled<ReportedLiteral> LostCharacters { get; } = new();

    /// <summary>For review: every string that lost more than one character, with its class. <c>damage</c> keeps a
    /// letter or digit and is kept as source; <c>translation</c> keeps only punctuation and is kept as written.</summary>
    public Sampled<ReportedShortening> LostSeveral { get; } = new();

    /// <summary>The strings of <see cref="LostSeveral"/> kept as translations.</summary>
    public int ShortenedTranslations { get; set; }

    /// <summary>An empty literal the translated install writes as spaces; it draws the same, so it needs no entry.
    /// Compares are listed under <see cref="LiteralCounts.FeedCompares"/> instead.</summary>
    public Sampled<ReportedLiteral> EmptyWrittenAsSpace { get; } = new();

    /// <summary>A translated string with control characters, which the runtime's string decoder rejects
    /// (<c>Sys4StringCodec</c>); the importer reads it leniently, with each control character as a space.</summary>
    public Sampled<ReportedLiteral> ControlCharacters { get; } = new();
}

public sealed class FuriganaCounts
{
    public int BaseRuns { get; set; }
    public int TranslatedEmptied { get; set; }
    public int TranslatedKeptReading { get; set; }
}

/// <summary>How the installs' non-script files compared (spec §12, §18.1).</summary>
public sealed class AssetCounts
{
    public string ArchivesCompared { get; set; } = "size";
    public List<string> ReplacedImages { get; } = [];
    public List<string> ChangedOther { get; } = [];
    public List<string> BaseOnly { get; } = [];
    public List<string> TranslatedOnly { get; } = [];
    public List<string> NonAssetFiles { get; } = [];

    /// <summary>With <c>--package</c>: how many replaced images were written to <c>assets/&lt;language&gt;/</c>;
    /// null without it.</summary>
    public int? Written { get; set; }
    /// <summary>With <c>--package</c>: the replaced images that could not be written, each with the reason.</summary>
    public List<string> NotWritten { get; } = [];
}

public sealed class VerifyCounts
{
    public int CheckedMessages { get; set; }
    public int CheckedDrawLiterals { get; set; }
    public int CheckedStoredLiterals { get; set; }
    public int CheckedPatterns { get; set; }
    public int CheckedComposed { get; set; }

    /// <summary>Messages, drawn literals and patterns whose presented text differs from the translated install's.
    /// Each one means two translations ended up under one key.</summary>
    public Sampled<ReportedMismatch> Mismatches { get; } = new();

    /// <summary>Literals stored into a frame local whose global entry is not the translation at this site: the
    /// unscopable conflicts of <see cref="LiteralCounts.UnscopableConflicts"/>, seen from the catalog. A literal
    /// stored into a string cell is checked at its storing site, and a difference there is a mismatch.</summary>
    public Sampled<ReportedMismatch> StoredLiteralConflicts { get; } = new();

    /// <summary>Stored literals the install left untranslated that the catalog translates
    /// (<see cref="LiteralCounts.UntranslatedInInstall"/>).</summary>
    public Sampled<ReportedMismatch> StoredLiteralsTranslatedAnyway { get; } = new();
}

/// <summary>
/// <c>import-report.json</c> (localization spec §18.1): what <c>omp text import-scripts</c> found when it aligned a
/// translated install with its base, and what the catalog could and could not carry.
/// </summary>
public sealed class ImportReport
{
    public const int SampleLimit = 200;

    public string Language { get; set; } = "";
    public string BaseRoot { get; set; } = "";
    public string TranslatedRoot { get; set; } = "";
    public List<string> TranslatedOverlays { get; set; } = [];
    public ScriptCounts Scripts { get; } = new();
    public AlignmentCounts Alignment { get; } = new();
    public MessageCounts Messages { get; } = new();
    public BreakCounts Breaks { get; } = new();
    public ScopeCounts Scope { get; } = new();
    public UnjoinedCounts Unjoined { get; } = new();
    public LiteralCounts Literals { get; } = new();
    public ComposedCounts Composed { get; } = new();
    public DamageCounts Damage { get; } = new();
    public FuriganaCounts Furigana { get; } = new();
    public AssetCounts? Assets { get; set; }
    public VerifyCounts? Verify { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
