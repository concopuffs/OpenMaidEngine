using System.Globalization;
using System.Text;

namespace Ome.Engine.Model;

/// <summary>
/// The text of the boxes AGE itself shows (localization spec §11): the interpreter error box (中止/再試行/無視,
/// age_show_message_box@0x409370 mode 7) and the diagnostic box of op 0x1b4. Each string has a key, as the shell's
/// tables have. <see cref="Japanese"/> is AGE's own wording; the VM composes every box with it, so the driver, the
/// timeline and a halt reason read what native shows. The shell's tables (Ome.Engine.Frontend's ShellStrings) take
/// these keys into their Japanese table and translate them, and a host presents a box in its language with
/// <see cref="Compose"/> over the box's <see cref="AgeBoxParts"/>.
/// </summary>
public static class AgeBoxText
{
    public const string Caption = "error.caption";
    public const string DebugHeading = "error.debug";
    public const string DebugLocation = "error.debug.location";
    public const string Prompt = "error.prompt";
    public const string PromptAbort = "error.prompt.abort";
    public const string PromptRetry = "error.prompt.retry";
    public const string PromptIgnore = "error.prompt.ignore";
    public const string Abort = "error.abort";
    public const string Retry = "error.retry";
    public const string Ignore = "error.ignore";
    /// <summary>A callback registered by one script dispatched while another runs: <c>{0}</c> is the running
    /// script, <c>{1}</c> the owner.</summary>
    public const string DepthMismatch = "error.depth";

    /// <summary>AGE's wording, by key. <see cref="DebugLocation"/> takes the file, the address in hex, the source
    /// line, the command name, the opcode and the depth.</summary>
    public static IReadOnlyDictionary<string, string> Japanese { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Caption] = "エラーが発生しました",
        [DebugHeading] = "デバック情報：",
        [DebugLocation] = "FILE={0} ADDRESS={1} LINE={2} COMMAND={3}({4}) DEPTH={5}",
        [Prompt] = "プログラムを続行しますか？",
        [PromptAbort] = "中止：プログラムを終了します．",
        [PromptRetry] = "再試行：もう一度このコマンドを実行します．\n        (ただし正常に動作しない場合があります)",
        [PromptIgnore] = "無視：エラーを無視しプログラムを続行します．\n      (ただし正常に動作しない場合があります)",
        [Abort] = "中止(&A)",
        [Retry] = "再試行(&R)",
        [Ignore] = "無視(&I)",
        [DepthMismatch] = "Depth が不正です {0} != {1}",
    };

    /// <summary>The error box's buttons' keys, in its order: 中止, 再試行, 無視.</summary>
    public static IReadOnlyList<string> ButtonKeys { get; } = [Abort, Retry, Ignore];

    /// <summary>A key's Japanese text.</summary>
    public static string JapaneseText(string key) => Japanese.TryGetValue(key, out string? text) ? text : key;

    /// <summary>
    /// A box's body as native lays it out (the "%s%s" body of age_show_message_box): the message, a blank line, the
    /// debug block and, for the error box, a blank line and the prompt. <paramref name="text"/> gives each key's
    /// text in the presenting language.
    /// </summary>
    public static string Compose(AgeBoxParts parts, Func<string, string> text)
    {
        AgeBoxLocation at = parts.Location;
        var body = new StringBuilder(parts.Message.Text(text))
            .Append("\n\n").Append(text(DebugHeading)).Append('\n')
            .Append(Format(text(DebugLocation),
                [at.File, at.Address.ToString("X", CultureInfo.InvariantCulture), Number(at.Line), at.Command,
                 Number(at.Opcode), Number(at.Depth)]))
            .Append('\n');
        if (parts.Prompt)
            body.Append("\n\n").Append(text(Prompt)).Append("\n\n")
                .Append(text(PromptAbort)).Append('\n')
                .Append(text(PromptRetry)).Append('\n')
                .Append(text(PromptIgnore)).Append('\n');
        return body.ToString();
    }

    /// <summary><paramref name="template"/> with <c>{0}</c>, <c>{1}</c>, … replaced by <paramref name="args"/>; a
    /// template that does not format (a table checks its entries, so only a bad embedded one) comes back as it
    /// is.</summary>
    public static string Format(string template, IReadOnlyList<string> args)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args.Cast<object>().ToArray());
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The first part of an AGE box: AGE's own message by key with its arguments, or text the script built
/// (op 0x1b2's accumulator), which is shown as it is.</summary>
public sealed record AgeBoxMessage(string? Key, IReadOnlyList<string> Args, string Literal = "")
{
    public static AgeBoxMessage Keyed(string key, params string[] args) => new(key, args);

    public static AgeBoxMessage Script(string text) => new(null, [], text);

    /// <summary>The message in the language <paramref name="text"/> looks keys up in.</summary>
    public string Text(Func<string, string> text) => Key == null ? Literal : AgeBoxText.Format(text(Key), Args);
}

/// <summary>Where an AGE box was raised, as its debug block prints it: AGE's FILE, ADDRESS (a dword offset), LINE,
/// COMMAND(opcode) and DEPTH.</summary>
public readonly record struct AgeBoxLocation(string File, int Address, int Line, string Command, int Opcode, int Depth);

/// <summary>An AGE box's parts, from which <see cref="AgeBoxText.Compose"/> builds its body in any language.
/// <see cref="Prompt"/> is true for the interpreter error box, which asks how to go on.</summary>
public sealed record AgeBoxParts(AgeBoxMessage Message, AgeBoxLocation Location, bool Prompt);
