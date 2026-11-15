using Tomlyn;
using Tomlyn.Model;

namespace Ome.Mods.Manifest;

/// <summary>One <c>[[event.field]]</c> as written (Lua scripting spec §9.2). Its type, access and location are
/// checked at link, where the declaring mod's layout resolves the names.</summary>
public sealed record ModEventFieldDeclaration(string Name, string Type, string Access, string? At);

/// <summary>One <c>[[event]]</c> of a file under <c>events/</c> (Lua scripting spec §9.2): raised at a point, or by
/// its declaring mod's Lua (<c>raised_by = "lua"</c>).</summary>
public sealed record ModEventDeclaration(string Name, string? Point, string? Description,
                                         IReadOnlyList<ModEventFieldDeclaration> Fields)
{
    public bool RaisedByLua => Point == null;
}

public static partial class ManifestReader
{
    /// <summary>The access of a field that names none: read-only.</summary>
    public const string DefaultFieldAccess = "r";

    /// <summary>
    /// Reads one <c>events/*.toml</c> file (Lua scripting spec §9.2): one or more <c>[[event]]</c> tables, each with a
    /// dotted name, exactly one of <c>point</c> and <c>raised_by = "lua"</c>, an optional description, and its
    /// <c>[[event.field]]</c> entries. Unknown keys are errors, as in the manifest.
    /// </summary>
    public static IReadOnlyList<ModEventDeclaration> ParseEvents(string text, string origin)
    {
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException error)
        {
            throw new ModManifestException(origin, [$"not valid TOML: {error.Message}"]);
        }
        var reader = new TableReader(root, "", []);
        var events = new List<ModEventDeclaration>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (TableReader entry in reader.TableArray("event"))
        {
            string where = entry.Path.TrimEnd('.');
            string name = entry.String("name", required: true) ?? "";
            if (name.Length > 0 && !DottedNamePattern().IsMatch(name))
                entry.Problems.Add($"{entry.Path}name: '{name}' is not a dotted name");
            else if (name.Length > 0 && !names.Add(name))
                entry.Problems.Add($"{entry.Path}name: the file declares {name} twice");
            string? point = entry.String("point"), raisedBy = entry.String("raised_by");
            string? description = entry.String("description");
            if (point != null && raisedBy != null)
                entry.Problems.Add($"{where}: an event is raised at its point or by Lua (raised_by = \"lua\"), not both");
            else if (point == null && raisedBy == null)
                entry.Problems.Add($"{where}: an event names its point, or raised_by = \"lua\" for one its mod raises");
            if (raisedBy != null && raisedBy != "lua")
                entry.Problems.Add($"{entry.Path}raised_by: '{raisedBy}' is not \"lua\"");
            if (point != null && !DottedNamePattern().IsMatch(point))
                entry.Problems.Add($"{entry.Path}point: '{point}' is not a dotted name");

            var fields = new List<ModEventFieldDeclaration>();
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (TableReader field in entry.TableArray("field"))
            {
                string fieldName = field.String("name", required: true) ?? "";
                if (fieldName.Length > 0 && !fieldNames.Add(fieldName))
                    field.Problems.Add($"{field.Path}name: the event has two fields named {fieldName}");
                string type = field.String("type", required: true) ?? "";
                string access = field.String("access") ?? DefaultFieldAccess;
                string? at = field.String("at");
                field.RejectUnknown();
                fields.Add(new ModEventFieldDeclaration(fieldName, type, access, at));
            }
            entry.RejectUnknown();
            events.Add(new ModEventDeclaration(name, point, description, fields));
        }
        reader.RejectUnknown();
        if (reader.Problems.Count == 0 && events.Count == 0)
            reader.Problems.Add("the file declares no event ([[event]])");
        if (reader.Problems.Count > 0) throw new ModManifestException(origin, reader.Problems);
        return events;
    }
}
