namespace Ome.Engine.Text;

/// <summary>The engine ABIs whose code differs: text rules, the SYS4REG.INI bindings and op 0x215's object query
/// (see each caller).</summary>
internal static class EngineRevisions
{
    /// <summary>Kamidori's engine; every other profile runs SYS4422's text code.</summary>
    public static bool IsSys4433(string engineAbiId)
        => string.Equals(engineAbiId, "SYS4433", StringComparison.Ordinal);
}
