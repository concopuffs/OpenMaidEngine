using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
using Ome.Engine.Diagnostics;

namespace Ome.Engine.Hosting;

public sealed partial class EngineHost : IHost, IFastForwardListener
{
    private readonly IFrontEnd _frontEnd;
    private readonly SkipAndAuto _skip;
    private readonly Audio _audio;        // the auto timer reads channel 12
    private readonly EngineTick _tick;    // every non-modal wait runs it (EngineHost.Waits.cs)
    private readonly Graphics _graphics;  // the surfaces and the publication state; the waits block on them here
    private readonly Movies _movies;      // op 0x20f's modal wait and the effect wait read it
    private readonly SavesAndSettings _saves;   // SYS4REG.INI's [message] keys, and message:ReadTextSkip at shutdown
    private readonly ShellServices _shell;      // the dialogs the modal waits wait on
    private readonly ResourceMap _res;
    private readonly Ome.Engine.Hosting.FrameClock _clock;
    private readonly TimelineLog? _timeline;
    private readonly PageLocatorState _locator;
    /// <param name="skipAndAuto">The skip and auto native the VM runs with: the host reads fast-forward from it, keeps
    /// the Auto delays in it for SYS4REG.INI, and keeps what a fast-forward change does here until those natives
    /// exist.</param>
    /// <param name="audio">The audio native the VM runs with: ADV Auto waits for its voice.</param>
    /// <param name="tick">The engine tick the VM runs with: every wait that is not modal runs it each time it
    /// wakes, and presentation waits while its statement block is open.</param>
    /// <param name="graphics">The graphics native the VM runs with: the presentation waits block on its
    /// publication.</param>
    /// <param name="saves">The save and settings native the VM runs with: the host seeds the Auto delays from
    /// SYS4REG.INI's [message] and writes the message settings back at shutdown (<see cref="FlushMessageSettings"/>).</param>
    /// <param name="shell">The shell-services native the VM runs with: the host's modal waits wait on its dialogs, a
    /// stop cancels them, and its menu requests wake a text wait.</param>
    /// <param name="movies">The movie native the VM runs with: the host runs op 0x20f's modal wait over it, and an
    /// effect wait holds while one of its movies does.</param>
    /// <param name="text">The text native the VM runs with: the host runs its reveal and page waits, and a click, a stop
    /// or fast-forward finishes its reveal.</param>
    public EngineHost(IFrontEnd frontEnd, SkipAndAuto skipAndAuto, Audio audio, EngineTick tick, Graphics graphics,
                        Movies movies, SavesAndSettings saves, ShellServices shell, TextManager text,
                        ResourceMap res,
                        Ome.Engine.Hosting.FrameClock clock,
                        PageLocatorState locator,
                        TimelineLog? timeline = null,
                        bool synchronizeExplicitPresentation = true)
    {
        _frontEnd = frontEnd; _res = res; _clock = clock;
        _skip = skipAndAuto ?? throw new ArgumentNullException(nameof(skipAndAuto));
        _skip.Listen(this);
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _locator = locator; _timeline = timeline;
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _movies = movies ?? throw new ArgumentNullException(nameof(movies));
        _movies.WakeWaitsWith(() => _frameSignal.Set());
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _shell.WakeWaitsWith(WakeInputCallbackService);   // a menu request reaches a text wait at once
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _text.ReadInputWaitFrom(() => IsWaiting);          // the wait indicator shows only in an input wait
        if (_saves.Settings is { } settings) SeedMessageSettings(settings);
        _synchronizeExplicitPresentation = synchronizeExplicitPresentation;
    }

    public Sys4LogicalCanvas LogicalCanvas => _graphics.Canvas;
    public void ReportWarning(string message) => System.Console.Error.WriteLine(message);
}

public sealed record EngineHostDiagnosticSnapshot(
    bool IsInputWaiting, bool IsTransitionWaiting, bool IsSleeping,
    bool IsTextRevealing, bool IsModalMovieWaiting, bool IsAdvPagePresentationSuspended,
    bool IsMessageSkipActive, bool IsScreenTransitionActive, long TransitionStartedAtMs,
    IReadOnlyList<MovieSurfaceDiagnostic> MovieSurfaces, IReadOnlyList<long> CompletedMoviePlaybackIds);
