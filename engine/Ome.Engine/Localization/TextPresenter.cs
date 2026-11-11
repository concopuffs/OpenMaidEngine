using Ome.Engine.Model;
using Ome.Engine.Text;

namespace Ome.Engine.Localization;

/// <summary>
/// Where a string reaches the boundary (localization spec §5.2): the script whose instruction draws it, and that
/// instruction's dword offset. <see cref="BaseOffset"/> is null when the running script is not the base script (a
/// mod patched it), since a site entry names a base offset (§6.1). The same form names a string's origin: the
/// instruction that stored it into a global string cell.
/// </summary>
public readonly record struct TextSite(string Script, int Offset, bool IsBaseScript = true)
{
    public int? BaseOffset => IsBaseScript ? Offset : null;

    public override string ToString() => $"{TextCatalog.NormalizeScriptName(Script)}@0x{Offset:x}";
}

/// <summary>What the presenter was asked for one run or message (localization spec §8): the source text, the drawing
/// site and the origin. History presents the run again from it.</summary>
public readonly record struct TextLookup(string Source, TextSite Site, TextSite? Origin);

/// <summary>What the presenter gives the host for one string: the text to draw, how it may break (§7.1), and the
/// catalog entry that supplied it, if one did.</summary>
public sealed record PresentedText(string Text, LineBreakMode LineBreak, TextEntry? Entry);

/// <summary>What a filter sees (spec §6.5): the source text, the catalog's result if any, the language, the drawing
/// site, the origin (§5.2; the site itself when the string has no record) and a pattern's captures, already
/// resolved (empty for an exact entry or a miss).</summary>
public readonly record struct TextFilterInput(string Source, string? CatalogText, string Language, TextSite Site,
                                              TextSite Origin, IReadOnlyDictionary<string, string> Captures);

/// <summary>
/// A presentation filter (spec §6.5). It runs after the catalog and may return a different presented text, or null
/// to keep what it was given. It must be a pure function of its input: its result is cached like the catalog's.
/// </summary>
public interface ITextFilter
{
    string? Present(in TextFilterInput input);
}

/// <summary>Whether a <c>num</c> capture and a string that is only a number keep their full-width digits
/// (spec §6.3, §9.2).</summary>
public enum DigitsMode { Keep, Ascii }

/// <summary>Which length opcode is measuring (spec §5.3).</summary>
public enum TextLengthKind
{
    /// <summary><c>strlen</c>, op <c>0x2c5</c>: half cells.</summary>
    HalfCells,
    /// <summary><c>halve-strlen</c>, op <c>0x1a6</c>: half cells halved, rounded up.</summary>
    Halved,
    /// <summary><c>cp932-character-length</c>, op <c>0x2c6</c>: characters.</summary>
    Characters,
}

/// <summary>The messages of the scripts the VM runs (spec §6.2): segmented once per loaded script. Null when a
/// script cannot be segmented; its runs then resolve one by one.</summary>
public interface ITextMessageIndex
{
    ScriptMessages? MessagesOf(Script script);
}

/// <summary>
/// The selected language, as the presenter resolves it (spec §6.4, §10): its tag, its catalogs in fallback order
/// (the language, then each fallback; source comes after the last), and its settings (§9.2).
/// </summary>
public sealed record TextLanguage(string Tag, IReadOnlyList<TextCatalog> Chain)
{
    public LineBreakMode LineBreak { get; init; } = LineBreakMode.Words;

    /// <summary>What the length opcodes measure (§5.3); an entry's own <c>measure</c> overrides it.</summary>
    public TextMeasure Measure { get; init; } = TextMeasure.Presented;

    public DigitsMode Digits { get; init; } = DigitsMode.Keep;

    /// <summary>The pseudo-language (spec §14): no catalog, every string lengthened and bracketed.</summary>
    public static TextLanguage Pseudo { get; } = new(LanguageTag.Pseudo, []);
}

/// <summary>
/// Turns source text into presented text (localization spec §4, §5.2, §6). A string is resolved for the instruction
/// that draws it and its origin, the instruction that stored it (§5.2), in the order of §6.4: exact entries at the
/// origin's site, the drawing site, the origin's script, the drawing script and global, then patterns in the same
/// scope order with their captures resolved, then the filters; each catalog of the fallback chain in turn. A result,
/// a miss included, is cached by source text, site and origin in a bounded least-recently-used cache. The presenter
/// is called only on the VM thread; changing language makes a new presenter, so a presenter never changes.
/// </summary>
public sealed class TextPresenter
{
    /// <summary>Results kept per presenter. Menus redraw the same strings every frame, and Himegari has about 17,400
    /// variable <c>draw-string</c> sites, so the cache needs a bound (spec §6.4).</summary>
    public const int DefaultCacheCapacity = 8192;

    private static readonly IReadOnlyDictionary<string, string> NoCaptures = new Dictionary<string, string>();

    /// <summary>The site a filter sees for a string whose site is unknown (history restored from a save, §8).</summary>
    private static readonly TextSite UnknownSite = new("", 0, IsBaseScript: false);

    private delegate bool ExactLookup(TextCatalog catalog, string source, out TextEntry entry);

    private readonly IReadOnlyList<ITextFilter> _filters;
    private readonly TextMissLog? _misses;
    private readonly BoundedLruCache<CacheKey, Result> _cache;
    private readonly BoundedLruCache<string, Result> _unscopedCache;

    public TextPresenter(TextLanguage language, IEnumerable<ITextFilter>? filters = null, TextMissLog? misses = null,
                         int cacheCapacity = DefaultCacheCapacity, ITextMessageIndex? messages = null)
    {
        Language = language;
        _filters = (filters ?? []).ToArray();
        _misses = misses;
        _cache = new BoundedLruCache<CacheKey, Result>(cacheCapacity);
        _unscopedCache = new BoundedLruCache<string, Result>(Math.Max(1, cacheCapacity / 8));
        Messages = messages;
    }

    public TextLanguage Language { get; }

    /// <summary>The message tables (§6.2), or null to resolve every run on its own.</summary>
    public ITextMessageIndex? Messages { get; }

    /// <summary>The presented form of <paramref name="source"/> drawn at <paramref name="site"/>, or null to draw the
    /// source text. <paramref name="origin"/> is the instruction that stored the string (§5.2), or null when it has
    /// none. A string with no catalog entry is a miss and goes to the miss log when <paramref name="logMiss"/> is
    /// set, even when a filter presents it; a string that is only a number is never one.</summary>
    public PresentedText? Present(string source, TextSite site, TextSite? origin = null, bool logMiss = true)
    {
        if (source.Length == 0) return null;
        Result result = Lookup(source, site, origin ?? site);
        if (result.Missed && logMiss) _misses?.Record(source, site);
        return result.Presented;
    }

    /// <summary>What a length opcode at <paramref name="site"/> returns for <paramref name="source"/> (spec §5.3), or
    /// null for the native result: the string resolves to no entry, or the language or the entry measures source.
    /// A string the language's <c>digits</c> setting converts counts as resolved. Misses are not logged here.</summary>
    public int? Measure(string source, TextSite site, TextSite? origin, TextLengthKind kind)
    {
        if (source.Length == 0) return null;
        Result result = Lookup(source, site, origin ?? site);
        if (!result.Measurable || result.Presented is not { } presented) return null;
        if ((presented.Entry?.Measure ?? Language.Measure) == TextMeasure.Source) return null;
        if (kind == TextLengthKind.Characters) return TextMeasurement.Characters(presented.Text);
        int cells = presented.Entry?.Cells ?? TextMeasurement.HalfCells(presented.Text);
        return kind == TextLengthKind.Halved ? TextMeasurement.Halve(cells) : cells;
    }

    /// <summary>
    /// The presented form of a history string whose site is unknown, from a group a save restored (spec §8): exact
    /// entries of every scope, a global one first, then patterns of every scope, then the filters, each catalog of the
    /// chain in turn. Misses are not logged.
    /// </summary>
    public PresentedText? PresentUnscoped(string source)
    {
        if (source.Length == 0) return null;
        if (!_unscopedCache.TryGetValue(source, out Result result))
        {
            result = Resolve(source, UnknownSite, UnknownSite,
                (TextCatalog catalog, string text, out TextEntry entry) => catalog.TryGetUnscoped(text, out entry),
                catalog => [catalog.UnscopedPatterns]);
            _unscopedCache.Set(source, result);
        }
        return result.Presented;
    }

    private Result Lookup(string source, TextSite site, TextSite origin)
    {
        var key = new CacheKey(source, Scope.Of(site), Scope.Of(origin));
        if (!_cache.TryGetValue(key, out Result result))
        {
            Scope drawn = key.Site, from = key.Origin;
            result = Resolve(source, site, origin,
                (TextCatalog catalog, string text, out TextEntry entry) => TryExact(catalog, text, drawn, from, out entry),
                catalog => PatternScopes(catalog, drawn, from));
            _cache.Set(key, result);
        }
        return result;
    }

    /// <summary>Resolve <paramref name="source"/> through the chain with <paramref name="exact"/> lookups and the
    /// <paramref name="patterns"/> lists in the order they are tried, then the filters.</summary>
    private Result Resolve(string source, TextSite site, TextSite originSite, ExactLookup exact,
                           Func<TextCatalog, IEnumerable<IReadOnlyList<TextEntry>>> patterns)
    {
        TextEntry? found = null;
        string? text = null;
        IReadOnlyDictionary<string, string> captures = NoCaptures;
        foreach (TextCatalog catalog in Language.Chain)
        {
            if (exact(catalog, source, out TextEntry literal))
            {
                (found, text) = (literal, literal.TextTemplate.Literal);
                break;
            }
            if (TryPattern(catalog, source, exact, patterns, out TextEntry pattern, out var matched))
            {
                captures = matched;
                (found, text) = (pattern, pattern.TextTemplate.Render(matched));
                break;
            }
        }
        bool number = found == null && TextPatternMatcher.IsNumber(source);
        if (number && Language.Digits == DigitsMode.Ascii) text = TextPatternMatcher.AsciiDigits(source);
        bool measurable = text != null;
        foreach (ITextFilter filter in _filters)
            text = filter.Present(new TextFilterInput(source, text, Language.Tag, site, originSite, captures)) ?? text;
        PresentedText? presented = text == null ? null : new PresentedText(text, Language.LineBreak, found);
        return new Result(presented, Missed: found == null && !number, measurable);
    }

    /// <summary>Spec §6.4 steps 1–5 in one catalog: site scope at the origin, then at the drawing instruction,
    /// script scope for the origin's script, then the drawing script, then global. For a string with no record the
    /// origin is the drawing instruction, so the steps pair up.</summary>
    private static bool TryExact(TextCatalog catalog, string source, Scope drawn, Scope origin, out TextEntry entry)
    {
        if (origin.At is { } originAt && catalog.TryGetSite(origin.Script, originAt, source, out entry)) return true;
        if (drawn.At is { } drawnAt && drawn != origin && catalog.TryGetSite(drawn.Script, drawnAt, source, out entry))
            return true;
        if (catalog.TryGetScript(origin.Script, source, out entry)) return true;
        if (drawn.Script != origin.Script && catalog.TryGetScript(drawn.Script, source, out entry)) return true;
        return catalog.TryGetGlobal(source, out entry);
    }

    /// <summary>Spec §6.4 step 6: patterns in the same scope order, the one with the most literal characters first.
    /// Each <c>{name}</c> capture is resolved as a string drawn at the same site; a <c>num</c> capture follows the
    /// language's <c>digits</c> setting.</summary>
    private bool TryPattern(TextCatalog catalog, string source, ExactLookup exact,
                            Func<TextCatalog, IEnumerable<IReadOnlyList<TextEntry>>> patterns, out TextEntry entry,
                            out IReadOnlyDictionary<string, string> captures)
    {
        foreach (IReadOnlyList<TextEntry> scoped in patterns(catalog))
            foreach (TextEntry candidate in scoped)
            {
                if (!TextPatternMatcher.TryMatch(candidate.SourceTemplate, source, out var matched)) continue;
                foreach (PlaceholderPart placeholder in candidate.SourceTemplate.Placeholders)
                    matched[placeholder.Name] = ResolveCapture(matched[placeholder.Name], placeholder.Kind, exact);
                (entry, captures) = (candidate, matched);
                return true;
            }
        (entry, captures) = (null!, NoCaptures);
        return false;
    }

    private static IEnumerable<IReadOnlyList<TextEntry>> PatternScopes(TextCatalog catalog, Scope drawn, Scope origin)
    {
        if (origin.At is { } originAt) yield return catalog.SitePatterns(origin.Script, originAt);
        if (drawn.At is { } drawnAt && drawn != origin) yield return catalog.SitePatterns(drawn.Script, drawnAt);
        yield return catalog.ScriptPatterns(origin.Script);
        if (drawn.Script != origin.Script) yield return catalog.ScriptPatterns(drawn.Script);
        yield return catalog.GlobalPatterns;
    }

    /// <summary>A capture's presented text: an exact entry through the fallback chain, else the captured text, with
    /// a number's digits made ASCII when the language asks for it.</summary>
    private string ResolveCapture(string captured, PlaceholderKind kind, ExactLookup exact)
    {
        if (kind == PlaceholderKind.Any)
            foreach (TextCatalog catalog in Language.Chain)
                if (exact(catalog, captured, out TextEntry entry))
                    return entry.TextTemplate.Literal;
        return Language.Digits == DigitsMode.Ascii && TextPatternMatcher.IsNumber(captured)
            ? TextPatternMatcher.AsciiDigits(captured)
            : captured;
    }

    /// <summary>A site as a lookup scope: the normalized script, and the base offset a site entry can name.</summary>
    private readonly record struct Scope(string Script, int Offset, bool IsBase)
    {
        public int? At => IsBase ? Offset : null;

        public static Scope Of(TextSite site)
            => new(TextCatalog.NormalizeScriptName(site.Script), site.Offset, site.IsBaseScript);
    }

    private readonly record struct CacheKey(string Source, Scope Site, Scope Origin);

    /// <summary><see cref="Measurable"/>: the presented text came from an entry or the <c>digits</c> setting, so the
    /// length opcodes follow it (a filter's text alone does not count).</summary>
    private readonly record struct Result(PresentedText? Presented, bool Missed, bool Measurable);
}
