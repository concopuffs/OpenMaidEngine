using Ome.Engine.Vm;

namespace Ome.Engine.Frontend.Shell;

/// <summary>The options panel's sections (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §7.1). 情報 holds
/// the about view and the diagnostics.</summary>
public enum ShellSection { Message, Sound, Other, Screen, Ome, Info }

/// <summary>
/// What a shell surface reads when it opens: the bar or the strip opening a popup (native's WM_INITMENU), the panel,
/// the 終了 dialog. Every enabled and checked state comes from this snapshot (spec §4.2).
/// </summary>
/// <param name="Windowed">IAGEService +0x08: the game runs in a window.</param>
/// <param name="Vm">The VM's menu state (<see cref="IVmMenuServices.MenuState"/>), which carries op 0x142's gate.</param>
public readonly record struct ShellState(bool Windowed, VmMenuState Vm);
