using System.Text;

namespace Ome.Engine.Frontend.Shell;

/// <summary>
/// 情報's about rows (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §5.5), which replace ﾊﾞｰｼﾞｮﾝ情報: the
/// engine's identity (<see cref="EngineIdentity"/>) and the game's name, version and copyright, filled as AGE fills them
/// (the catalog title, set:GameVersion by the VERREGPOS rule, set:Copyright). The product ID stays dropped.
/// </summary>
public sealed record ShellAbout(string EngineVersion, string GameName, string GameVersion, string Copyright);

/// <summary>
/// 情報's diagnostics, which replace 動作環境: what a bug report needs, as label-key and value rows (the
/// <c>diag.*</c> keys). Native's DirectX version, CPU clock and VRAM figures are not carried.
/// </summary>
public sealed record ShellDiagnostics(IReadOnlyList<(string Key, string Value)> Rows)
{
    /// <summary>The rows the clipboard button copies, one <c>label: value</c> line each. The labels are English
    /// whatever the panel shows, since a bug report goes to the engine's developers.</summary>
    public string ClipboardText()
    {
        ShellStrings english = ShellStrings.Load("en");
        var text = new StringBuilder();
        foreach ((string key, string value) in Rows) text.Append(english[key]).Append(": ").Append(value).Append('\n');
        return text.ToString();
    }
}
