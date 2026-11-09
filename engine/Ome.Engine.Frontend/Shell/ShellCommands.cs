using Ome.Engine.Vm;

namespace Ome.Engine.Frontend.Shell;

/// <summary>What a command is: an action, an on/off toggle, one choice of a radio pair, or the way into a panel
/// section.</summary>
public enum ShellCommandKind { Action, Toggle, Radio, Section }

/// <summary>
/// One command of the engine shell (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §5.1). The name is the
/// driver's <c>menu</c> name, and the id is native's menu-110 command id, which the driver also accepts. Where the
/// command shows comes from the menu tree and its <c>Menu_*</c> pruning (<see cref="ShellMenu"/>); its enabled state,
/// check mark and label come from the rules here, read against a <see cref="ShellState"/>. The rules follow
/// agerc_refresh_system_menu_state@0x10002e10 and the actions agerc_menu_command@0x100032f0 (docs/engine-re.md,
/// "Native menu bar").
/// </summary>
public sealed class ShellCommand
{
    public required string Name { get; init; }
    public required int Id { get; init; }
    public required ShellCommandKind Kind { get; init; }
    /// <summary>The string-table key of the label (for <c>hide</c>, while the message window shows).</summary>
    public required string LabelKey { get; init; }
    /// <summary>The string-table key of the accelerator text drawn after the label, or null.</summary>
    public string? AcceleratorKey { get; init; }
    /// <summary>In op 0x142's set: grayed while the script turns the system menu off (CONFIG runs).</summary>
    public bool SystemMenuGated { get; init; }
    /// <summary>The panel section a <see cref="ShellCommandKind.Section"/> command opens.</summary>
    public ShellSection? Section { get; init; }
    /// <summary>For the 情報 section: the diagnostics rather than the about view.</summary>
    public bool Diagnostics { get; init; }

    /// <summary>The command's own enable rule; op 0x142's gate applies on top.</summary>
    public Func<ShellState, bool> EnabledRule { get; init; } = _ => true;
    public Func<ShellState, bool>? CheckedRule { get; init; }
    /// <summary>A label key that depends on the state, or null for <see cref="LabelKey"/>.</summary>
    public Func<ShellState, string>? LabelRule { get; init; }
    public required Action<IShellHost, IVmMenuServices> Action { get; init; }

    public bool IsEnabled(ShellState state) =>
        (!SystemMenuGated || state.Vm.SystemMenuEnabled) && EnabledRule(state);

    public bool IsChecked(ShellState state) => CheckedRule?.Invoke(state) ?? false;

    public string LabelKeyFor(ShellState state) => LabelRule?.Invoke(state) ?? LabelKey;

    /// <summary>The item text as a menu draws it: the label, then a tab and the accelerator text when it has one.
    /// </summary>
    public string Text(ShellStrings strings, ShellState state) =>
        AcceleratorKey == null ? strings[LabelKeyFor(state)] : $"{strings[LabelKeyFor(state)]}\t{strings[AcceleratorKey]}";

    public void Run(IShellHost host, IVmMenuServices vm) => Action(host, vm);
}

/// <summary>The shell's command table (spec §5.1). ｾｰﾌﾞ/ﾛｰﾄﾞ (0x9c43/0x9c44) and the 設定 radio groups are not commands:
/// both games prune them, and their native handlers are not decoded (<see cref="ShellMenu"/> draws them grayed when a
/// profile keeps them).</summary>
public static class ShellCommands
{
    public const int Exit = 0x9c41, VersionInfo = 0x9c45, SaveScreen = 0x9c46, ImageFolder = 0x9c47;
    public const int FullScreen = 0x9c5a, Windowed = 0x9c5b;
    public const int MessageSkip = 0x9c63, HideWindow = 0x9c64, AutoMessage = 0x9c65;
    public const int SoundSettings = 0x9c68, MessageSettings = 0x9c6c, SystemInfo = 0x9c6d, OtherSettings = 0x9c6e;
    public const int ReadMessageSkip = 0x9c6f;

    public static IReadOnlyList<ShellCommand> All { get; } =
    [
        new()
        {
            Name = "exit", Id = Exit, Kind = ShellCommandKind.Action, LabelKey = "command.exit",
            AcceleratorKey = "command.exit.accelerator",
            Action = (host, _) => host.RequestClose(),   // age_main_window_proc: PostMessage(WM_CLOSE, 0)
        },
        new()
        {
            Name = "save-screen", Id = SaveScreen, Kind = ShellCommandKind.Action, LabelKey = "command.save-screen",
            Action = (host, _) => host.SaveScreenshot(),   // IAGEService +0xa4: engine_save_screenshot_bmp
        },
        new()
        {
            Name = "image-folder", Id = ImageFolder, Kind = ShellCommandKind.Action, LabelKey = "command.image-folder",
            Action = (host, _) => host.ChooseImageFolder(),   // agerc_choose_screenshot_folder: SHBrowseForFolder
        },
        new()
        {
            Name = "skip", Id = MessageSkip, Kind = ShellCommandKind.Toggle, LabelKey = "command.skip",
            SystemMenuGated = true, CheckedRule = s => s.Vm.MessageSkip,   // +0x04: the persistent Skip
            Action = (_, vm) => vm.RequestMenuMessageSkipToggle(),   // +0x88: adv_toggle_skip_mode
        },
        new()
        {
            Name = "read-skip", Id = ReadMessageSkip, Kind = ShellCommandKind.Toggle, LabelKey = "command.read-skip",
            SystemMenuGated = true, CheckedRule = s => s.Vm.ReadTextSkip,   // message:ReadTextSkip
            Action = (_, vm) => vm.RequestMenuReadMessageSkipToggle(),   // +0xd8: adv_toggle_read_skip_and_refresh
        },
        new()
        {
            // While the hidden-window wait runs (+0xb8) it reads 表示する and stays enabled; otherwise it needs a polling
            // text wait (+0xbc) whose frame has an op-0x7b handler A (+0x80). The op-0x95 choice wait (+0xc0) would
            // also count, but neither game uses op 0x95, so the VM has no such state (spec §4.1).
            Name = "hide", Id = HideWindow, Kind = ShellCommandKind.Action, LabelKey = "command.hide",
            SystemMenuGated = true,
            EnabledRule = s => s.Vm.HiddenWindow || (s.Vm.TextWait && s.Vm.YieldHandler),
            LabelRule = s => s.Vm.HiddenWindow ? "command.hide.hidden" : "command.hide",
            Action = (_, vm) => vm.RequestMenuHideWindow(),   // +0x8c: iage_service_request_hide_window
        },
        new()
        {
            Name = "auto", Id = AutoMessage, Kind = ShellCommandKind.Toggle, LabelKey = "command.auto",
            SystemMenuGated = true, CheckedRule = s => s.Vm.AutoMessage,   // +0x0c
            Action = (_, vm) => vm.RequestMenuAutoMessageToggle(),   // +0x84: adv_toggle_auto_mode
        },
        new()
        {
            Name = "message-settings", Id = MessageSettings, Kind = ShellCommandKind.Section,
            LabelKey = "command.message-settings", SystemMenuGated = true, Section = ShellSection.Message,
            Action = (host, _) => host.OpenPanel(ShellSection.Message),
        },
        new()
        {
            Name = "fullscreen", Id = FullScreen, Kind = ShellCommandKind.Radio, LabelKey = "command.fullscreen",
            CheckedRule = s => !s.Windowed,
            Action = (host, _) => host.SwitchScreenMode(windowed: false, "menu ﾌﾙｽｸﾘｰﾝ"),   // IAGEService +0x1c(0)
        },
        new()
        {
            Name = "windowed", Id = Windowed, Kind = ShellCommandKind.Radio, LabelKey = "command.windowed",
            CheckedRule = s => s.Windowed,
            Action = (host, _) => host.SwitchScreenMode(windowed: true, "menu ｳｲﾝﾄﾞｳ"),   // IAGEService +0x1c(1)
        },
        new()
        {
            Name = "sound-settings", Id = SoundSettings, Kind = ShellCommandKind.Section,
            LabelKey = "command.sound-settings", SystemMenuGated = true, Section = ShellSection.Sound,
            Action = (host, _) => host.OpenPanel(ShellSection.Sound),
        },
        new()
        {
            Name = "other-settings", Id = OtherSettings, Kind = ShellCommandKind.Section,
            LabelKey = "command.other-settings", SystemMenuGated = true, Section = ShellSection.Other,
            Action = (host, _) => host.OpenPanel(ShellSection.Other),
        },
        new()
        {
            Name = "system-info", Id = SystemInfo, Kind = ShellCommandKind.Section, LabelKey = "command.system-info",
            Section = ShellSection.Info, Diagnostics = true,
            Action = (host, _) => host.OpenPanel(ShellSection.Info, diagnostics: true),
        },
        new()
        {
            Name = "version", Id = VersionInfo, Kind = ShellCommandKind.Section, LabelKey = "command.version",
            Section = ShellSection.Info,
            Action = (host, _) => host.OpenPanel(ShellSection.Info),
        },
    ];

    private static readonly Dictionary<int, ShellCommand> ById = All.ToDictionary(c => c.Id);
    private static readonly Dictionary<string, ShellCommand> ByName =
        All.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

    public static ShellCommand? Find(int id) => ById.GetValueOrDefault(id);

    public static ShellCommand? Find(string name) => ByName.GetValueOrDefault(name);
}
