namespace Ome.Engine.Model;

/// <summary>The mixer category whose volume and route a movie's audio follows (movie_sound_route_from_flags@0x405490;
/// SYS4433 0x405200). docs/engine-re.md, "Movie-to-surface opcode 0x236".</summary>
public static class MovieSoundRoute
{
    /// <summary>Movie flag bits 0x10000/0x20000/0x40000/0x80000 force silence, music, SFX or voice. Without one,
    /// SYS4INI DEPENDMOVIESOUND picks the route by the same numbering, 4 being the movie category: Himegari's 1 plays
    /// its movies at the music volume and Kamidori's 2 at the SFX volume. The movie's volume is then master × that
    /// category's volume, and the movie is silent while the category's route is off
    /// (sound_route_volume@0x407950, sound_route_is_enabled@0x405420).</summary>
    /// <returns>The category, or null for a silent movie (route 0, or any value outside 1..4).</returns>
    public static AudioMixerCategory? Resolve(long movieFlags, int dependMovieSound)
    {
        ulong flags = unchecked((ulong)movieFlags);
        int route = (flags & 0x10000) != 0 ? 0
            : (flags & 0x20000) != 0 ? 1
            : (flags & 0x40000) != 0 ? 2
            : (flags & 0x80000) != 0 ? 3
            : dependMovieSound;
        return route is >= 1 and <= 4 ? (AudioMixerCategory)route : null;
    }
}
