using Ome.Engine.Model;

namespace Ome.Engine.Frontend.Shell;

/// <summary>
/// The boxes the port draws for AGE and AGERC, in the shell's language (localization spec §11): AGE's interpreter
/// error box and op 0x1b4's diagnostic box (<see cref="AgeBoxText"/>), and AGERC's name entry (command 10,
/// INPUTNAME). Their Japanese is the original's, and the driver reads them in Japanese whatever is shown.
/// </summary>
public static class ShellBoxes
{
    public const string NameEntryTitle = "name-entry.title";
    public const string NameEntryPrompt = "name-entry.prompt";
    public const string NameEntryTooLong = "name-entry.too-long";
    public const string NameEntryHalfWidth = "name-entry.half-width";
    public const string NameEntryAccept = "name-entry.accept";
    public const string NameEntryCancel = "name-entry.cancel";

    public static readonly string[] NameEntryKeys =
        [NameEntryTitle, NameEntryPrompt, NameEntryTooLong, NameEntryHalfWidth, NameEntryAccept, NameEntryCancel];

    /// <summary>An AGE box's caption and body as <paramref name="strings"/> presents them. A box without parts (one
    /// the VM did not build) keeps its text.</summary>
    public static (string Caption, string Text) Present(DiagnosticMessage message, ShellStrings strings)
        => message.Parts is { } parts
            ? (strings[AgeBoxText.Caption], AgeBoxText.Compose(parts, key => strings[key]))
            : (message.Caption, message.Text);

    /// <summary>The error box's buttons as <paramref name="strings"/> labels them: 中止, 再試行, 無視.</summary>
    public static IReadOnlyList<string> ErrorButtons(ShellStrings strings)
        => AgeBoxText.ButtonKeys.Select(key => strings[key]).ToList();
}
