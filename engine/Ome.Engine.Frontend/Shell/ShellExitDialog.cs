namespace Ome.Engine.Frontend.Shell;

/// <summary>
/// The 終了 dialog's content (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §7.3): what AGE's dialog 3
/// asks when an unforced close arrives (wm_close_confirm_exit_or_title@0x405740; docs/engine-re.md, "Close policy"),
/// in the shell's own words. Quit answers 1, タイトルに戻る 2 and ｷｬﾝｾﾙ 0; the frontend draws it.
/// </summary>
public sealed record ShellExitDialog(string Title, string Prompt, string Quit, string ReturnToTitle, string Ok,
                                     string Cancel)
{
    /// <summary>The text native's SS_ICON static holds, its icon's resource name, which the driver's <c>dialogs</c>
    /// step reads before the prompt.</summary>
    public const string NativeIconText = "SYSTEM3";

    public static ShellExitDialog For(ShellStrings strings) => new(
        strings["exit.title"], strings["exit.prompt"], strings["exit.quit"], strings["exit.return-to-title"],
        strings["dialog.ok"], strings["dialog.cancel"]);

    /// <summary>The Japanese dialog, as the driver reports it whatever the player's language (spec §8).</summary>
    public static ShellExitDialog Japanese { get; } = For(ShellStrings.Japanese);

    /// <summary>What the driver's <c>dialogs</c> step reads from the native dialog: its statics' texts joined by a
    /// newline (the icon's resource name, then the prompt).</summary>
    public string DriveText => $"{NativeIconText}\n{Prompt}";

    /// <summary>The buttons' texts in native's creation order: the two radios, OK and ｷｬﾝｾﾙ.</summary>
    public IReadOnlyList<string> Buttons => [Quit, ReturnToTitle, Ok, Cancel];
}
