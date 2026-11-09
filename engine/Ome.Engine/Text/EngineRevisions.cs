namespace Ome.Engine.Text;

/// <summary>The engine ABIs whose text code differs (see each rule's callers).</summary>
internal static class EngineRevisions
{
    /// <summary>Kamidori's engine; every other profile runs SYS4422's text code.</summary>
    public static bool IsSys4433(string engineAbiId)
        => string.Equals(engineAbiId, "SYS4433", StringComparison.Ordinal);
}
