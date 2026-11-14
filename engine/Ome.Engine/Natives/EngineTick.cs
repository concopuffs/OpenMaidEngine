using Ome.Engine.Diagnostics;

namespace Ome.Engine.Natives;

/// <summary>
/// The engine tick of docs/superpowers/specs/2026-11-12-engine-layering-design.md §5.3, part 1: one function that runs
/// the natives' timed work in native's order (engine_main_tick_with_exception_policy@0x411840). It runs at every
/// instruction boundary and on each wake of every wait that blocks the engine thread, except the modal ones (the waits
/// run it through one helper, <c>InputAndWaits.Blocking.cs</c>).
/// <para>It also owns the statement block of ops 0x1f4/0x1f5 (implementation plan §3.2, addition 1), since every reader
/// is a step of native's tick: while the outermost block is open the tick withholds the delayed starts, the waits
/// withhold presentation, and the interpreter defers callback scripts (docs/engine-re.md, "Statement blocks").</para>
/// </summary>
/// <remarks>Threads: the engine thread runs the tick and the statement block ops; the front end reads
/// <see cref="StatementBlockOpen"/> from its own.</remarks>
public sealed class EngineTick
{
    private readonly Audio _audio;
    private readonly TimelineLog? _timeline;

    // Scene-lifetime state: op 0x9's reset rebuilds it (engine layering spec §5.1, rule 5).
    private sealed class Scene
    {
        public volatile bool StatementBlockOpen;   // statement_block_active (ctx+0x5f2fc)
        public int StatementBlockDepth;            // statement_block_depth (ctx+0x5f300): opens nested inside it
    }

    private Scene _scene = new();

    public EngineTick(Audio audio, TimelineLog? timeline = null)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _timeline = timeline;
    }

    /// <summary>Whether the outermost statement block is open.</summary>
    public bool StatementBlockOpen => _scene.StatementBlockOpen;

    /// <summary>Run the timed work that is due.</summary>
    public void Service() => _audio.Service(_scene.StatementBlockOpen);

    /// <summary>Op 0x1f4: open a block, or count one nested inside the open block.</summary>
    public void OpenStatementBlock()
    {
        if (_scene.StatementBlockOpen)
        {
            _scene.StatementBlockDepth++;
            return;
        }
        _scene.StatementBlockOpen = true;
        _timeline?.Event("statement-block", new() { ["active"] = true });
    }

    /// <summary>Op 0x1f5: close the innermost block. An unmatched close is ignored, as natively.</summary>
    public void CloseStatementBlock()
    {
        if (_scene.StatementBlockDepth > 0)
        {
            _scene.StatementBlockDepth--;
            return;
        }
        if (!_scene.StatementBlockOpen) return;
        _scene.StatementBlockOpen = false;
        _timeline?.Event("statement-block", new() { ["active"] = false });
    }

    /// <summary>A root reload abandons every open block with its frames.</summary>
    public void ResetScene() => _scene = new();
}
