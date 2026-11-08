using Age.Engine.Frontend.NativeResources;

namespace Age.Engine.Frontend.NativeMenu;

/// <summary>
/// AGERC's 終了 dialog (host command 3: DialogBoxParamA(3, agerc_exit_dialog_proc), opened by
/// wm_close_confirm_exit_or_title@0x405740; docs/engine-re.md "Close policy") as the game's own AGERC.DLL dialog 3
/// holds it. The controls are found by their ids. AGE never names their texts, so a translated AGERC.DLL changes what
/// they say: Himegari's is Japanese (終了, プログラムを終了する(&amp;E)…), and the Kamidori install here has an English
/// one (Quit, Exit(&amp;E)…) with the same layout.
/// </summary>
/// <param name="Icon">0x404, the SS_ICON static (resource name SYSTEM3).</param>
/// <param name="Prompt">The id-less left-aligned static: the question.</param>
/// <param name="Quit">Radio 0x3ea, checked at WM_INITDIALOG: the dialog answers 1.</param>
/// <param name="ReturnToTitle">Radio 0x3eb: the dialog answers 2.</param>
/// <param name="Ok">IDOK, the default push button: ends the dialog with the checked radio's answer.</param>
/// <param name="Cancel">IDCANCEL: the dialog answers 0.</param>
public sealed record AgercExitDialog(DialogTemplate Template, DialogItem Icon, DialogItem Prompt, DialogItem Quit,
                                     DialogItem ReturnToTitle, DialogItem Ok, DialogItem Cancel)
{
    public const int ResourceId = 3;
    public const int IconId = 0x404, QuitId = 0x3ea, ReturnToTitleId = 0x3eb, OkId = 1, CancelId = 2;

    public string Title => Template.Title;

    /// <summary>The template's controls, or null when one of them is missing.</summary>
    public static AgercExitDialog? From(DialogTemplate template)
    {
        DialogItem? prompt = template.Items.FirstOrDefault(i => i.Id == -1 && i.ClassName == "Static"
                                                                 && i.TypeBits == DialogStyles.SsLeft);
        return template.Item(IconId) is { } icon && prompt != null && template.Item(QuitId) is { } quit
               && template.Item(ReturnToTitleId) is { } title && template.Item(OkId) is { } ok
               && template.Item(CancelId) is { } cancel
            ? new AgercExitDialog(template, icon, prompt, quit, title, ok, cancel)
            : null;
    }

    /// <summary>Dialog 3 of <paramref name="agerc"/>, or null when it has none.</summary>
    public static AgercExitDialog? Load(ReadOnlySpan<byte> agerc) =>
        DialogTemplateResource.Load(agerc, ResourceId) is { } template ? From(template) : null;

    /// <summary>The texts the driver's <c>dialogs</c> step reads from the native dialog: its Static controls' non-empty
    /// texts joined by newlines (the icon's resource name, then the prompt).</summary>
    public string Text => string.Join("\n", Template.Items
        .Where(i => i.ClassName == "Static" && i.Text.Length > 0).Select(i => i.Text));

    /// <summary>Its Button controls' texts in creation order: the two radios, OK and Cancel.</summary>
    public IReadOnlyList<string> Buttons => Template.Items.Where(i => i.ClassName == "Button").Select(i => i.Text)
        .ToList();

    /// <summary>A control text's mnemonic: the character after its single '&amp;', upper-cased, or null.</summary>
    public static char? Mnemonic(string text)
    {
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != '&') continue;
            if (text[i + 1] != '&') return char.ToUpperInvariant(text[i + 1]);
            i++;   // "&&" is a literal ampersand
        }
        return null;
    }
}
