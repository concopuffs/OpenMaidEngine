using System;
using System.Collections.Generic;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Diagnostics;

namespace Ome.Engine.Hosting;

/// <summary>
/// The lifted host (engine layering milestone 4), what is left of it. Its state went to the natives in milestones 5 to
/// 11; it keeps the members of <see cref="IHost"/> the VM calls, each now a call to the native that does the work
/// (the waits are <see cref="InputAndWaits"/>'s), the cursor, the Auto delays' seed and the shutdown write of the message
/// settings, and the front end's page notices. Milestone 12 removes it.
/// </summary>
public sealed partial class EngineHost : IHost, IWaitListener
{
    private readonly IFrontEnd _frontEnd;
    private readonly SkipAndAuto _skip;
    private readonly Graphics _graphics;  // the screen transitions the waits block on start here
    private readonly Movies _movies;      // the diagnostic snapshot lists its surfaces
    private readonly SavesAndSettings _saves;   // SYS4REG.INI's [message] keys, and message:ReadTextSkip at shutdown
    private readonly TextManager _text;
    private readonly InputAndWaits _waits;
    private readonly ResourceMap _res;
    private readonly FrameClock _clock;
    private readonly TimelineLog? _timeline;

    /// <param name="frontEnd">The front end: the page notices and the cursor go to it.</param>
    /// <param name="skipAndAuto">The skip and auto native the VM runs with: the host keeps the Auto delays in it for
    /// SYS4REG.INI.</param>
    /// <param name="graphics">The graphics native the VM runs with: the legacy screen transitions start in it.</param>
    /// <param name="movies">The movie native the VM runs with: the diagnostic snapshot reads it.</param>
    /// <param name="saves">The save and settings native the VM runs with: the host seeds the Auto delays from
    /// SYS4REG.INI's [message] and writes the message settings back at shutdown (<see cref="FlushMessageSettings"/>).</param>
    /// <param name="text">The text native the VM runs with: its settings are written back at shutdown.</param>
    /// <param name="waits">The input and waits native the VM runs with: every wait the VM asks the host for is its.</param>
    public EngineHost(IFrontEnd frontEnd, SkipAndAuto skipAndAuto, Graphics graphics, Movies movies,
                      SavesAndSettings saves, TextManager text, InputAndWaits waits, ResourceMap res,
                      FrameClock clock, TimelineLog? timeline = null)
    {
        _frontEnd = frontEnd ?? throw new ArgumentNullException(nameof(frontEnd));
        _skip = skipAndAuto ?? throw new ArgumentNullException(nameof(skipAndAuto));
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _movies = movies ?? throw new ArgumentNullException(nameof(movies));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _waits = waits ?? throw new ArgumentNullException(nameof(waits));
        _res = res;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeline = timeline;
        _waits.Listen(this);
        if (_saves.Settings is { } settings) SeedMessageSettings(settings);
    }

    public Sys4LogicalCanvas LogicalCanvas => _graphics.Canvas;
    public void ReportWarning(string message) => System.Console.Error.WriteLine(message);

    void IWaitListener.PageWaitStarted(int page) => _frontEnd.PageBreak();
    void IWaitListener.PageCleared() => _frontEnd.ClearPage();

    /// <summary>End every wait and keep them ended: the engine is stopping. The AGE cursor goes.</summary>
    public void Stop()
    {
        _frontEnd.ClearAgeCursor();
        _waits.Stop();
    }

    public EngineHostDiagnosticSnapshot CaptureDiagnosticSnapshot()
        => new(
            _waits.IsWaiting,
            _waits.IsTransitionWaiting,
            _waits.IsSleeping,
            _text.IsRevealing,
            _waits.IsModalMovieWaiting,
            _text.IsAdvPagePresentationSuspended,
            _skip.FastForwardActive,
            _graphics.ScreenTransitionPending,
            _waits.TransitionStartedAtMs,
            _movies.SurfaceDiagnostics(),
            _movies.CompletedPlaybackIds());
}

public sealed record EngineHostDiagnosticSnapshot(
    bool IsInputWaiting, bool IsTransitionWaiting, bool IsSleeping,
    bool IsTextRevealing, bool IsModalMovieWaiting, bool IsAdvPagePresentationSuspended,
    bool IsMessageSkipActive, bool IsScreenTransitionActive, long TransitionStartedAtMs,
    IReadOnlyList<MovieSurfaceDiagnostic> MovieSurfaces, IReadOnlyList<long> CompletedMoviePlaybackIds);
