using System.IO;
using System.Linq;
using Ome.Engine.Devices;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Xunit;

/// <summary>The movie native over fakes (engine layering milestone 8): what the ops start, and what the front end's
/// frame service does with it.</summary>
public class MoviesNativeTests
{
    private static RgbaImage Frame(byte green = 0x80) => new(1, 1, [0, green, 0, 255]);

    private static MovieRig RigWithSurface(int slot = 9, Func<string, FakeMovieDecoder>? decoder = null,
                                           int dependMovieSound = 0)
    {
        var rig = new MovieRig(decoder, dependMovieSound);
        rig.Graphics.CreateSurface(slot, 280, 352);
        return rig;
    }

    [Fact]
    public void ThePlayReturnsTheDecodersStopTimeAndBindsTheSurface()
    {
        MovieRig rig = RigWithSurface(decoder: _ => new FakeMovieDecoder { StopTimeMs = 500 });

        Assert.Equal(500, rig.Movies.PlayToSurface("BTL.BIN", 0x2be3, 9, 0, 0));

        Assert.True(rig.Movies.IsSurfaceActive(9));
        Assert.Equal(FakeMovieAssets.NameOf(0x2be3), rig.Decoders.OpenedName);
        Assert.Equal(new MovieSurfaceDiagnostic(9, 1, 0x2be3, false, false, null),
                     rig.Movies.SurfaceDiagnostics().Single());
    }

    [Fact]
    public void AnUnresolvedMovieAnswersNull()
    {
        MovieRig rig = RigWithSurface();
        rig.Assets.Unresolved.Add(0x2be3);

        Assert.Null(rig.Movies.PlayToSurface("btl.bin", 0x2be3, 9, 0, 0));

        Assert.False(rig.Movies.IsSurfaceActive(9));
        Assert.Empty(rig.Decoders.Decoders);
        Assert.Equal(["movie unresolved BTL:0x2be3"], rig.Log);
    }

    [Fact]
    public void WithoutADecoderAMovieCompletesAtOnce()
    {
        var graphics = new Graphics(null, new FrameClock(), new Sys4LogicalCanvas(800, 600));
        graphics.CreateSurface(9, 280, 352);
        var movies = new Movies(graphics, new FakeMovieAssets(), decoders: null, sound: null, dependMovieSound: 0);

        Assert.Equal(0, movies.PlayToSurface("BTL", 0x2be3, 9, 0, 0));

        Assert.False(movies.IsSurfaceActive(9));
        Assert.Equal([1L], movies.CompletedPlaybackIds());
    }

    [Fact]
    public void ADecoderThatCannotOpenTheFileCompletesTheMovieAtOnce()
    {
        MovieRig rig = RigWithSurface();
        rig.Decoders.Failure = new InvalidDataException("bad pack header");

        Assert.Equal(0, rig.Movies.PlayToSurface("BTL", 0x2be3, 9, 0, 0));

        Assert.False(rig.Movies.IsSurfaceActive(9));
        Assert.Equal(1, rig.Wakes);
        Assert.Contains($"movie decode failed {FakeMovieAssets.NameOf(0x2be3)}: bad pack header", rig.Log);
    }

    [Fact]
    public void TheFirstFrameWaitsForTheStartDelay()
    {
        var decoder = new FakeMovieDecoder { StopTimeMs = 1000, Frame = Frame() };
        MovieRig rig = RigWithSurface(decoder: _ => decoder);
        rig.Movies.PlayToSurface("BTL", 0x2be3, 9, 0, startDelayMs: 750);

        rig.ServiceFrame();
        rig.Advance(749);
        Assert.NotNull(decoder.Frame);
        Assert.False(rig.Movies.SurfaceDiagnostics().Single().HasFrame);

        rig.Advance(1);
        Assert.Null(decoder.Frame);
        Assert.True(rig.Movies.SurfaceDiagnostics().Single().HasFrame);
        Assert.Contains(rig.Log, line => line.StartsWith("movie first frame ") && line.Contains("at render frame 3"));
    }

    [Fact]
    public void AMovieCompletesWhenItsDecoderEnds()
    {
        var decoder = new FakeMovieDecoder { StopTimeMs = 1000 };
        MovieRig rig = RigWithSurface(decoder: _ => decoder);
        rig.Movies.PlayToSurface("BTL", 0x2be3, 9, 4, 0);
        rig.ServiceFrame();
        Assert.True(rig.Movies.IsSurfaceActive(9));
        Assert.True(rig.Movies.HoldsServiceWait);

        decoder.IsCompleted = true;
        rig.ServiceFrame();

        Assert.False(rig.Movies.IsSurfaceActive(9));
        Assert.Equal([1L], rig.Movies.CompletedPlaybackIds());
        Assert.Equal(1, rig.Wakes);
        // A completed movie stays bound, and still holds the effect wait until its surface is released.
        Assert.True(rig.Movies.HoldsServiceWait);
        Assert.False(decoder.Disposed);
    }

    [Fact]
    public void TheWatchdogCompletesAMovieWhoseDecoderNeverEnds()
    {
        MovieRig rig = RigWithSurface(decoder: _ => new FakeMovieDecoder { StopTimeMs = 1000 });
        rig.Movies.PlayToSurface("BTL", 0x2be3, 9, 0, 0);   // the stop time plus 2 s, at least 5 s

        rig.ServiceFrame();
        rig.Advance(4999);
        Assert.True(rig.Movies.IsSurfaceActive(9));

        rig.Advance(1);
        Assert.False(rig.Movies.IsSurfaceActive(9));
        Assert.Contains(rig.Log, line => line.Contains("forcing completion after 5000 ms"));
    }

    [Fact]
    public void AReleasedSurfaceStopsItsMovieAtTheNextFrameService()
    {
        var decoder = new FakeMovieDecoder { StopTimeMs = 1000 };
        MovieRig rig = RigWithSurface(decoder: _ => decoder);
        rig.Movies.PlayToSurface("BTL", 0x2be3, 9, 4, 0);
        rig.ServiceFrame();

        rig.Graphics.ReleaseSurface(9);

        Assert.False(rig.Movies.IsSurfaceActive(9));
        Assert.False(rig.Movies.HoldsServiceWait);
        Assert.False(decoder.Disposed);
        rig.ServiceFrame();
        Assert.True(decoder.Disposed);
        Assert.Contains(rig.Log, line => line.StartsWith("movie stopped ") && line.EndsWith("at render frame 2"));
        Assert.Empty(rig.Movies.PlaybackDiagnostics().Playing);
    }

    [Fact]
    public void ASecondMovieOnASurfaceStopsTheFirst()
    {
        MovieRig rig = RigWithSurface();
        rig.Movies.PlayToSurface("BTL", 0x2be3, 9, 0, 0);
        rig.ServiceFrame();

        rig.Movies.PlayToSurface("BTL", 0x2b94, 9, 0, 0);
        rig.ServiceFrame();

        Assert.True(rig.Decoders.Decoders[0].Disposed);
        Assert.False(rig.Decoders.Decoders[1].Disposed);
        Assert.Equal(0x2b94, rig.Movies.SurfaceDiagnostics().Single().ResourceId);
        Assert.Equal([2L], rig.Movies.PlaybackDiagnostics().Playing.Select(movie => movie.PlaybackId));
    }

    [Fact]
    public void TheScenesResetStopsEveryMovieAndKeepsTheSurfaces()
    {
        MovieRig rig = RigWithSurface();
        rig.Graphics.CreateSurface(10, 280, 352);
        rig.Movies.PlayToSurface("BTL", 0x2be3, 9, 0, 0);
        rig.Movies.PlayToSurface("BTL", 0x2b94, 10, 0, 0);
        rig.ServiceFrame();

        rig.Movies.ResetScene();
        rig.ServiceFrame();

        Assert.Empty(rig.Movies.SurfaceDiagnostics());
        Assert.All(rig.Decoders.Decoders, decoder => Assert.True(decoder.Disposed));
        // A movie's surface takes the screen's size until its first frame; the reset leaves the surface as it is.
        Assert.Equal((800, 600), rig.Graphics.SurfaceSize(10));
    }

    [Fact]
    public void ARateOnASurfaceWithoutAMovieIsIgnored()
    {
        MovieRig rig = RigWithSurface();

        rig.Movies.SetPlaybackRate(9, 2.0);
        rig.ServiceFrame();

        Assert.Empty(rig.Log);
    }

    [Theory]
    [InlineData(0L, 0, AudioBus.Movie, true)]          // no route in the flags or SYS4INI: silent
    [InlineData(0L, 3, AudioBus.Voice, false)]         // SYS4INI DEPENDMOVIESOUND
    [InlineData(0x20000L, 3, AudioBus.Music, false)]   // the flags' route wins
    [InlineData(0x10000L, 3, AudioBus.Movie, true)]    // the flags' silence wins
    public void TheSoundPlaysOnItsRoutesBus(long flags, int dependMovieSound, AudioBus bus, bool muted)
    {
        MovieRig rig = RigWithSurface(
            decoder: _ => new FakeMovieDecoder { StopTimeMs = 1000, AudioInfo = new MovieAudioInfo(44100, 2) },
            dependMovieSound: dependMovieSound);
        rig.Movies.PlayToSurface("BTL", 0x2be3, 9, flags, 0);

        rig.ServiceFrame();

        FakeAudioStream stream = rig.Sound.Streams.Single();
        Assert.Equal((44100, bus, muted), (stream.SampleRate, stream.Bus, stream.Muted));
    }

    [Fact]
    public void TheSoundStartsWithTheFirstFrameAndKeepsItsPresentationTimes()
    {
        var decoder = new FakeMovieDecoder
        {
            StopTimeMs = 1000, AudioInfo = new MovieAudioInfo(44100, 2), Frame = Frame(),
        };
        decoder.AudioChunks.Enqueue(new MovieAudioChunk(new float[441 * 2], 441, 0));
        decoder.AudioChunks.Enqueue(new MovieAudioChunk(new float[441 * 2], 441, 100));   // 90 ms after the first ends
        MovieRig rig = RigWithSurface(decoder: _ => decoder, dependMovieSound: 4);
        rig.Movies.PlayToSurface("BTL", 0x2be3, 9, 0, 0);

        rig.ServiceFrame();

        FakeAudioStream stream = rig.Sound.Streams.Single();
        Assert.Equal(["start-held", "push 0+441", "resume", "silence 3969", "push 0+441"], stream.Calls);
        Assert.Equal(1, decoder.AudioSubmittedMarks);
        Assert.Equal([0L], decoder.ClockAdvances);

        // What has been heard paces the decoder's video.
        stream.HeardSeconds = 0.25;
        rig.ServiceFrame();
        Assert.Equal([0L, 250L], decoder.ClockAdvances);
        Assert.Equal(1, decoder.AudioSubmittedMarks);

        rig.Movies.SetPlaybackRate(9, 1.5);
        rig.ServiceFrame();
        Assert.Equal("rate 1.5", stream.Calls[^1]);
        Assert.Equal([1.5], decoder.Rates);

        rig.Graphics.ReleaseSurface(9);
        rig.ServiceFrame();
        Assert.True(stream.Disposed);
    }

    [Fact]
    public void NoSoundIsQueuedBeforeTheFirstFrame()
    {
        var decoder = new FakeMovieDecoder { StopTimeMs = 1000, AudioInfo = new MovieAudioInfo(44100, 2) };
        decoder.AudioChunks.Enqueue(new MovieAudioChunk(new float[441 * 2], 441, 0));
        MovieRig rig = RigWithSurface(decoder: _ => decoder, dependMovieSound: 4);
        rig.Movies.PlayToSurface("BTL", 0x2be3, 9, 0, 0);

        rig.ServiceFrame();
        rig.Advance(100);
        Assert.Empty(rig.Sound.Streams.Single().Calls);

        decoder.Frame = Frame();
        rig.Advance(16);
        Assert.Equal(["start-held", "push 0+441", "resume"], rig.Sound.Streams.Single().Calls);
    }

    [Fact]
    public void AMaskMovieEndsItsTransitionWhenTheMovieEnds()
    {
        var decoder = new FakeMovieDecoder { StopTimeMs = 1000 };
        MovieRig rig = RigWithSurface(slot: 45, decoder: _ => decoder);
        var request = new MovieMaskTransitionRequest(11, 45, 10, 1, 0, 0, 4, 4, 0, 0x325e, 0, 1000);

        rig.Movies.PlayMaskTransition("SC0000", request);

        Assert.Equal(1000, rig.Decoders.OpenedPresentationDurationMs);
        Assert.True(((IGraphicsMovies)rig.Movies).IsMaskedSurface(45));
        Assert.False(rig.Graphics.State.SnapshotMovieMaskTransitions().Single().Completed);
        Assert.True(rig.Graphics.State.TryGetMovieStopTime(45, out long? stopTime));
        Assert.Equal(1000, stopTime);

        decoder.Frame = Frame();
        rig.ServiceFrame();
        decoder.IsCompleted = true;
        rig.ServiceFrame();

        Assert.True(rig.Graphics.State.SnapshotMovieMaskTransitions().Single().Completed);
        Assert.False(rig.Movies.IsSurfaceActive(45));
        // The waits wake for the first mask, the decoded frame's, the terminal one and the completion.
        Assert.Equal(4, rig.Wakes);
    }

    [Fact]
    public void ShutdownDisposesEveryDecoderAndStream()
    {
        MovieRig rig = RigWithSurface(
            decoder: _ => new FakeMovieDecoder { StopTimeMs = 1000, AudioInfo = new MovieAudioInfo(44100, 2) },
            dependMovieSound: 4);
        rig.Graphics.CreateSurface(10, 280, 352);
        rig.Movies.PlayToSurface("BTL", 0x2be3, 9, 0, 0);
        rig.ServiceFrame();
        rig.Movies.PlayToSurface("BTL", 0x2b94, 10, 0, 0);   // opened, not yet taken by a frame service

        rig.Movies.Shutdown();

        Assert.All(rig.Decoders.Decoders, decoder => Assert.True(decoder.Disposed));
        Assert.All(rig.Sound.Streams, stream => Assert.True(stream.Disposed));
    }
}
