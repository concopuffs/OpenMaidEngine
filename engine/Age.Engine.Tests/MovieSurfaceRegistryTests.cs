using Age.Engine.Sys4;

public class MovieSurfaceRegistryTests
{
    [Fact]
    public void SameResourceOnTwoSurfacesHasIndependentFramesCompletionAndRelease()
    {
        var registry = new MovieSurfaceRegistry();
        MovieSurfaceBinding first = registry.Begin(7, 0x2b42, out _);
        MovieSurfaceBinding second = registry.Begin(8, 0x2b42, out _);
        var firstFrame = Frame(1);
        var secondFrame = Frame(2);

        Assert.True(registry.PublishFrame(first.PlaybackId, firstFrame, "MVB126.AGF", 0x2b42));
        Assert.True(registry.PublishFrame(second.PlaybackId, secondFrame, "MVB126.AGF", 0x2b42));
        Assert.True(registry.Complete(first.PlaybackId));

        MovieSurfaceRelease released = registry.ReleaseIfCompleted(7);

        Assert.Equal(MovieSurfaceReleaseKind.Released, released.Kind);
        Assert.False(registry.IsBound(7));
        Assert.True(registry.IsActive(8));
        Assert.True(registry.TryResolveSurface(8, out MovieSurfaceFrame? remaining));
        Assert.Same(secondFrame, remaining!.Image);
        Assert.True(registry.Complete(second.PlaybackId));
        Assert.Equal(MovieSurfaceReleaseKind.Released, registry.ReleaseIfCompleted(8).Kind);
        Assert.False(registry.HasActivePlayback);
    }

    [Fact]
    public void ReplacingSurfaceInvalidatesLateEventsFromPriorPlayback()
    {
        var registry = new MovieSurfaceRegistry();
        MovieSurfaceBinding prior = registry.Begin(7, 0x2bdc, out _);

        MovieSurfaceBinding current = registry.Begin(7, 0x2bad, out MovieSurfaceBinding? replaced);

        Assert.Equal(prior, replaced);
        Assert.False(registry.Complete(prior.PlaybackId));
        Assert.False(registry.PublishFrame(prior.PlaybackId, Frame(1), "OLD.AGF", 1));
        Assert.True(registry.IsActive(7));
        Assert.True(registry.PublishFrame(current.PlaybackId, Frame(2), "NEW.AGF", 2));
        Assert.True(registry.TryResolveSurface(7, out MovieSurfaceFrame? frame));
        Assert.Equal("NEW.AGF", frame!.Name);
    }

    [Fact]
    public void ReleasedMovieResourceRemainsTypedAsMovieForCleanupFrame()
    {
        var registry = new MovieSurfaceRegistry();
        MovieSurfaceBinding binding = registry.Begin(7, 0x2bde, out _);

        Assert.True(registry.IsKnownMovieResource(0x2bde));
        Assert.True(registry.Complete(binding.PlaybackId));
        Assert.Equal(MovieSurfaceReleaseKind.Released, registry.ReleaseIfCompleted(7).Kind);

        Assert.False(registry.IsBound(7));
        Assert.False(registry.TryResolveResource(0x2bde, out _));
        Assert.True(registry.IsKnownMovieResource(0x2bde));
        Assert.False(registry.IsKnownMovieResource(0x2af5));
    }

    private static RgbaImage Frame(byte value)
        => new(1, 1, new[] { value, value, value, (byte)255 });
}
