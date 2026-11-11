using System.Globalization;

namespace Ome.Engine.Frontend.Shell;

/// <summary>How a slider's value reads in the panel (spec §5.3: values display naturally).</summary>
public enum ShellValueFormat
{
    /// <summary>message:MessageSpeed, milliseconds per glyph: ﾉｰｳｴｲﾄ at 0.</summary>
    Milliseconds,
    /// <summary>message:MesWinAlpha 0..16, as the window's opacity: (16 − v) × 100 / 16 %, as 設定's edit reads.</summary>
    Opacity,
    /// <summary>Milliseconds as seconds with one decimal.</summary>
    Seconds,
    /// <summary>A value that is already a percentage.</summary>
    Percent,
    /// <summary>Basis points (0..10000) as a percentage.</summary>
    BasisPoints,
}

/// <summary>One row of a panel group.</summary>
public abstract record ShellPanelItem;

/// <summary>A native option's slider, labelled by the option, with the range's ends labelled as 設定's are. An inverted
/// slider runs from the domain's top on the left (ﾒｯｾｰｼﾞｽﾋﾟｰﾄﾞ's 遅い), and <paramref name="Switch"/> is the route
/// check box drawn with it (ｻｳﾝﾄﾞ's BGM, 効果音 and 音声).</summary>
public sealed record ShellPanelSlider(string Option, string LowKey, string HighKey, ShellValueFormat Format,
                                      bool Inverted = false, string? Switch = null) : ShellPanelItem;

/// <summary>A check box for a native switch option or a port switch (<see cref="ShellPanel.PortSwitches"/>).</summary>
public sealed record ShellPanelSwitch(string Option) : ShellPanelItem;

/// <summary>A port option chosen from a list: <c>language</c> or <c>window-size</c>.</summary>
public sealed record ShellPanelChoice(string Option) : ShellPanelItem;

/// <summary>The screen mode's radio pair: commands that apply at once, not draft options (spec §5.3).</summary>
public sealed record ShellPanelScreenMode : ShellPanelItem;

/// <summary>情報's about rows: the engine and the game.</summary>
public sealed record ShellPanelAbout : ShellPanelItem;

/// <summary>情報's diagnostics rows and their copy button.</summary>
public sealed record ShellPanelDiagnostics : ShellPanelItem;

/// <summary>A titled group of rows, as 設定's group boxes hold its controls.</summary>
public sealed record ShellPanelGroup(string TitleKey, IReadOnlyList<ShellPanelItem> Items);

/// <summary>One section of the panel.</summary>
public sealed record ShellPanelSection(ShellSection Section, IReadOnlyList<ShellPanelGroup> Groups)
{
    public string TitleKey => $"section.{Section.ToString().ToLowerInvariant()}";

    /// <summary>Whether op 0x142's gate makes the section read-only: the native options' sections.</summary>
    public bool ScriptShared => Section is ShellSection.Message or ShellSection.Sound or ShellSection.Other;
}

/// <summary>
/// The options panel's layout (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §7.1): its sections, in the
/// order the panel lists them, and each section's groups. The native sections keep 設定's pages' groups and their
/// wording (AGERC dialogs 100, 102 and 104); 画面, OME and 情報 are the port's own. The Godot panel draws this data.
/// </summary>
public static class ShellPanel
{
    /// <summary>The port options shown as check boxes.</summary>
    public static readonly string[] PortSwitches = ["fps-counter", "locator-hud"];

    /// <summary>The port options chosen from a list.</summary>
    public static readonly string[] PortChoices = ["language", "window-size"];

    /// <summary>The panel's own strings, beyond the sections, groups and options.</summary>
    public static readonly string[] TextKeys =
    [
        "panel.title", "panel.ok", "panel.cancel", "panel.read-only",
        "range.slow", "range.fast", "range.light", "range.dark", "range.short", "range.long", "range.quiet",
        "range.loud",
        "value.no-wait", "value.milliseconds", "value.seconds", "value.percent", "value.window-size",
        "value.window-size.canvas",
        "screen.fullscreen", "screen.windowed",
        "language.ja", "language.en",
        "about.version", "about.game-version",
        "diag.os", "diag.godot", "diag.renderer", "diag.gpu", "diag.audio", "diag.engine", "diag.profile",
        "diag.game-root", "diag.data-root", "diag.copy", "diag.copied",
    ];

    public static IReadOnlyList<ShellPanelSection> Sections { get; } =
    [
        new(ShellSection.Message,
        [
            new("option.message-speed",
                [new ShellPanelSlider("message-speed", "range.slow", "range.fast", ShellValueFormat.Milliseconds,
                                      Inverted: true)]),
            new("option.window-opacity",
                [new ShellPanelSlider("window-opacity", "range.light", "range.dark", ShellValueFormat.Opacity,
                                      Inverted: true)]),
            new("group.auto-wait",
            [
                new ShellPanelSlider("auto-wait-voice", "range.short", "range.long", ShellValueFormat.Seconds),
                new ShellPanelSlider("auto-wait-text", "range.short", "range.long", ShellValueFormat.Seconds),
            ]),
            new("group.message-options", [new ShellPanelSwitch("smooth-text"), new ShellPanelSwitch("wheel-advance")]),
        ]),
        new(ShellSection.Sound,
        [
            new("group.volume",
            [
                new ShellPanelSlider("master-volume", "range.quiet", "range.loud", ShellValueFormat.BasisPoints),
                new ShellPanelSlider("bgm-volume", "range.quiet", "range.loud", ShellValueFormat.BasisPoints,
                                     Switch: "bgm"),
                new ShellPanelSlider("se-volume", "range.quiet", "range.loud", ShellValueFormat.BasisPoints,
                                     Switch: "se"),
                new ShellPanelSlider("voice-volume", "range.quiet", "range.loud", ShellValueFormat.BasisPoints,
                                     Switch: "voice"),
            ]),
            new("group.voice-options",
            [
                new ShellPanelSwitch("lower-bgm-on-voice"),
                new ShellPanelSlider("lower-bgm-level", "range.quiet", "range.loud", ShellValueFormat.Percent),
            ]),
        ]),
        new(ShellSection.Other, [new("group.effects", [new ShellPanelSwitch("effect-skip")])]),
        new(ShellSection.Screen,
        [
            new("group.screen-mode", [new ShellPanelScreenMode()]),
            new("group.window-size", [new ShellPanelChoice("window-size")]),
        ]),
        new(ShellSection.Ome,
        [
            new("group.language", [new ShellPanelChoice("language")]),
            new("group.overlays", [new ShellPanelSwitch("fps-counter"), new ShellPanelSwitch("locator-hud")]),
        ]),
        new(ShellSection.Info,
        [
            new("group.about", [new ShellPanelAbout()]),
            new("group.diagnostics", [new ShellPanelDiagnostics()]),
        ]),
    ];

    public static ShellPanelSection Section(ShellSection section) => Sections.First(s => s.Section == section);

    /// <summary>The string-table key of a native or port option's label.</summary>
    public static string LabelKey(string option) => $"option.{option}";

    /// <summary>The slider position a value shows at: inverted sliders run from the domain's top.</summary>
    public static int Position(ShellOption option, ShellPanelSlider slider, int value) =>
        slider.Inverted ? option.Min + option.Max - value : value;

    /// <summary>The value a slider position stands for.</summary>
    public static int ValueAt(ShellOption option, ShellPanelSlider slider, int position) =>
        option.Snap(slider.Inverted ? option.Min + option.Max - position : position);

    /// <summary>The value's readout beside its slider.</summary>
    public static string Format(ShellStrings strings, ShellValueFormat format, int value) => format switch
    {
        ShellValueFormat.Milliseconds when value == 0 => strings["value.no-wait"],
        ShellValueFormat.Milliseconds => Fill(strings["value.milliseconds"], value.ToString(CultureInfo.InvariantCulture)),
        ShellValueFormat.Opacity => Fill(strings["value.percent"],
                                         ((16 - value) * 100 / 16).ToString(CultureInfo.InvariantCulture)),
        ShellValueFormat.Seconds => Fill(strings["value.seconds"],
                                         (value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)),
        ShellValueFormat.BasisPoints => Fill(strings["value.percent"],
                                             (value / 100).ToString(CultureInfo.InvariantCulture)),
        _ => Fill(strings["value.percent"], value.ToString(CultureInfo.InvariantCulture)),
    };

    /// <summary>The line beside the language setting (localization spec §10).</summary>
    public const string LanguageNoteKey = "option.language.note";

    /// <summary>
    /// The languages the OME section offers (localization spec §10): the shell's own, by their entries in
    /// <paramref name="strings"/>; then each language the mounted stack defines that the shell has no table for, by
    /// its <c>name</c> (its tag when it has none); then <paramref name="current"/> by its tag when neither has it
    /// (<c>x-pseudo</c>, or a language no package defines any more).
    /// </summary>
    public static IReadOnlyList<(string Tag, string Label)> LanguageChoices(
        ShellStrings strings, IReadOnlyList<(string Tag, string? Name)> stack, string current)
    {
        var choices = ShellStrings.Languages.Select(tag => (tag, strings[$"language.{tag}"])).ToList();
        foreach ((string tag, string? name) in stack)
            if (!choices.Any(choice => choice.Item1 == tag)) choices.Add((tag, string.IsNullOrWhiteSpace(name) ? tag : name));
        if (!choices.Any(choice => choice.Item1 == current)) choices.Add((current, current));
        return choices;
    }

    /// <summary>A window size's entry in the 画面 list; the canvas's own size says so.</summary>
    public static string WindowSizeText(ShellStrings strings, int width, int height, bool canvas)
    {
        string size = Fill(strings["value.window-size"], width.ToString(CultureInfo.InvariantCulture),
                           height.ToString(CultureInfo.InvariantCulture));
        return canvas ? Fill(strings["value.window-size.canvas"], size) : size;
    }

    /// <summary>The window sizes the 画面 list offers for a canvas: the canvas at 100 %, 125 %, 150 %, 175 % and 200 %,
    /// those that fit <paramref name="usable"/> (the screen's usable area less the frame), and the saved size when it is
    /// none of them. A size is the game's area; the window adds <paramref name="barHeight"/> rows above it at 100 %,
    /// scaled with the size, for the menu bar.</summary>
    public static IReadOnlyList<(int Width, int Height)> WindowSizes(int canvasWidth, int canvasHeight,
                                                                       (int Width, int Height) usable,
                                                                       (int Width, int Height)? saved = null,
                                                                       int barHeight = 0)
    {
        var sizes = new List<(int Width, int Height)>();
        foreach (int percent in new[] { 100, 125, 150, 175, 200 })
        {
            (int Width, int Height) size = (canvasWidth * percent / 100, canvasHeight * percent / 100);
            int windowHeight = size.Height + barHeight * percent / 100;
            if (percent == 100 || (size.Width <= usable.Width && windowHeight <= usable.Height)) sizes.Add(size);
        }
        if (saved is { } custom && !sizes.Contains(custom)) sizes.Add(custom);
        return sizes;
    }

    private static string Fill(string format, params string[] values)
    {
        string text = format;
        for (int i = 0; i < values.Length; i++) text = text.Replace("{" + i + "}", values[i]);
        return text;
    }
}
