# Engine shell: replacing AGERC — design

**Date:** 2026-11-09
**Status:** accepted 2026-11-09. §2 records the decisions the user made on 2026-11-08; §11 records the three
questions the user settled on 2026-11-09.
**Home in the canonical map:** this file is the design. Native findings stay in `docs/engine-re.md` ("Native menu
bar — AGERC menu 110", "Screen mode", the close policy under "Natural boot"). `SYS4REG.INI` and `SYS4INI.BIN` facts
stay in `docs/sys4-format-notes.md`. Tool usage stays in `docs/tools-reference.md`. Per-game facts stay in
`docs/modding/games/<profile>.md`.

## 1. Purpose

The port builds its front end from the game's `AGERC.DLL`, which it reads at runtime:

- menu 110, for the windowed menu bar and the fullscreen strip (`godot/Main.MenuBar.cs`, `Main.ScreenMode.cs`);
- dialogs 2 (ﾊﾞｰｼﾞｮﾝ情報), 3 (終了) and 9 (設定), with 設定's pages 100, 102, 104 and 105
  (`Main.AgercDialogs.cs`, `Main.Exit.cs`);
- the icon `IDI_ICON1`, for the version dialog and the strip's caption.

`PeResource`, `NativeMenuResource`, `DialogTemplate` and `IconResource` (`engine/Ome.Engine.Frontend/`) parse them,
and `Win32TemplateDialog` lays the dialogs out as `CreateDialogIndirect` would.

That works only for Himegari:

- **Kamidori, clean install.** Its `AGERC.DLL` is packed: the file holds only its icon, version and manifest
  resources. The port has no menu bar and no fullscreen strip, quits without the 終了 dialog, and opens neither
  設定 nor ﾊﾞｰｼﾞｮﾝ情報. Native AGE shows all of them (`docs/modding/games/kamidori.md`).
- **Kamidori, translated install.** Its `agerc.dll` keeps menu 110 as a MENUEX template, which
  `NativeMenuResource` rejects, so the port has no menu bar there either. Its dialogs load, in the translator's
  English.
- **Future games** ship other AGERC builds, packed or not. Engine code that reads them would depend on each.

Two things AGERC does not hold make the replacement possible:

- **No game logic.** AGERC reaches the engine only through two interfaces: `AGE:IAGEService`, the service table, and
  `AGE:reg`, the settings registry that `SYS4REG.INI` stores.
- **No game-specific content.** What varies between games comes from `SYS4INI.BIN` keys, which the port already
  reads.

The resources of the two AGERC builds we can read agree, except in their text:

- Both menu 110s have the same tree and command ids.
- Dialogs 2, 3, 9 and 10 and 設定's pages 100, 102, 104 and 105 have the same controls in both: the same ids,
  classes, styles and rectangles.

These are Himegari's `AGERC.DLL` and the translated install's `agerc.dll`, dumped 2026-11-09.

## 2. Decisions

The user made these on 2026-11-08 (backlog, "Replace AGERC with an engine-owned shell"):

1. **At runtime the port reads game data, never game binaries.** Game data means scripts, archives, `SYS4INI.BIN`,
   saves and settings. Game binaries means `AGE.EXE` and `AGERC.DLL`. What we learn from the binaries becomes our
   own code and data. The binaries stay research material and the native reference that the driver runs.
2. **AGERC is part of the engine, not the game.** The port replaces it as it replaces `AGE.EXE`.
3. **What scripts can observe keeps native semantics.** This covers:
   - the settings registry: keys, units, defaults and the `SYS4REG.INI` format;
   - the services, including the toggles' side effects;
   - the callback scripts;
   - the script-set gates.
4. **The option set is ours.** Only `SYS4INI.BIN` keys configure it (§5.4).
5. **Two presentations.** The options panel is the main UI. It is drawn inside the window and works fullscreen, on
   any OS, and with keyboard, mouse or controller. The menu bar is a desktop convenience: a classic bar when
   windowed, and the top-edge strip when fullscreen.
6. **The Japanese labels stay close to AGE's** unless that conflicts with the design (§6). The English labels are
   our own. The player's UI language picks between them, not the installed DLL.

These follow from them:

7. **Chrome parity becomes behaviour parity.** Driver checks compare the settings file, the VM position and which
   callbacks ran, not the pixels of the chrome.
8. **Port-only options get their own file.** `SYS4REG.INI` keeps only native keys, so native AGE can read the
   port's file and the other way round.

## 3. What AGERC does, and what replaces each part

| AGERC part | Native | Replacement |
|---|---|---|
| Menu 110 and its pruning | `LoadMenuA(110)`, then `_OperateMenu` op 1, `agerc_prune_menu_by_settings@0x10002ca0` | Shell menu tree (§5.2), pruned by the same `Menu_*` rules |
| Refresh on `WM_INITMENU` | op 0, `agerc_refresh_system_menu_state@0x10002e10` | Shell state refresh at the same moments (§4.2) |
| Command handler | op 2, `agerc_menu_command@0x100032f0` | Shell commands calling the engine services (§5.1) |
| Fullscreen strip (host cmd 4, cmd −1) | `agerc_system_menu_strip_dialog_proc@0x10003c20` | The same menu tree drawn as the strip (§7.2) |
| 終了 (host cmd 3, dialog 3) | `agerc_exit_dialog_proc@0x10002040`, result 1 / 2 / 0 | Shell dialog with the same choices and results (§7.3) |
| ﾊﾞｰｼﾞｮﾝ情報 (host cmd 2, dialog 2) | `agerc_version_dialog_proc@0x10001e70` | The panel's 情報 section (§5.5) |
| 設定 (host cmd 9, dialog 9, pages 100/102/104/105) | `agerc_settings_dialog_proc@0x10006400`, `agerc_settings_apply@0x10005c20` | The options panel (§5.3, §7.1) |
| 動作環境 (page 105) | OS, DirectX, CPU clock, memory, VRAM, drive space | An OME diagnostics section (§5.5) |
| 画像の保存場所 | `SHBrowseForFolderA` and its prompt | Unchanged (the OS picker); the prompt moves to the string table |
| Full-width editor (host cmd 10, dialog 10 名前の入力) | Op `0x144` | Unchanged: `FullwidthTextEditorDialog` is already built by hand, without the DLL |
| `IDI_ICON1` | Version dialog, strip caption | Our own OME icon (§5.5) |
| Not used by either game | Debug menu 124, dialogs 5, 6, 8, 134, the right-click popup, the radio groups, page 101 | Not carried (§10) |

## 4. Architecture

| Layer | Where | What it owns | Rule |
|---|---|---|---|
| Engine core | `Ome.Engine` | Settings registry, services, callback scripts, gates | Native semantics: what scripts observe |
| Shell model | `Ome.Engine.Frontend.Shell` (new) | Commands, menu tree, options, string tables, the state snapshot | Ours; headless and testable; no Godot types |
| Presentation | `godot/` | Options panel, menu bar, strip, 終了 dialog | Ours; draws the model and sends user actions back to it |

### 4.1 Engine core

Most of this layer exists. The changes are one gap and one interface.

- **Registry.** `Persistence/Sys4RegIniStore.cs` already holds the native keys, defaults, `SYS4INI.BIN` overrides and
  the canonical rewrite (`sys4-format-notes.md`).
  - **Gap: read-skip is not persisted.** `SharedProfile.ReadMessageSkipEnabled` holds `message:ReadTextSkip`, but
    nothing loads it from the file or writes it back, so the port forgets the toggle at every start. Native
    persists it.
  - **Write timing.** AGE writes the file only when it quits (`engine-re.md`, "設定"). The port writes most keys at
    once (the sound keys, `display:ScreenMode`, the 設定 dialog's other keys), which survives a crash. The `[message]`
    values the VM holds while it runs (MessageSpeed, MesWinAlpha, AutoMessageTime0/1 and ReadTextSkip) are written at
    quit, as AGE writes them. After the same actions both files end up identical.
- **Services.** These already exist:
  - the VM's menu services (`VirtualMachine.MenuServices.cs`): the Skip, Auto, read-skip and hide toggles, and
    `RequestMessageSettings`;
  - the screen-mode request (`VirtualMachine.ScreenMode.cs`);
  - the audio mixer settings, which apply live;
  - screenshots and the close policy, in the frontend.

  The shell reaches services that run on the frontend thread through a new interface, `IShellHost`. Godot's `Main`
  implements it with: switch the screen mode, save a screenshot, choose the image folder, quit, return to title, and
  hold or release the VM. VM services keep their existing queues, so a toggle still applies on the VM thread.
  - **No choice-wait gap.** The hide command's enable rule reads IAGEService `+0xc0`, run-state `0x10000000`, which
    only op `0x95` sets. Neither game uses op `0x95`, and the port stops at it as unsupported (`engine-re.md`), so
    the term is false on both targets and `VmMenuState` needs no field for it. (The survey listed this as a gap; M1
    withdrew it on 2026-11-09.)
- **Callback scripts.** No change:
  - `CALLBACK_SETTING.BIN` runs after each toggle and after the options panel applies (§5.3);
  - `CALLBACK_WINDOW.BIN` runs after a screen-mode switch;
  - `CALLBACK_LOAD.BIN` runs as before.

  `CALLBACK_LOST.BIN` is not the shell's concern: its native trigger is unknown.
- **Gates.** No change:
  - op `0x142` grays the native set {Skip, hide, Auto, ﾒｯｾｰｼﾞの設定, ｻｳﾝﾄﾞ設定, その他の設定, read-skip};
  - op `0x10f` bit 0 blocks F1 and bit `0x200` blocks F10;
  - op `0x149` sets the strip's dwell.

  §5.1 says how each gate maps onto the panel.

### 4.2 Shell model

`engine/Ome.Engine.Frontend/Shell/` replaces `NativeMenu/` and `NativeResources/`. The logic of `AgercMenu.Prune`
and `AgercMenu.Refresh` moves here under shell names, and the `Agerc*` names retire. The model holds:

- **Commands** (§5.1). Each command has a stable name, a native id, a label key, a kind (action, toggle,
  radio or panel section), a visibility rule, an enable rule, a check rule and an action.
- **The menu tree** (§5.2). It is data in our own table, in menu 110's order, with the `Menu_*` rules applied once
  at startup.
- **Options** (§5.3). Each option has a name, a registry key, a value domain, a display format, an apply time,
  a visibility rule and a section.
- **The state snapshot.** It combines `VmMenuState`, the registry, the screen mode and the gates. The model rebuilds
  it whenever a surface opens: the bar or strip opening a popup (native `WM_INITMENU`), the panel, the 終了 dialog.
  Every enabled and checked state comes from this snapshot.
- **Holding the VM.** While the bar, the panel or the 終了 dialog is open, the VM stops, as it does under native's
  modal loops. `SetFrontendModalHold` does this today for menus and AGERC dialogs, and keeps doing it.
- **String tables** (§6).

## 5. The option set

### 5.1 Commands

The names are the driver's existing `menu` names (`tools/drive_core.py`, `MENU_COMMANDS`), and the native ids stay
as the driver's numeric aliases.

| Name | Id | Label (ja) | Enabled | Checked | Action |
|---|---|---|---|---|---|
| `exit` | `0x9c41` | 終了(&X), with the accelerator text ESC | always | | Unforced close: 終了 dialog |
| `save-screen` | `0x9c46` | 表示画面を保存(&S) | always (op `0x10f` bit 0 blocks only the F1 key) | | Screenshot of the frozen frame |
| `image-folder` | `0x9c47` | 画像の保存場所(&P)... | always | | OS folder picker; writes `system:SaveBMPPath` |
| `skip` | `0x9c63` | ﾒｯｾｰｼﾞｽｷｯﾌﾟ(&S) | op `0x142` | persistent Skip | Toggle; `CALLBACK_SETTING` |
| `read-skip` | `0x9c6f` | 既読ﾒｯｾｰｼﾞ ｽｷｯﾌﾟ(&R) | op `0x142` | `message:ReadTextSkip` | Toggle; `CALLBACK_SETTING` |
| `hide` | `0x9c64` | ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳを消す(&H) / を表示する(&O) | op `0x142` and (hidden-window wait, or a text or choice wait with an op-`0x7b` handler) | | Hide request |
| `auto` | `0x9c65` | ｵｰﾄﾒｯｾｰｼﾞ(&A) | op `0x142` | Auto | Toggle; `CALLBACK_SETTING` |
| `message-settings` | `0x9c6c` | ﾒｯｾｰｼﾞの設定(&M)... | op `0x142` | | Panel at ﾒｯｾｰｼﾞ |
| `fullscreen` / `windowed` | `0x9c5a` / `0x9c5b` | ﾌﾙｽｸﾘｰﾝ(&F) / ｳｲﾝﾄﾞｳ(&W) | always | the current mode | Screen-mode switch (`ForceScreen` veto); `CALLBACK_WINDOW` |
| `sound-settings` | `0x9c68` | ｻｳﾝﾄﾞ設定(&S)... | op `0x142` | | Panel at ｻｳﾝﾄﾞ |
| `other-settings` | `0x9c6e` | その他の設定(&X)... | op `0x142` | | Panel at その他 |
| `system-info` | `0x9c6d` | このｺﾝﾋﾟｭｰﾀの動作環境(&S)... | always | | Panel at 情報, diagnostics |
| `version` | `0x9c45` | ﾊﾞｰｼﾞｮﾝ情報(&V)... | always | | Panel at 情報 |

**The op-`0x142` gate in the panel.** While the field is 0, the commands marked "op `0x142`" are grayed, as natively.
A panel that is already open, or one opened through `version` or `system-info`, shows ﾒｯｾｰｼﾞ, ｻｳﾝﾄﾞ and その他
read-only. The script-shared settings therefore cannot change while CONFIG runs. Native has the same property,
because its 設定 cannot be opened then.

**Left out, as today.** ｾｰﾌﾞ/ﾛｰﾄﾞ (`0x9c43`/`0x9c44`) and the six radio groups are not modelled: both games prune
them, and their handlers are not decoded. A profile that ships the matching `Menu_*` key at 1 reopens the gap.

### 5.2 Menu tree

The tree follows menu 110: ｹﾞｰﾑ, 画像の保存, ﾒｯｾｰｼﾞ, 設定 and a right-justified ﾍﾙﾌﾟ, in the order and with the
separators of `engine-re.md` ("Native menu bar"). The `Menu_*` rules carry over unchanged, including native's
double read of `Menu_MesWinA`; `Menu_MesSpeed` stays unread. Both games prune to the same live bar:

- ｹﾞｰﾑ, holding 終了;
- 画像の保存;
- ﾒｯｾｰｼﾞ;
- 設定, holding 画面表示, a separator, ｻｳﾝﾄﾞ設定 and その他の設定;
- ﾍﾙﾌﾟ.

`Menu_*` keys prune only the bar. Natively they never touch the 設定 dialog, so they never prune the panel.

### 5.3 Options

Every option has the domain and step of the native control, so the panel can show every value native can write, and
writes nothing native could not. The panel displays values naturally; for example, a volume reads as 0–100 % rather
than native's inverted vertical trackbar.

| Section | Name | Key | Domain | Native control | Applies |
|---|---|---|---|---|---|
| ﾒｯｾｰｼﾞ | `message-speed` | `message:MessageSpeed` | 0..100 ms per glyph | page 100 `0x414`, pos = 100 − v | on OK |
| | `window-opacity` | `message:MesWinAlpha` | 0..16 | `0x415`, pos = 16 − v | on OK |
| | `auto-wait-voice` | `message:AutoMessageTime0` | 0..10000 ms, step 100 | `0x416` (音声再生時) | on OK |
| | `auto-wait-text` | `message:AutoMessageTime1` | 0..10000 ms, step 100 | `0x417` (テキストのみ) | on OK |
| | `smooth-text` | `message:UseAntiFont` | 0/1 | `0x3e8`; shown only when `ENABLEANTIFONT` binds the key | on OK |
| | `wheel-advance` | `message:AdvanceMesOnWheel` | 0/1 | `0x3e9` | on OK |
| ｻｳﾝﾄﾞ | `master-volume`, `bgm-volume`, `se-volume`, `voice-volume` | `sound:Volume0..3` | 0..10000, step 100 | page 102 `0x414`–`0x417` | live; Cancel reverts |
| | `bgm`, `se`, `voice` | the Music, SE and Voice route enables | on/off | `0x3e8`, `0x3e9`, `0x3ed` | live; Cancel reverts |
| | `lower-bgm-on-voice` | `sound:MusicFadeOnVoicePlaying` | 0/1 | `0x3ea` | on OK |
| | `lower-bgm-level` | `sound:MusicFadeOnVoicePlayingVolume` | 0..100 % | `0x418` | on OK |
| その他 | `effect-skip` | `system:EffectSkipOnClick` | 0/1 | page 104 `0x3e8` | on OK |

**The panel keeps 設定's commit model.** The panel edits a draft:

- **決定** applies the draft in `agerc_settings_apply`'s order (`engine-re.md`, "設定"), then launches
  `CALLBACK_SETTING.BIN` once.
- **ｷｬﾝｾﾙ**, Escape or closing the panel discards the draft and restores the live ｻｳﾝﾄﾞ values.

A script can therefore observe the same sequence as natively. The ﾒｯｾｰｼﾞ values reach the VM through
`RequestMessageSettings`, as they do today.

**Not exposed, as natively:**
- `RMouseEvent` and the radio-group keys;
- `Font`, `UseMMX`, `OutErrorLog`;
- the display keys other than the screen mode;
- `sound:Movie` and `Volume4`;
- Kamidori's `AntiFontLevel`: its 設定 pages have no control for it.

The registry keeps all of them. The screen mode is a command, not a draft option.

### 5.4 Configuration from SYS4INI.BIN

| Key | Effect |
|---|---|
| `set:Menu_*` | Prunes the bar (§5.2) |
| `set:NoSetMusic` | Nonzero, as in both games: ｻｳﾝﾄﾞ has the BGM on/off route. 0 would select page 101's Music source radio, which is not carried (§10) |
| `set:CoexistMesSkip` | The toggles' Skip/Auto exclusion (engine core, already modelled) |
| `ENABLEANTIFONT` | Whether `message:UseAntiFont` is bound, and so whether `smooth-text` exists |
| The catalog title (`set:GameName`), `set:GameVersion`, `VERREGPOS`, `set:Copyright` | The 情報 section (§5.5) |
| `set:UseAppDataFolder` | The image folder's default |

### 5.5 情報: about and diagnostics

- **About.** This section replaces ﾊﾞｰｼﾞｮﾝ情報. It shows:
  - the engine's identity (`EngineIdentity`: name, version, license, FFmpeg notice, notices pointer);
  - the game name (catalog title) and copyright (`set:Copyright`);
  - the game version, by the existing rule: with `VERREGPOS` set, the InstallShield `DisplayVersion` on Windows,
    else `set:GameVersion`. The InstallShield value is install metadata, not a game binary.

  The product ID stays dropped.
- **Diagnostics.** This replaces 動作環境. It shows the OS, the Godot version, the renderer and GPU adapter, the
  audio output, the engine version and the data paths, with a ｸﾘｯﾌﾟﾎﾞｰﾄﾞにｺﾋﾟｰ button. That is what a bug report
  needs. Native's DirectX version, CPU clock and VRAM figures are not carried, so `AgercSystemInfo` retires.
- **Icon.** The window, the strip and 情報 use our own OME icon. The game's icon exists only inside the binaries.

### 5.6 Port-only options

These live in `user://OME.INI`, an INI file the port shares across games, outside `SYS4REG.INI`.

- **The initial set:**
  - `ui:Language`: `ja` (the default) or `en`;
  - `display:WindowWidth` and `display:WindowHeight`, which `--window-width`/`--window-height` set today;
  - `debug:FpsCounter` and `debug:LocatorHud`.
- **Flags override for one run.** A command-line flag wins for that run and does not write the file.
- **The driver sandboxes the file** with the saves, so a driven run never reads the player's language or window size.

The panel shows these options in their own section, OME.

## 6. Strings

- **One keyed table per language.** These are JSON files embedded in `Ome.Engine.Frontend`: `Shell/Strings/ja.json`
  and `en.json`. The keys name what a string is for (`command.skip`, `option.message-speed`,
  `exit.prompt`), not its text.
- **Japanese stays close to AGE's.** The table uses AGERC's wording, half-width katakana included. Examples: the
  menu labels above; 遅い/速い, 薄い/濃い, 音声再生時/テキストのみ, 主音量, 効果音, 音声, 音声再生時にBGMﾎﾞﾘｭｰﾑを下げる,
  エフェクト中にキー入力でエフェクトをスキップ; 終了, 次の方法で終了しますか？, プログラムを終了する(&E), タイトルに戻る(&T),
  OK, ｷｬﾝｾﾙ.
  - **Departures.** Mnemonics stay on the bar and in the 終了 dialog, but the panel drops them. Labels for parts
    that do not exist natively (決定, the OME section, diagnostics) are our own. Page-105 labels change with that
    page.
- **English is our own wording.** We do not copy the translated install's DLL text, which is the translator's.
- **Language choice.** `ui:Language` selects the table, and a missing key falls back to Japanese.
- **Relation to the localization brainstorm** (backlog). This table is the first engine-owned text with keys. The
  wider questions stay in that backlog item: mod overrides, per-profile wording, fonts, and the other engine dialogs
  (the error boxes). The shell's keys must not get in their way.

## 7. Presentation

### 7.1 Options panel

- **A new Godot overlay** inside the game viewport. It is laid out in the game's logical resolution, so it scales
  with the window, and it draws over the frozen frame.
- **Sections:** ﾒｯｾｰｼﾞ, ｻｳﾝﾄﾞ, その他, 画面, OME and 情報. The 画面 section holds the screen-mode command and the
  window size.
- **Navigation:**
  - The mouse and Godot's focus navigation work throughout.
  - Tab, the arrows, Enter and Escape follow the dialog-manager rules that `Win32TemplateDialog` uses today.
  - A controller drives the same focus chain.
- **Entry points:** the commands in §5.1, from the bar or the strip.
- **Font:** bundled Noto Sans JP (§11), so the panel looks the same on every OS.

### 7.2 Menu bar and strip

- **Windowed.** The existing classic bar draws the model's tree: `NativeMenuBar`, its popups and keyboard mode, with
  Alt/F10 entry, the op-`0x10f` F10 mask and the window menu. The window geometry is unchanged: a 20 px bar, and the
  800×600 client below it.
- **Fullscreen.** `SystemMenuStrip` draws the same tree, with the game title and the OME icon in its caption and
  its minimize, restore and close buttons. Its dwell is op `0x149`'s value.
- **The bar's look is ours to keep.** It needs no DLL: only the tree came from AGERC. Its remaining look differences
  from native are no longer a parity target (§9).

### 7.3 終了 dialog

`ExitConfirmationDialog` keeps its behaviour:

- the 0x80 dim;
- the radios プログラムを終了する(&E) and タイトルに戻る(&T);
- OK and ｷｬﾝｾﾙ;
- results 1 (quit, saving `SAVE.DAT`), 2 (title, after 250 ms) and 0 (cancel);
- no dialog while minimized.

Its layout and labels come from our constants and the string table instead of dialog 3, so every install shows it.

### 7.4 What retires

**Runtime code:**
- the DLL readers: `PeResource`, `NativeMenuResource`, `DialogTemplate`, `IconResource`;
- `AgercMenu`, `AgercExitDialog`, `AgercSettingsText`;
- `AgercSettingsDialog`, `AgercVersionDialog`, `AgercSystemInfo`, `Main.AgercDialogs.cs`;
- `Win32TemplateDialog`, and the classic controls that only 設定 and ﾊﾞｰｼﾞｮﾝ情報 use (`Win32Trackbar`,
  `Win32TabControl`, `Win32GroupBox`, `Win32Edit`, `Win32StaticEdge`, `Win32StaticImage`, `Win32ClassicCheckBox`).
  The 終了 dialog uses only its dialog-unit conversion (`BaseUnits`, `ClientSizeOf`), and the pixel constants
  that conversion gives take its place.

**Stays:** `Win32Dialog`, `Win32DialogFrame`, `Win32DialogStyle`, `Win32Font`, `Win32StaticText`,
`Win32ClassicButton`, `Win32ClassicRadio` and `Win32DialogIcon`. The 終了 dialog, the script error box, the bar and
the strip use them.

**Tests:**
- These read a game's DLL, so they retire: `NativeResourceTests`; the DLL cases of `NativeMenuTests` and
  `EngineIdentityTests`.
- Model tests on synthetic `SYS4INI` keys replace them.

**Docs:** `engine-re.md` keeps the native findings as the reference the model follows.

## 8. Driving

- **`menu NAME|ID`** keeps its names on both targets. On the port it resolves through the shell's command table: a
  grayed or absent command fails the step, as now.
- **`dialogs` and `answer`** report each control by its Japanese text, whatever `ui:Language` shows, so a 終了
  answer reads the same on both targets. This settles the localization item's "stable drive texts" question for the
  shell.
- **A new step, `option NAME VALUE`, plus `options` to list them.** It sets one option and commits as 決定 does.
  - **On the port** it goes through the panel's model.
  - **On native** it drives the real 設定: open dialog 9 at the option's tab, set the control (`TBM_SETPOS` plus
    the trackbar's scroll notification, or `BM_CLICK` for a check), then press OK.

  One scenario can then change settings on both targets and compare their `SYS4REG.INI` files and VM positions.
- **Scenarios:**
  - These keep running unchanged: `himegari-menu-bar`, `-menu-keyboard`, `-fullscreen`, `-screenshot`,
    `-exit-dialog` and `-minimized-close`.
  - These click pixels in 設定 or ﾊﾞｰｼﾞｮﾝ情報, so they move to `option` steps: `-settings-dialog`,
    `-version-dialog`, `-antifont-off` and `-effect-skip-on-click`.
  - A new Kamidori clean-install scenario exercises 終了 → title and an `option` change.

## 9. Acceptance

- The port opens no `AGERC.DLL`. A test asserts that no runtime path resolves it, and a port run with the file
  renamed away behaves the same.
- On Himegari, Kamidori clean and Kamidori translated:
  - the bar, the strip, the panel and 終了 all work;
  - each `menu` command does what §5.1 says.
- The unchanged scenarios in §8 still pass on both targets.
- The `option` scenarios leave identical `SYS4REG.INI` files on both targets and reach the same VM positions. The
  same positions show that `CALLBACK_SETTING.BIN` ran.
- `message:ReadTextSkip` survives a restart on both targets.
- Model tests cover:
  - the `Menu_*` pruning and the refresh rules;
  - the op-`0x142` behaviour, including read-only sections in an open panel;
  - the apply order;
  - the string-table fallback.

## 10. Non-goals

- Pixel parity of the chrome. The backlog item "Native menu bar: look differences and undriven cases" is rewritten in
  M4: its look items become optional.
- 動作環境's native figures, the product ID, and the registration, debug, data-select and file-select dialogs
  (134, 6, 5, 8).
- The things neither game reaches. Their backlog notes stay:
  - the right-click ﾒｯｾｰｼﾞ popup (`RMouseEvent=2`);
  - the 設定 radio groups;
  - ｾｰﾌﾞ/ﾛｰﾄﾞ;
  - page 101 (`NoSetMusic=0`).
- The full localization framework (§6).
- Controller remapping. The panel only needs the focus chain.

## 11. Settled questions

The user accepted the three recommendations on 2026-11-09:

1. **No classic 設定 skin.** A Win32-look 設定 over the same model would be a second UI for the same options.
2. **No entry key for the panel yet.** The bar and the strip are its entry points until controller support. Few
   F-keys are free: F1 is AGE's screenshot key, F10 enters the bar, and the port's debug tools use F2–F4 and F6.
3. **The panel font is Noto Sans JP.** It covers Japanese and is under the OFL; M3 adds it to
   `THIRD_PARTY_NOTICES.md`.

## 12. Milestones

1. **M1 Engine core.**
   - Persist `message:ReadTextSkip`.
   - Add `IShellHost`.
   - Write the tests.

   Done 2026-11-09. The read-skip toggle loads from `SYS4REG.INI` and is written back at quit, and the menu bar's
   frontend commands and the 終了 dialog's outcomes go through `IShellHost`. The planned ChoiceWait field was
   dropped (§4.1).
2. **M2 Shell model and menu.**
   - Build `Ome.Engine.Frontend.Shell`: commands, tree, options, state, string tables.
   - Feed the bar, the strip and 終了 from it, and stop reading the DLL for them.
   - Write the model tests.

   After M2, the clean Kamidori install has its bar, strip and 終了.

   Done 2026-11-09. `Ome.Engine.Frontend.Shell` holds the commands (`ShellCommands`), the menu 110 tree with its
   prune and refresh (`ShellMenu`), the state (`ShellState`), the options (`ShellOptions`), the string tables
   (`ShellStrings`, `Strings/ja.json` and `en.json`) and the 終了 texts (`ShellExitDialog`). The bar, the strip and
   終了 come from it, and the port no longer reads menu 110 or dialog 3; `ShellModelTests` checks the model. Both
   Kamidori installs now have the bar, the strip and 終了. Until M3:
   - The language is fixed to `ja`, since `ui:Language` comes with `OME.INI`. The translated Kamidori install shows
     a Japanese bar and 終了 where native shows English.
   - The section commands and ﾊﾞｰｼﾞｮﾝ情報 still open AGERC's dialogs. They are grayed when the game's `AGERC.DLL`
     lacks dialogs 9 and 2, as the clean Kamidori install's packed DLL does.
   - The strip's caption icon is blank until the OME icon.

   A strip command now keeps the frame frozen across the strip's hide, so the frame 終了 dims no longer shows the
   strip.
3. **M3 Options panel.**
   - Build the panel with all its sections, the bundled font and the OME icon.
   - Retire 設定, ﾊﾞｰｼﾞｮﾝ情報, 動作環境 and the DLL readers (§7.4).
   - Add `user://OME.INI`.
4. **M4 Driving and docs.**
   - Add the `option` and `options` steps on both targets.
   - Move the four scenarios to them, and add the Kamidori scenario.
   - Update `tools-reference.md`, `docs/modding/games/kamidori.md` and the backlog: close the shell item and
     rewrite the menu-bar look item.
