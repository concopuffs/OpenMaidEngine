using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

/// <summary>Where the movie native finds a movie's file: the game's catalog in a run, a fake in a test.</summary>
public interface IMovieAssets
{
    /// <summary>The movie a packed resource id names (ops 0x20f, 0x236, 0x241 and 0x24d), or null when there is none.</summary>
    AssetEntry? Resolve(long resourceId);

    /// <summary>The movie's file. Throws when it cannot be read or is not an MPEG program stream.</summary>
    MoviePayload Read(AssetEntry movie);
}

/// <summary>No movies: every lookup answers null.</summary>
public sealed class NoMovieAssets : IMovieAssets
{
    public static readonly NoMovieAssets Instance = new();

    public AssetEntry? Resolve(long resourceId) => null;

    public MoviePayload Read(AssetEntry movie)
        => throw new System.IO.FileNotFoundException($"no movie assets: {movie.Name}");
}

/// <summary>A game's movies through its <see cref="ResourceMap"/>.</summary>
public sealed class ResourceMovieAssets(ResourceMap resources) : IMovieAssets
{
    public AssetEntry? Resolve(long resourceId) => resources.ResolveMovie(resourceId);

    public MoviePayload Read(AssetEntry movie) => resources.ReadMovie(movie);
}
