using Ome.Engine.Frontend.NativeResources;
using Godot;

/// <summary>
/// AGERC's ﾊﾞｰｼﾞｮﾝ情報 (menu 0x9c45: host command 2, dialog resource 2, agerc_version_dialog_proc@0x10001e70;
/// docs/engine-re.md, "Native menu bar") as the port presents it: AGE's template with the engine half naming Open Maid
/// Engine and no プロダクトID row (<see cref="EngineIdentity.VersionDialogTemplate"/>). WM_INITDIALOG fills
/// <list type="bullet">
/// <item>0x400 with "Version " and this engine's version, where AGE shows its EXE's FileVersion (4.24J);</item>
/// <item>0x404 with AGERC's IDI_ICON1, the game's icon;</item>
/// <item>0x3ee with set:GameName (the SYS4INI catalog title);</item>
/// <item>0x3ef with "Version " and set:GameVersion;</item>
/// <item>0x407 with set:Copyright (SYS4INI COPYRIGHT).</item>
/// </list>
/// Its only button, OK, has id 2 (IDCANCEL), so OK, Enter, Escape and ✕ all close it; IDOK does nothing. OK with Ctrl
/// held opens the registration dialog 134 when set:IsReggist exists and the registration check fails; Himegari never
/// sets set:IsReggist, so the port always closes.
/// </summary>
public partial class AgercVersionDialog : Win32TemplateDialog
{
    public const int ResourceId = 2;
    private const int EngineVersionId = 0x400, IconId = 0x404, GameNameId = 0x3ee, GameVersionId = 0x3ef;
    private const int CopyrightId = 0x407;

    private readonly string _engineVersion;
    private readonly Texture2D? _icon;
    private readonly string _gameName;
    private readonly string _gameVersion;
    private readonly string _copyright;

    /// <param name="template">AGERC's dialog 2 as AGE has it.</param>
    /// <param name="engineVersion">This engine's version as <see cref="EngineIdentity.DisplayVersion"/> gives it.</param>
    public AgercVersionDialog(DialogTemplate template, string engineVersion, Texture2D? icon, string gameName,
                              string gameVersion, string copyright)
        : base(EngineIdentity.VersionDialogTemplate(template))
    {
        _engineVersion = engineVersion;
        _icon = icon;
        _gameName = gameName;
        _gameVersion = gameVersion;
        _copyright = copyright;
    }

    protected override void OnInitDialog()
    {
        SetItemText(EngineVersionId, "Version " + _engineVersion);
        if (_icon != null) SetItemIcon(IconId, _icon);
        SetItemText(GameNameId, _gameName);
        SetItemText(CopyrightId, _copyright);
        SetItemText(GameVersionId, "Version " + _gameVersion);
    }

    protected override void OnCommand(int id)
    {
        if (id == IdCancel) EndDialog(id);
    }
}
