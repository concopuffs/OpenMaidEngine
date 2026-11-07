using Age.Engine.Model;

/// <summary>movie_sound_route_from_flags@0x405490 and sound_route_volume@0x407950.</summary>
public class MovieSoundRouteTests
{
    [Theory]
    [InlineData(2, 1, AudioMixerCategory.Music)]          // Himegari's SC0000 movie (flags 2) at DEPENDMOVIESOUND 1
    [InlineData(2, 2, AudioMixerCategory.SoundEffect)]    // Kamidori
    [InlineData(0, 3, AudioMixerCategory.Voice)]
    [InlineData(0, 4, AudioMixerCategory.Movie)]
    [InlineData(0x20000, 2, AudioMixerCategory.Music)]    // a flag overrides the setting
    [InlineData(0x40000, 1, AudioMixerCategory.SoundEffect)]
    [InlineData(0x80000, 1, AudioMixerCategory.Voice)]
    [InlineData(0xa0000, 1, AudioMixerCategory.Music)]    // 0x20000 is tested before 0x80000
    public void TheRouteIsTheFlagsOrTheSetting(long flags, int dependMovieSound, AudioMixerCategory expected)
        => Assert.Equal(expected, MovieSoundRoute.Resolve(flags, dependMovieSound));

    [Theory]
    [InlineData(0x10000, 1)]   // the mute flag wins over every other bit
    [InlineData(0x30000, 4)]
    [InlineData(0, 0)]         // route 0
    [InlineData(0, 5)]         // no category: zero volume, and sound_route_is_enabled is false
    [InlineData(0, -1)]
    public void SomeRoutesAreSilent(long flags, int dependMovieSound)
        => Assert.Null(MovieSoundRoute.Resolve(flags, dependMovieSound));
}
