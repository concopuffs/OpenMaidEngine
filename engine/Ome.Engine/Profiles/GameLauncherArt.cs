namespace Ome.Engine.Profiles;

/// <summary>One image a game manifest names for the launcher (launcher spec §8.7): an <c>https</c> address and the
/// SHA-256 of the file as served, in lowercase hexadecimal. The launcher keeps a downloaded file only when its hash is
/// this one.</summary>
public sealed record GameArtSource
{
    public GameArtSource(string url, string sha256)
    {
        Url = GameLauncherArt.RequireHttps(url, "url");
        if (sha256 is not { Length: 64 } || !sha256.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException($"sha256 '{sha256}' is not 64 lowercase hexadecimal digits", nameof(sha256));
        Sha256 = sha256;
    }

    public Uri Url { get; }
    public string Sha256 { get; }
}

/// <summary>
/// A game manifest's <c>launcher</c> entry (launcher spec §8.7), which only the launcher reads: the publisher's page for
/// the game, and the addresses of its banner and cover. Only a built-in manifest names an address (spec §12), so a
/// package, a README or a profile file cannot make the launcher open a connection.
/// </summary>
public sealed record GameLauncherArt
{
    public GameLauncherArt(string site, GameArtSource? banner, GameArtSource? cover)
    {
        Site = RequireHttps(site, "site");
        Banner = banner;
        Cover = cover;
    }

    /// <summary>The publisher's page for the game; the launcher's page for the game links to it.</summary>
    public Uri Site { get; }
    public GameArtSource? Banner { get; }
    public GameArtSource? Cover { get; }

    /// <summary>An absolute <c>https</c> address with a host and no user information.</summary>
    internal static Uri RequireHttps(string? value, string key)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? address) || address.Scheme != Uri.UriSchemeHttps
            || address.Host.Length == 0 || address.UserInfo.Length != 0)
            throw new ArgumentException($"{key} '{value}' is not an https address", key);
        return address;
    }
}
