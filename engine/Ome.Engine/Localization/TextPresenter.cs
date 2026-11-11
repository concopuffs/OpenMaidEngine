using Ome.Engine.Text;

namespace Ome.Engine.Localization;

/// <summary>
/// Where a string reaches the boundary (localization spec §5.2): the script whose instruction draws it, and that
/// instruction's dword offset. <see cref="BaseOffset"/> is null when the running script is not the base script (a
/// mod patched it), since a site entry names a base offset (§6.1).
/// </summary>
public readonly record struct TextSite(string Script, int Offset, bool IsBaseScript = true)
{
    public int? BaseOffset => IsBaseScript ? Offset : null;

    public override string ToString() => $"{TextCatalog.NormalizeScriptName(Script)}@0x{Offset:x}";
}

/// <summary>What the presenter gives the host for one string: the text to draw, how it may break (§7.1), and the
/// catalog entry that supplied it, if one did.</summary>
public sealed record PresentedText(string Text, LineBreakMode LineBreak, TextEntry? Entry);

/// <summary>What a filter sees (spec §6.5): the source text, the catalog's result if any, the language and the site.
/// Captures join this in milestone 2, with patterns.</summary>
public readonly record struct TextFilterInput(string Source, string? CatalogText, string Language, TextSite Site);

/// <summary>
/// A presentation filter (spec §6.5). It runs after the catalog and may return a different presented text, or null
/// to keep what it was given. It must be a pure function of its input: its result is cached like the catalog's.
/// </summary>
public interface ITextFilter
{
    string? Present(in TextFilterInput input);
}

/// <summary>
/// The selected language, as the presenter resolves it (spec §6.4, §10): its tag, its catalogs in fallback order
/// (the language, then each fallback; source comes after the last), and its settings.
/// </summary>
public sealed record TextLanguage(string Tag, IReadOnlyList<TextCatalog> Chain)
{
    public LineBreakMode LineBreak { get; init; } = LineBreakMode.Words;

    /// <summary>The pseudo-language (spec §14): no catalog, every string lengthened and bracketed.</summary>
    public static TextLanguage Pseudo { get; } = new(LanguageTag.Pseudo, []);
}

/// <summary>
/// Turns source text into presented text (localization spec §4, §5.2). Milestone 1 resolves exact entries at the
/// two presentation sites through the language's fallback chain, then runs the filter chain; patterns, messages and
/// measurement come in milestone 2. A result, a miss included, is cached by source text and site in a bounded
/// least-recently-used cache. The presenter is called only on the VM thread; changing language makes a new
/// presenter, so a presenter never changes.
/// </summary>
public sealed class TextPresenter
{
    /// <summary>Results kept per presenter. Menus redraw the same strings every frame, and Himegari has about 17,400
    /// variable <c>draw-string</c> sites, so the cache needs a bound (spec §6.4).</summary>
    public const int DefaultCacheCapacity = 8192;

    private readonly IReadOnlyList<ITextFilter> _filters;
    private readonly TextMissLog? _misses;
    private readonly BoundedLruCache<(string Source, string Script, int Offset, bool Base), Result> _cache;

    public TextPresenter(TextLanguage language, IEnumerable<ITextFilter>? filters = null, TextMissLog? misses = null,
                         int cacheCapacity = DefaultCacheCapacity)
    {
        Language = language;
        _filters = (filters ?? []).ToArray();
        _misses = misses;
        _cache = new BoundedLruCache<(string, string, int, bool), Result>(cacheCapacity);
    }

    public TextLanguage Language { get; }

    /// <summary>The presented form of <paramref name="source"/> at <paramref name="site"/>, or null to draw the
    /// source text. A string with no catalog entry is a miss and goes to the miss log, even when a filter presents
    /// it.</summary>
    public PresentedText? Present(string source, TextSite site)
    {
        if (source.Length == 0) return null;
        var key = (source, TextCatalog.NormalizeScriptName(site.Script), site.Offset, site.IsBaseScript);
        if (!_cache.TryGetValue(key, out Result result))
        {
            result = Resolve(source, site);
            _cache.Set(key, result);
        }
        if (result.Missed) _misses?.Record(source, site);
        return result.Presented;
    }

    private Result Resolve(string source, TextSite site)
    {
        TextEntry? found = null;
        foreach (TextCatalog catalog in Language.Chain)
            if (catalog.TryResolveExact(source, site.Script, site.BaseOffset, out TextEntry entry))
            {
                found = entry;
                break;
            }
        string? text = found?.TextTemplate.Literal;
        foreach (ITextFilter filter in _filters)
            text = filter.Present(new TextFilterInput(source, text, Language.Tag, site)) ?? text;
        PresentedText? presented = text == null ? null : new PresentedText(text, Language.LineBreak, found);
        return new Result(presented, found == null);
    }

    private readonly record struct Result(PresentedText? Presented, bool Missed);
}
