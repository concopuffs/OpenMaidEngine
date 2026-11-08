using Age.Engine.Model;

public class MovieSurfaceRegistryTests
{
    [Fact]
    public void SameResourceOnTwoSurfacesHasIndependentFramesCompletionAndRelease()
    {
        var registry = new MovieSurfaceRegistry();
        MovieSurfaceBinding first = registry.Begin(7, 0x2b42, 0, out _);
        MovieSurfaceBinding second = registry.Begin(8, 0x2b42, 0, out _);
        var firstFrame = Frame(1);
        var secondFrame = Frame(2);

        Assert.True(registry.PublishFrame(first.PlaybackId, firstFrame, "MVB126.AGF", 0x2b42));
        Assert.True(registry.PublishFrame(second.PlaybackId, secondFrame, "MVB126.AGF", 0x2b42));
        Assert.True(registry.Complete(first.PlaybackId));

        MovieSurfaceRelease released = registry.Release(7);

        Assert.Equal(MovieSurfaceReleaseKind.Released, released.Kind);
        Assert.True(released.Completed);
        Assert.False(registry.IsBound(7));
        Assert.True(registry.IsActive(8));
        Assert.True(registry.TryResolveSurface(8, out MovieSurfaceFrame? remaining));
        Assert.Same(secondFrame, remaining!.Image);
        Assert.True(registry.Complete(second.PlaybackId));
        Assert.Equal(MovieSurfaceReleaseKind.Released, registry.Release(8).Kind);
    }

    [Fact]
    public void ReleasingASurfaceRemovesAMovieThatIsStillPlaying()
    {
        // Kamidori FIELD@0xbbf8: the chest effect plays into surface 0x2a, the anim clock runs out before the decoder
        // reports its end, op 0x1fa releases 0x2a and MES recreates it for its text box.
        var registry = new MovieSurfaceRegistry();
        MovieSurfaceBinding binding = registry.Begin(0x2a, 0x4437, 0, out _);
        Assert.True(registry.PublishFrame(binding.PlaybackId, Frame(1), "EF001.AGF", 0x4437));

        MovieSurfaceRelease released = registry.Release(0x2a);

        Assert.Equal(MovieSurfaceReleaseKind.Released, released.Kind);
        Assert.False(released.Completed);
        Assert.Equal(binding, released.Binding);
        Assert.False(registry.IsBound(0x2a));
        Assert.False(registry.TryResolveSurface(0x2a, out _));
        Assert.False(registry.Complete(binding.PlaybackId));   // the decoder's late end belongs to no surface now
        Assert.Equal(MovieSurfaceReleaseKind.NotBound, registry.Release(0x2a).Kind);
    }

    [Fact]
    public void ReplacingSurfaceInvalidatesLateEventsFromPriorPlayback()
    {
        var registry = new MovieSurfaceRegistry();
        MovieSurfaceBinding prior = registry.Begin(7, 0x2bdc, 0, out _);

        MovieSurfaceBinding current = registry.Begin(7, 0x2bad, 0, out MovieSurfaceBinding? replaced);

        Assert.Equal(prior, replaced);
        Assert.False(registry.Complete(prior.PlaybackId));
        Assert.False(registry.PublishFrame(prior.PlaybackId, Frame(1), "OLD.AGF", 1));
        Assert.True(registry.IsActive(7));
        Assert.True(registry.PublishFrame(current.PlaybackId, Frame(2), "NEW.AGF", 2));
        Assert.True(registry.TryResolveSurface(7, out MovieSurfaceFrame? frame));
        Assert.Equal("NEW.AGF", frame!.Name);
        Assert.True(registry.TryGetBinding(7, out MovieSurfaceBinding resolved));
        Assert.Equal(current, resolved);
    }

    [Fact]
    public void ReleasedMovieResourceRemainsTypedAsMovieForCleanupFrame()
    {
        var registry = new MovieSurfaceRegistry();
        MovieSurfaceBinding binding = registry.Begin(7, 0x2bde, 0, out _);

        Assert.True(registry.IsKnownMovieResource(0x2bde));
        Assert.True(registry.Complete(binding.PlaybackId));
        Assert.Equal(MovieSurfaceReleaseKind.Released, registry.Release(7).Kind);

        Assert.False(registry.IsBound(7));
        Assert.False(registry.TryResolveResource(0x2bde, out _));
        Assert.True(registry.IsKnownMovieResource(0x2bde));
        Assert.False(registry.IsKnownMovieResource(0x2af5));
    }

    [Fact]
    public void ResourceLookup_SelectsNewestPublishedPlayback()
    {
        var registry = new MovieSurfaceRegistry();
        MovieSurfaceBinding older = registry.Begin(7, 0x2b42, 0, out _);
        MovieSurfaceBinding unrelated = registry.Begin(8, 0x2bad, 0, out _);
        MovieSurfaceBinding newer = registry.Begin(9, 0x2b42, 0, out _);
        Assert.True(registry.PublishFrame(older.PlaybackId, Frame(1), "OLDER.AGF", 1));
        Assert.True(registry.PublishFrame(unrelated.PlaybackId, Frame(2), "OTHER.AGF", 2));
        Assert.True(registry.PublishFrame(newer.PlaybackId, Frame(3), "NEWER.AGF", 3));

        Assert.True(registry.TryResolveResource(0x2b42, out MovieSurfaceFrame? found));
        Assert.Equal("NEWER.AGF", found!.Name);
        Assert.False(registry.TryResolveResource(0x2af5, out _));
    }

    [Theory]
    [InlineData(0x0, false)]    // Kamidori's ADDEXP/SHOWGROW/BTL: the movie plays on while the script continues
    [InlineData(0x2, false)]    // Himegari's SC0000 chapter movie (its anim clock holds the wait instead)
    [InlineData(0x4, true)]
    [InlineData(0x5, false)]    // bit 0 set
    [InlineData(0x6, true)]
    [InlineData(0x20004, true)] // the sound-route bits do not matter
    public void OnlyFlagsWithBit2SetAndBit0ClearHoldTheServiceWait(long flags, bool holds)
    {
        var registry = new MovieSurfaceRegistry();
        MovieSurfaceBinding binding = registry.Begin(0x2a, 0x4437, flags, out _);

        Assert.Equal(holds, registry.HoldsServiceWait);   // pending start: native tests the stored flags
        Assert.True(registry.Complete(binding.PlaybackId));
        Assert.Equal(holds, registry.HoldsServiceWait);   // DirectShow's end leaves the flags word as it was
        Assert.Equal(MovieSurfaceReleaseKind.Released, registry.Release(0x2a).Kind);
        Assert.False(registry.HoldsServiceWait);          // releasing the surface removes the movie
    }

    private static RgbaImage Frame(byte value)
        => new(1, 1, new[] { value, value, value, (byte)255 });
}
