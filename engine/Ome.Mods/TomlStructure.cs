using System.Globalization;
using System.Text;
using Ome.Engine.Modding;
using Tomlyn.Parsing;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Ome.Mods;

/// <summary>A key or a table that a TOML text defines where TOML 1.0 does not let it, or an array of tables continued
/// where Tomlyn's model would lose its earlier tables: at <see cref="At"/> (0-based, as Tomlyn's positions are).</summary>
internal sealed record TomlStructureProblem(TomlTextPosition At, string Message);

/// <summary>What <see cref="TomlStructure.Walk"/> found. <see cref="Nodes"/> counts every value, table and array of
/// the document but its own table, up to where the walk stopped.</summary>
internal sealed record TomlStructureResult(IReadOnlyList<TomlStructureProblem> Problems, int Nodes, bool PastBound,
                                           TomlTextPosition? Unread);

/// <summary>What a walk reports beside its result.</summary>
internal sealed class TomlStructureOptions
{
    /// <summary>The walk stops once the document holds more nodes than this (<see cref="TomlStructureResult.PastBound"/>).</summary>
    public int MaxNodes { get; init; } = int.MaxValue;

    /// <summary>Each key-value, table header and array element, with the path it writes and its 1-based line: keys,
    /// and an array's element (of an inline array or an array of tables) as <see cref="TomlStructure.IndexMark"/> and
    /// its index from 1. The list is the walk's own and changes after the call.</summary>
    public Action<IReadOnlyList<string>, int>? Written { get; init; }

    /// <summary>Each integer as it is written (sign, prefix and underscores), with its 1-based line.</summary>
    public Action<string, int>? Integer { get; init; }
}

/// <summary>
/// The structure of a TOML text, read in one pass over Tomlyn's lexer, with no syntax tree: the tables, arrays and keys
/// it defines and where, under TOML 1.0's rules. A key or a table is defined once. A table that dotted keys or an inline
/// table define is closed to a header, and one that a header defines is closed to dotted keys; a header can still define
/// a table under either. An inline table or an array is closed once written, and an array of tables takes elements only
/// from <c>[[headers]]</c>. A header that names an array of tables goes into its last element.
/// <para>One rule is the engine's, not TOML's: a header reaches an array of tables only while the header before it is
/// inside that array. Tomlyn's model reads the headers as a stack of open tables, and a header that reaches an array
/// the stack has closed (<c>[[a]]</c>, <c>[b]</c>, <c>[[a]]</c>) makes the array afresh, so its earlier tables are lost
/// without a word, or <c>a</c> becomes a table (<c>[a.c]</c> there): Tomlyn 2.10.1 to 2.11.1, plan §12.6. Such a text is
/// refused, with the spelling that keeps it: an array's tables, with their sub-tables, one after another.</para>
/// <para>Tomlyn's validating parser makes the same check, but its keys share their storage with the path it is walking
/// (<c>ObjectPath.Clone</c> copies a list's array by reference), so two different keys whose hashes meet are taken for
/// one: false repeats at random, more often the more keys a table holds (data patches plan §12.6). Keys here are compared
/// by their text. The walk holds one small node for each value it has met, and keeps no syntax tree, whose nodes and
/// trivia cost about 120 bytes for each token: a file of 16 MB costs the walk the lexer and its nodes.</para>
/// <para>The walk checks structure, not syntax. A token where TOML has none stops it with
/// <see cref="TomlStructureResult.Unread"/>, and the reader's parser gives the syntax error; past that point it only
/// counts, from the tokens, a node for each value, <c>[</c> and <c>{</c>, so that the bound still holds.</para>
/// </summary>
internal sealed class TomlStructure
{
    /// <summary>The marker of an array's element in a written path.</summary>
    public const char IndexMark = '\u0001';

    /// <summary>How deep inline arrays and tables nest before the walk stops, as the readers' parsers do.</summary>
    private const int MaxNesting = 64;

    private enum Kind : byte { Table, TableArray, Array, Value }

    /// <summary>How a table came to be: the document's, a header's super-table (<c>a</c> of <c>[a.b]</c>), a header's,
    /// dotted keys', or an inline table.</summary>
    private enum Origin : byte { Root, Super, Header, Dotted, Inline }

    private sealed class Node(Kind kind, Origin origin, TomlTextPosition at)
    {
        public readonly Kind Kind = kind;
        public Origin Origin = origin;
        /// <summary>Where it is defined: a key-value's key, a header's <c>[</c>, or an array's element.</summary>
        public TomlTextPosition At = at;
        public Dictionary<string, Node>? Children = kind == Kind.Table ? new Dictionary<string, Node>(StringComparer.Ordinal) : null;
        public List<Node>? Elements = kind == Kind.TableArray ? [] : null;
    }

    private readonly TomlLexer _lexer;
    private readonly TomlStructureOptions _options;
    private readonly Node _root = new(Kind.Table, Origin.Root, default);
    private readonly List<string> _path = [];
    private readonly List<TomlStructureProblem> _problems = [];
    /// <summary>The arrays of tables the last header's table is in, outermost first: those a header may reach.</summary>
    private readonly List<Node> _openArrays = [];
    private TomlToken _token;
    private int _nodes;
    private bool _pastBound;
    private TomlTextPosition? _unread;

    private bool Stopped => _pastBound || _unread != null;

    private TomlStructure(string text, TomlStructureOptions options)
    {
        _lexer = TomlLexer.Create(text, new TomlLexerOptions { DecodeScalars = true });
        _options = options;
    }

    public static TomlStructureResult Walk(string text, TomlStructureOptions? options = null)
    {
        var walk = new TomlStructure(text, options ?? new TomlStructureOptions());
        walk.Document();
        if (walk._unread != null && !walk._pastBound) walk.CountTheRest();
        return new TomlStructureResult(walk._problems, walk._nodes, walk._pastBound, walk._unread);
    }

    private void Document()
    {
        Node table = _root;
        Advance(TomlLexerMode.Key);
        while (!Stopped)
        {
            while (_token.Kind == TokenKind.NewLine) Advance(TomlLexerMode.Key);
            switch (_token.Kind)
            {
                case TokenKind.Eof:
                    return;
                case TokenKind.BasicKey or TokenKind.String or TokenKind.StringLiteral:
                    KeyValue(table);
                    break;
                case TokenKind.OpenBracket or TokenKind.OpenBracketDouble:
                    table = Header(_token.Kind == TokenKind.OpenBracketDouble);
                    break;
                default:
                    Unexpected();
                    return;
            }
            if (Stopped) return;
            // A key-value or a header ends its line.
            Advance(TomlLexerMode.Key);
            if (_token.Kind is not (TokenKind.NewLine or TokenKind.Eof)) Unexpected();
        }
    }

    /// <summary>The next token but whitespace and comments, lexed in <paramref name="mode"/>; inside an array or an
    /// inline table newlines are passed over too.</summary>
    private void Advance(TomlLexerMode mode, bool newLines = false)
    {
        _lexer.Mode = mode;
        while (true)
        {
            if (!_lexer.MoveNext())
            {
                _token = new TomlToken(TokenKind.Eof, _token.End, _token.End);
                return;
            }
            _token = _lexer.Current;
            if (_token.Kind is TokenKind.Whitespaces or TokenKind.Comment) continue;
            if (newLines && _token.Kind == TokenKind.NewLine) continue;
            return;
        }
    }

    private void Unexpected() => _unread ??= _token.Start;

    /// <summary>Counts one node; past the bound, stops the walk.</summary>
    private void Count()
    {
        if (++_nodes > _options.MaxNodes) _pastBound = true;
    }

    private void Written(int line) => _options.Written?.Invoke(_path, line + 1);

    /// <summary>A key's parts, the first at the current token; the walk ends at the token after the key.</summary>
    private List<string>? Key(TomlLexerMode mode)
    {
        var keys = new List<string>();
        while (true)
        {
            string? key = _token.Kind switch
            {
                TokenKind.BasicKey => _lexer.GetText(_token),
                TokenKind.String or TokenKind.StringLiteral => _token.StringValue,
                _ => null,
            };
            if (key == null)
            {
                Unexpected();
                return null;
            }
            keys.Add(key);
            // Each part but the last names a table, which counts: a key of more parts is past the bound already.
            if (keys.Count - 1 > _options.MaxNodes)
            {
                _pastBound = true;
                return null;
            }
            Advance(mode);
            if (_token.Kind != TokenKind.Dot) return keys;
            Advance(mode);
        }
    }

    /// <summary>A key-value in <paramref name="table"/>, at its key; the walk ends at the value's last token.</summary>
    private void KeyValue(Node table, int nesting = 0)
    {
        TomlTextPosition at = _token.Start;
        List<string>? keys = Key(TomlLexerMode.Key);
        if (keys == null) return;
        if (_token.Kind != TokenKind.Equal)
        {
            Unexpected();
            return;
        }
        int depth = _path.Count;
        _path.AddRange(keys);
        Written(at.Line);
        // The keys before the last name tables: made by dotted keys, or passed through when dotted keys or a header's
        // super-table made them (TOML 1.0: dotted keys define a table "provided that such tables were not previously
        // created", so a header may still define a super-table they pass through, as Tomlyn has it); anything else
        // was defined already.
        Node? target = table;
        for (int k = 0; k < keys.Count - 1 && target != null; k++)
        {
            Node? child = target.Children!.GetValueOrDefault(keys[k]);
            if (child == null)
            {
                child = new Node(Kind.Table, Origin.Dotted, at);
                target.Children!.Add(keys[k], child);
                Count();
            }
            else if (child.Kind != Kind.Table || child.Origin is not (Origin.Dotted or Origin.Super))
            {
                Redefined(at, child, depth + k + 1);
                child = null;
            }
            target = child;
        }
        string last = keys[^1];
        Node? existing = target?.Children!.GetValueOrDefault(last);
        if (existing != null)
        {
            Redefined(at, existing, _path.Count);
            target = null;
        }
        Advance(TomlLexerMode.Value);
        Node? value = Value(nesting);
        if (value != null && target != null)
        {
            value.At = at;
            target.Children!.Add(last, value);
        }
        _path.RemoveRange(depth, _path.Count - depth);
    }

    /// <summary>A value, at its first token (lexed as a value); the walk ends at its last token. Null when the walk
    /// stopped.</summary>
    private Node? Value(int nesting)
    {
        TomlToken token = _token;
        switch (token.Kind)
        {
            case TokenKind.Integer or TokenKind.IntegerHexa or TokenKind.IntegerOctal or TokenKind.IntegerBinary:
                if (_options.Integer is { } integer) integer(_lexer.GetText(token) ?? "", token.Start.Line + 1);
                Count();
                return new Node(Kind.Value, Origin.Root, token.Start);
            case TokenKind.Float or TokenKind.Infinite or TokenKind.PositiveInfinite or TokenKind.NegativeInfinite
                or TokenKind.Nan or TokenKind.PositiveNan or TokenKind.NegativeNan
                or TokenKind.True or TokenKind.False
                or TokenKind.String or TokenKind.StringMulti or TokenKind.StringLiteral or TokenKind.StringLiteralMulti
                or TokenKind.OffsetDateTimeByZ or TokenKind.OffsetDateTimeByNumber or TokenKind.LocalDateTime
                or TokenKind.LocalDate or TokenKind.LocalTime:
                Count();
                return new Node(Kind.Value, Origin.Root, token.Start);
            case TokenKind.OpenBracket:
                return Array(nesting + 1);
            case TokenKind.OpenBrace:
                return InlineTable(nesting + 1);
            default:
                Unexpected();
                return null;
        }
    }

    /// <summary>An inline array, at its <c>[</c>; the walk ends at its <c>]</c>. Its elements are closed, so only their
    /// own tables are kept while they are read.</summary>
    private Node? Array(int nesting)
    {
        if (nesting > MaxNesting)
        {
            Unexpected();
            return null;
        }
        var array = new Node(Kind.Array, Origin.Inline, _token.Start);
        Count();
        int index = 0;
        Advance(TomlLexerMode.Value, newLines: true);
        while (!Stopped)
        {
            if (_token.Kind == TokenKind.CloseBracket) return array;
            _path.Add(IndexMark + (++index).ToString(CultureInfo.InvariantCulture));
            Written(_token.Start.Line);
            Value(nesting);
            _path.RemoveAt(_path.Count - 1);
            if (Stopped) return null;
            Advance(TomlLexerMode.Value, newLines: true);
            if (_token.Kind == TokenKind.Comma) Advance(TomlLexerMode.Value, newLines: true);
            else if (_token.Kind != TokenKind.CloseBracket) Unexpected();
        }
        return null;
    }

    /// <summary>An inline table, at its <c>{</c>; the walk ends at its <c>}</c>. Newlines between its entries and a comma
    /// after the last are taken, as Tomlyn's parsers take them (TOML 1.1).</summary>
    private Node? InlineTable(int nesting)
    {
        if (nesting > MaxNesting)
        {
            Unexpected();
            return null;
        }
        var table = new Node(Kind.Table, Origin.Inline, _token.Start);
        Count();
        Advance(TomlLexerMode.Key, newLines: true);
        while (!Stopped)
        {
            if (_token.Kind == TokenKind.CloseBrace)
            {
                table.Children = null;   // closed: nothing reaches into it again
                return table;
            }
            KeyValue(table, nesting);
            if (Stopped) return null;
            Advance(TomlLexerMode.Key, newLines: true);
            if (_token.Kind == TokenKind.Comma) Advance(TomlLexerMode.Key, newLines: true);
            else if (_token.Kind != TokenKind.CloseBrace) Unexpected();
        }
        return null;
    }

    /// <summary>A header, at its <c>[</c> or <c>[[</c>; the walk ends at its <c>]</c> or <c>]]</c>. The table it names,
    /// or a table of no one's when it names one it may not.</summary>
    private Node Header(bool array)
    {
        TomlTextPosition at = _token.Start;
        Advance(TomlLexerMode.Key);
        List<string>? keys = Key(TomlLexerMode.Key);
        var detached = new Node(Kind.Table, Origin.Header, at);
        if (keys == null) return detached;
        if (_token.Kind != (array ? TokenKind.CloseBracketDouble : TokenKind.CloseBracket))
        {
            Unexpected();
            return detached;
        }
        _path.Clear();
        var arrays = new List<Node>();
        Node? table = _root;
        for (int k = 0; k < keys.Count - 1 && table != null; k++)
        {
            _path.Add(keys[k]);
            Node? child = table.Children!.GetValueOrDefault(keys[k]);
            if (child == null)
            {
                child = new Node(Kind.Table, Origin.Super, at);
                table.Children!.Add(keys[k], child);
                Count();
            }
            else if (child.Kind == Kind.TableArray)
            {
                if (!Open(child, at))
                    child = null;
                else
                {
                    arrays.Add(child);
                    _path.Add(IndexMark + child.Elements!.Count.ToString(CultureInfo.InvariantCulture));
                    child = child.Elements[^1];
                }
            }
            else if (child.Kind != Kind.Table || child.Origin == Origin.Inline)
            {
                Redefined(at, child, _path.Count);
                child = null;
            }
            table = child;
        }
        _path.Add(keys[^1]);
        Node? named = null;
        if (table != null)
        {
            Node? existing = table.Children!.GetValueOrDefault(keys[^1]);
            if (!array)
            {
                if (existing == null)
                {
                    named = new Node(Kind.Table, Origin.Header, at);
                    table.Children!.Add(keys[^1], named);
                    Count();
                }
                else if (existing.Kind == Kind.Table && existing.Origin == Origin.Super)
                {
                    existing.Origin = Origin.Header;
                    named = existing;
                }
                else Redefined(at, existing, _path.Count);
            }
            else
            {
                if (existing == null)
                {
                    existing = new Node(Kind.TableArray, Origin.Header, at);
                    table.Children!.Add(keys[^1], existing);
                    Count();
                }
                if (existing.Kind == Kind.TableArray && (existing.Elements!.Count == 0 || Open(existing, at)))
                {
                    named = new Node(Kind.Table, Origin.Header, at);
                    existing.Elements!.Add(named);
                    Count();
                    arrays.Add(existing);
                    _path.Add(IndexMark + existing.Elements.Count.ToString(CultureInfo.InvariantCulture));
                }
                else if (existing.Kind != Kind.TableArray) Redefined(at, existing, _path.Count);
            }
        }
        _openArrays.Clear();
        if (named == null) _path.Clear();
        else
        {
            _openArrays.AddRange(arrays);
            Written(at.Line);
        }
        return named ?? detached;
    }

    /// <summary>Whether the header at <paramref name="at"/> may reach <paramref name="array"/>: the last header's table is
    /// in it. Else the walk's path names the array, and the problem is reported.</summary>
    private bool Open(Node array, TomlTextPosition at)
    {
        if (_openArrays.Contains(array)) return true;
        _problems.Add(new TomlStructureProblem(at, $"The array of tables `{PathText(_path.Count)}` is interrupted by "
                                                   + "another table before this header; write an array's tables, with "
                                                   + "their sub-tables, one after another"));
        return false;
    }

    /// <summary>A definition over <paramref name="first"/>, at the first <paramref name="parts"/> of the walk's path, in
    /// the words of Tomlyn's own validator.</summary>
    private void Redefined(TomlTextPosition at, Node first, int parts)
        => _problems.Add(new TomlStructureProblem(at, $"The key `{PathText(parts)}` is already defined at {first.At} and "
                                                      + "cannot be redefined"));

    /// <summary>The first <paramref name="parts"/> of the walk's path, as a path prints: keys joined by dots, a key
    /// that is not bare quoted, an array's element by its index from 1.</summary>
    private string PathText(int parts)
    {
        var text = new StringBuilder();
        for (int k = 0; k < parts && k < _path.Count; k++)
        {
            string part = _path[k];
            if (part.Length > 0 && part[0] == IndexMark)
            {
                text.Append('[').Append(part, 1, part.Length - 1).Append(']');
                continue;
            }
            if (text.Length > 0) text.Append('.');
            text.Append(DataValue.KeyText(part));
        }
        return text.ToString();
    }

    /// <summary>Past a token the walk does not follow, a node for each value, <c>[</c> and <c>{</c> of the rest: no fewer
    /// than the document can hold, so that a text the walk cannot follow still meets the bound before a parser builds
    /// it.</summary>
    private void CountTheRest()
    {
        while (!_pastBound)
        {
            Advance(TomlLexerMode.Value, newLines: true);
            switch (_token.Kind)
            {
                case TokenKind.Eof:
                    return;
                case TokenKind.Integer or TokenKind.IntegerHexa or TokenKind.IntegerOctal or TokenKind.IntegerBinary
                    or TokenKind.Float or TokenKind.Infinite or TokenKind.PositiveInfinite or TokenKind.NegativeInfinite
                    or TokenKind.Nan or TokenKind.PositiveNan or TokenKind.NegativeNan or TokenKind.True or TokenKind.False
                    or TokenKind.String or TokenKind.StringMulti or TokenKind.StringLiteral or TokenKind.StringLiteralMulti
                    or TokenKind.OffsetDateTimeByZ or TokenKind.OffsetDateTimeByNumber or TokenKind.LocalDateTime
                    or TokenKind.LocalDate or TokenKind.LocalTime or TokenKind.OpenBracket or TokenKind.OpenBrace:
                    Count();
                    break;
            }
        }
    }
}
