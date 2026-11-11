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
    public int TranslationsWithJoinedBreaks { get; set; }
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

    /// <summary>A stored literal translated differently from the global choice; the catalog cannot scope it,
    /// because the string is drawn somewhere else.</summary>
    public Sampled<ReportedConflict> UnscopableConflicts { get; } = new();

    /// <summary>A stored literal the install left untranslated where the global entry translates it: the catalog
    /// presents more than the install did, and no scope can avoid that either.</summary>
    public Sampled<ReportedConflict> UntranslatedInInstall { get; } = new();

    /// <summary>Literals compared or switched on (spec §5.1): the base literal stays, so no entry is needed.</summary>
    public List<ReportedLiteral> FeedCompares { get; } = [];

    /// <summary>An empty literal the translation filled with text, such as a stage description line the Japanese
    /// left empty: an empty source is no key, so the catalog cannot carry it.</summary>
    public Sampled<ReportedLiteral> EmptyFilled { get; } = new();

    /// <summary>Literals fed to <c>concat</c>: candidates for patterns (spec §6.3).</summary>
    public Sampled<ReportedLiteral> FeedConcat { get; } = new();

    /// <summary>Changed literals of every other opcode, which nothing presents.</summary>
    public Sampled<ReportedLiteral> Other { get; } = new();
}

/// <summary>Differences in the translated install's strings that are not translations. The importer keeps the
/// source for them, and gate 11's run of the translated install shows them as they are.</summary>
public sealed class DamageCounts
{
    /// <summary>A string equal to its source with characters missing and nothing added. The translated install's
    /// scripts lose characters from many strings the translators left in Japanese (<c>savemesskip</c> reads
    /// <c>avemesskip</c>, <c>キャンセル</c> reads <c>ャンル</c>, <c>週送り。</c> reads three ways at three sites).</summary>
    public Sampled<ReportedLiteral> LostCharacters { get; } = new();

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
}

public sealed class VerifyCounts
{
    public int CheckedMessages { get; set; }
    public int CheckedDrawLiterals { get; set; }
    public int CheckedStoredLiterals { get; set; }
    public int CheckedPatterns { get; set; }

    /// <summary>Messages, drawn literals and patterns whose presented text differs from the translated install's.
    /// Each one means two translations ended up under one key.</summary>
    public Sampled<ReportedMismatch> Mismatches { get; } = new();

    /// <summary>Stored literals whose global entry is not the translation at this site: the unscopable conflicts
    /// of <see cref="LiteralCounts.UnscopableConflicts"/>, seen from the catalog.</summary>
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
    public ScopeCounts Scope { get; } = new();
    public UnjoinedCounts Unjoined { get; } = new();
    public LiteralCounts Literals { get; } = new();
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
