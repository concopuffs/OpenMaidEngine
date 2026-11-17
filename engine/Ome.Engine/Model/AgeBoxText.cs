using System.Globalization;
using System.Text;

namespace Ome.Engine.Model;

/// <summary>
/// The text of the boxes AGE itself shows (localization spec §11): the interpreter error boxes (中止/再試行/無視,
/// age_show_message_box@0x409370 mode 7; はい/いいえ, mode 5) and the diagnostic box of op 0x1b4. Each string has a
/// key, as the shell's tables have. <see cref="Japanese"/> is AGE's own wording; the VM composes every box with it, so
/// the driver, the timeline and a halt reason read what native shows. The shell's tables (Ome.Engine.Frontend's
/// ShellStrings) take these keys into their Japanese table and translate them, and a host presents a box in its
/// language with <see cref="Compose"/> over the box's <see cref="AgeBoxParts"/>.
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
    public const string PromptYes = "error.prompt.yes";
    public const string PromptNo = "error.prompt.no";
    public const string Abort = "error.abort";
    public const string Retry = "error.retry";
    public const string Ignore = "error.ignore";
    public const string Yes = "error.yes";
    public const string No = "error.no";
    /// <summary>A callback registered by one script dispatched while another runs: <c>{0}</c> is the running
    /// script, <c>{1}</c> the owner.</summary>
    public const string DepthMismatch = "error.depth";
    /// <summary>A command refused its argument: <c>{0}</c> is the command's name in AGE's script language
    /// (SetKeyTotal for op 0xfe).</summary>
    public const string BadArgument = "error.argument";
    /// <summary>Op 0x60's bound is 0.</summary>
    public const string RandomBoundZero = "error.random-zero";
    /// <summary>Op 0x8 names a frame slot no script was loaded into: <c>{0}</c> is the slot.</summary>
    public const string EmptyScriptSlot = "error.script-slot-empty";
    /// <summary>Op 0xb4 could not decode a sound effect: <c>{0}</c> is its file.</summary>
    public const string WaveLoadFailed = "error.wave-load";
    /// <summary>An index outside its table: <c>{0}</c> is the op, <c>{1}</c> the argument's number, <c>{2}</c> its
    /// value and <c>{3}</c> the table's range.</summary>
    public const string IndexOutOfRange = "error.index-range";
    /// <summary>A negative index where there is no upper bound (a numbered save's slot): <c>{0}</c> is the op,
    /// <c>{1}</c> the argument's number and <c>{2}</c> its value.</summary>
    public const string IndexNegative = "error.index-negative";

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
        [PromptYes] = "はい：このままプログラムを続行します．\n      (ただし正常に動作しない場合があります)",
        [PromptNo] = "いいえ：プログラムを終了します．",
        [Abort] = "中止(&A)",
        [Retry] = "再試行(&R)",
        [Ignore] = "無視(&I)",
        [Yes] = "はい(&Y)",
        [No] = "いいえ(&N)",
        [DepthMismatch] = "Depth が不正です {0} != {1}",
        [BadArgument] = "{0}の引数が不正です．",
        [RandomBoundZero] = "RANDOMの最大値が0になっています．",
        [EmptyScriptSlot] = "この階層にはファイルが読み込まれていません．Depth={0}",
        [WaveLoadFailed] = "WAVファイル {0} の読み込みに失敗しました",
        [IndexOutOfRange] = "{0}の第{1}引数 {2} が範囲 {3} の外です．",
        [IndexNegative] = "{0}の第{1}引数 {2} が負の値です．",
    };

    /// <summary>A box's buttons' keys, in its order: 中止, 再試行, 無視 (mode 7); はい, いいえ (mode 5); none for the
    /// diagnostic box, whose only button is the system's OK.</summary>
    public static IReadOnlyList<string> ButtonKeys(AgeBoxButtons buttons) => buttons switch
    {
        AgeBoxButtons.AbortRetryIgnore => [Abort, Retry, Ignore],
        AgeBoxButtons.YesNo => [Yes, No],
        _ => [],
    };

    /// <summary>What each of a box's buttons does, in <see cref="ButtonKeys"/>' order: native's box returns 1 (exit),
    /// 2 (retry) or 4 (ignore), and the main loop's policy at 0x412689 acts on that. Mode 5 maps IDYES to 4 and IDNO to
    /// 1 (age_show_message_box at 0x4095b7; driven 2026-11-16).</summary>
    public static IReadOnlyList<ScriptErrorChoice> ButtonChoices(AgeBoxButtons buttons) => buttons switch
    {
        AgeBoxButtons.AbortRetryIgnore => [ScriptErrorChoice.Abort, ScriptErrorChoice.Retry, ScriptErrorChoice.Ignore],
        AgeBoxButtons.YesNo => [ScriptErrorChoice.Ignore, ScriptErrorChoice.Abort],
        _ => [],
    };

    /// <summary>A key's Japanese text.</summary>
    public static string JapaneseText(string key) => Japanese.TryGetValue(key, out string? text) ? text : key;

    /// <summary>
    /// A box's body as native lays it out (the "%s%s" body of age_show_message_box): the message, a blank line, the
    /// debug block and, for an error box, a blank line and the prompt that names its buttons. <paramref name="text"/>
    /// gives each key's text in the presenting language.
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
        IReadOnlyList<string> lines = parts.Buttons switch
        {
            AgeBoxButtons.AbortRetryIgnore => [PromptAbort, PromptRetry, PromptIgnore],
            AgeBoxButtons.YesNo => [PromptYes, PromptNo],
            _ => [],
        };
        if (lines.Count > 0)
        {
            body.Append("\n\n").Append(text(Prompt)).Append("\n\n");
            foreach (string line in lines) body.Append(text(line)).Append('\n');
        }
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
/// <see cref="Buttons"/> says which box it is: an interpreter error box asks how to go on.</summary>
public sealed record AgeBoxParts(AgeBoxMessage Message, AgeBoxLocation Location, AgeBoxButtons Buttons);

/// <summary>Which AGE box: the mode of age_show_message_box@0x409370, after the flags a thrown error carries.</summary>
public enum AgeBoxButtons
{
    /// <summary>Op 0x1b4's diagnostic box (mode 4, MB_OK | MB_ICONINFORMATION): no prompt.</summary>
    Ok,
    /// <summary>The interpreter error box for flags 0x10007 (mode 7, style 0x112): 中止, 再試行, 無視.</summary>
    AbortRetryIgnore,
    /// <summary>The error box for flags 0x10005 (mode 5, style 0x14, MB_YESNO | MB_ICONHAND): はい continues past the
    /// command, いいえ ends the program.</summary>
    YesNo,
}
