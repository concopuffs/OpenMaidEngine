using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using Ome.Engine.Profiles;

namespace Ome.Launcher.Core;

/// <summary>A game's two images (launcher spec §8.7): the banner (5 : 1) and the cover (32 : 45).</summary>
public enum ArtKind { Banner, Cover }

/// <summary>Where a shown image came from, in the order the launcher takes them.</summary>
public enum ArtOrigin { Profile, Player, Downloaded, Card }

/// <summary>An image's width and height in pixels.</summary>
public readonly record struct ImageSize(int Width, int Height);

/// <summary>
/// Decodes an image far enough to say whether it is one, and its size; null when it is not. Core's own,
/// <see cref="ArtProbe.Header"/>, reads a PNG or JPEG header; the window passes its image decoder (launcher plan M3), so
/// only a file that decodes is kept.
/// </summary>
public delegate ImageSize? ArtDecoder(ReadOnlySpan<byte> data);

/// <summary>The generated card, drawn when no image is found (spec §8.7): the game's title on a colour derived from its
/// id. <paramref name="Rgb"/> is <c>0xRRGGBB</c>.</summary>
public sealed record ArtCard(string Game, string Title, string? OriginalTitle, uint Rgb);

/// <summary>The image to show: its bytes as read and checked, or none (<see cref="ArtOrigin.Card"/>). The card is always
/// given, for a window whose decoder refuses the bytes after all.</summary>
public sealed record ArtImage(ArtKind Kind, ArtOrigin Origin, string? Path, byte[]? Data, ImageSize? Size, ArtCard Card);

public enum ArtDownloadStatus { Stored, AlreadyStored, NoAddress, Failed }

/// <summary>What one download did, and why it did not store a file.</summary>
public sealed record ArtDownload(string Game, ArtKind Kind, ArtDownloadStatus Status, string? Problem = null)
{
    public bool Requested { get; init; }
}

/// <summary>The limits on what art is read (spec §8.7): a local file's size, a download's size as its body arrives, and
/// an image's width and height.</summary>
public sealed record ArtLimits(long LocalBytes = 8 * 1024 * 1024, long DownloadBytes = 2 * 1024 * 1024, int Side = 4096)
{
    public static ArtLimits Defaults { get; } = new();
}

/// <summary>The seams of an <see cref="ArtStore"/>: the games (the built-in manifests by default; only a built-in
/// manifest names an address, spec §12), the HTTP handler, the decoder, the limits, and where a line of the launcher's
/// log goes.</summary>
public sealed record ArtOptions(GameProfileRegistry? Registry = null, HttpMessageHandler? Handler = null,
                                ArtDecoder? Decoder = null, ArtLimits? Limits = null, Action<string>? Log = null,
                                TimeSpan? Timeout = null);

/// <summary>
/// A game's art for one launcher run (launcher spec §8.7). For each image it takes the first of: the profile's own
/// cover (covers only), the player's image in <c>&lt;OME home&gt;/art/&lt;game&gt;/</c>, the downloaded file in
/// <c>cache/art/&lt;game&gt;/</c> (its hash checked again on each load), and the generated card. A download happens only
/// on consent, once per image: <c>https</c> only, no redirect, the size limit enforced as the body arrives, and the
/// SHA-256 checked before any byte reaches the decoder. A failure leaves the card and one line in the log; nothing
/// throws for art.
/// </summary>
public sealed class ArtStore : IDisposable
{
    /// <summary>The user agent of a download: generic, naming no player and no machine.</summary>
    public const string UserAgent = "OpenMaidEngine-Launcher";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    private readonly OmeHome _home;
    private readonly GameProfileRegistry _registry;
    private readonly ArtDecoder _decoder;
    private readonly ArtLimits _limits;
    private readonly Action<string> _log;
    private readonly HttpClient _client;
    private readonly HashSet<(string Game, ArtKind Kind)> _tried = [];

    public ArtStore(OmeHome home, ArtOptions? options = null)
    {
        options ??= new ArtOptions();
        _home = home;
        _registry = options.Registry ?? GameProfileRegistry.BuiltIn;
        _decoder = options.Decoder ?? ArtProbe.Header;
        _limits = options.Limits ?? ArtLimits.Defaults;
        _log = options.Log ?? (_ => { });
        TimeSpan timeout = options.Timeout ?? DefaultTimeout;
        HttpMessageHandler handler = options.Handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = timeout,
        };
        _client = new HttpClient(handler, disposeHandler: options.Handler == null) { Timeout = timeout };
    }

    public void Dispose() => _client.Dispose();

    /// <summary>The image to show for <paramref name="game"/>; for a cover, <paramref name="profile"/>'s own first. A
    /// profile root not yet reviewed is not read (spec §12).</summary>
    public ArtImage Load(string game, ArtKind kind, ProfileEntry? profile = null)
    {
        ArtCard card = Card(game);
        if (kind == ArtKind.Cover && profile is { Usable: true, NeedsReview: false }
            && profile.Loaded!.Document.Launcher.Cover is { } cover)
        {
            if (Local(cover) is { } own) return new ArtImage(kind, ArtOrigin.Profile, cover, own.Data, own.Size, card);
        }
        foreach (string file in PlayerFiles(game, kind))
        {
            if (Local(file) is { } mine) return new ArtImage(kind, ArtOrigin.Player, file, mine.Data, mine.Size, card);
        }
        if (Downloaded(game, kind) is { } stored)
            return new ArtImage(kind, ArtOrigin.Downloaded, stored.Path, stored.Data, stored.Size, card);
        return new ArtImage(kind, ArtOrigin.Card, null, null, null, card);
    }

    /// <summary>The generated card: the manifest's title, the install's title beside it, and a colour from the id.</summary>
    public ArtCard Card(string game)
    {
        GameProfileManifest? manifest = _registry.Find(game);
        string title = manifest?.DisplayTitle ?? game;
        string? original = manifest?.CatalogIdentities.FirstOrDefault()?.Title;
        return new ArtCard(game, title, original == title ? null : original, Colour(game));
    }

    /// <summary>The address a manifest names for an image, or null.</summary>
    public GameArtSource? Source(string game, ArtKind kind)
    {
        GameLauncherArt? art = _registry.Find(game)?.Launcher;
        return kind == ArtKind.Banner ? art?.Banner : art?.Cover;
    }

    /// <summary>The publisher's page for <paramref name="game"/>, which the games page links to.</summary>
    public Uri? Site(string game) => _registry.Find(game)?.Launcher?.Site;

    /// <summary>Whether a download would be the image shown: the manifest names an address, and neither the player's
    /// image nor a stored download is there.</summary>
    public bool IsMissing(string game, ArtKind kind)
        => Source(game, kind) != null && !PlayerFiles(game, kind).Any(file => Local(file) != null)
           && Downloaded(game, kind) == null;

    /// <summary>The hosts downloads for <paramref name="games"/>' missing images would contact; with <c>ask</c>, the
    /// launcher names them when it asks (spec §8.7). Empty when nothing is missing.</summary>
    public IReadOnlyList<string> MissingHosts(IEnumerable<string> games)
        => games.Distinct(StringComparer.Ordinal)
            .SelectMany(game => Enum.GetValues<ArtKind>().Where(kind => IsMissing(game, kind))
                .Select(kind => Source(game, kind)!.Url.Host))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();

    /// <summary>The cached file of an image, whether it is there or not.</summary>
    public string CachePath(string game, ArtKind kind)
    {
        string extension = Path.GetExtension(Source(game, kind)?.Url.AbsolutePath ?? "").ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg")) extension = ".img";
        return Path.Combine(_home.ArtCacheDirectory(game), Name(kind) + extension);
    }

    /// <summary>The automatic downloads of one launcher start: with <c>always</c> only, each missing image of
    /// <paramref name="games"/> once per start. With <c>ask</c> (not yet answered) and <c>never</c>, no request.</summary>
    public async Task<IReadOnlyList<ArtDownload>> DownloadMissingAsync(IEnumerable<string> games, ArtConsent consent,
                                                                      CancellationToken cancel = default)
    {
        var results = new List<ArtDownload>();
        if (consent != ArtConsent.Always) return results;
        foreach (string game in games.Distinct(StringComparer.Ordinal))
            foreach (ArtKind kind in Enum.GetValues<ArtKind>())
            {
                if (!IsMissing(game, kind) || !_tried.Add((game, kind))) continue;
                results.Add(await DownloadAsync(game, kind, cancel).ConfigureAwait(false));
            }
        return results;
    }

    /// <summary>Downloads one image now: the games page's action, or a download the player consented to. A stored image
    /// is not requested again.</summary>
    public async Task<ArtDownload> DownloadAsync(string game, ArtKind kind, CancellationToken cancel = default)
    {
        GameArtSource? source = Source(game, kind);
        if (source == null) return new ArtDownload(game, kind, ArtDownloadStatus.NoAddress, "the game's manifest names no address");
        if (Downloaded(game, kind) != null) return new ArtDownload(game, kind, ArtDownloadStatus.AlreadyStored);
        string what = $"{game}'s {Name(kind)} from {source.Url}";
        ArtDownload Failed(string problem, bool requested = true)
        {
            _log($"art: {what} is not stored: {problem}");
            return new ArtDownload(game, kind, ArtDownloadStatus.Failed, problem) { Requested = requested };
        }
        if (source.Url.Scheme != Uri.UriSchemeHttps) return Failed("the address is not https", requested: false);

        byte[] body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using HttpResponseMessage response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status is >= 300 and < 400) return Failed($"the server answered {status}, a redirect, which is refused");
            if (!response.IsSuccessStatusCode) return Failed($"the server answered {status}");
            if (response.Content.Headers.ContentLength > _limits.DownloadBytes)
                return Failed($"its size, {response.Content.Headers.ContentLength} bytes, is over {_limits.DownloadBytes}");
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            body = await ReadUpTo(stream, _limits.DownloadBytes, cancel).ConfigureAwait(false)
                   ?? throw new InvalidDataException($"its body is over {_limits.DownloadBytes} bytes");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException
                                          or InvalidDataException)
        {
            return Failed(error is OperationCanceledException ? "it timed out" : error.Message);
        }

        string hash = Hex(SHA256.HashData(body));
        if (hash != source.Sha256) return Failed($"its SHA-256 is {hash}, not the manifest's {source.Sha256}");
        if (Decode(body) is not { } size) return Failed("it is not an image the launcher decodes, or it is too large");

        string path = CachePath(game, kind);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string partial = path + ".partial-" + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(partial, body, cancel).ConfigureAwait(false);
            File.Move(partial, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Failed($"it could not be written to {path}: {error.Message}");
        }
        _log($"art: stored {what} ({size.Width}×{size.Height}) in {path}");
        return new ArtDownload(game, kind, ArtDownloadStatus.Stored) { Requested = true };
    }

    /// <summary>Removes what was downloaded for <paramref name="game"/>, which is safe at any time.</summary>
    public void RemoveDownloaded(string game)
    {
        string folder = _home.ArtCacheDirectory(game);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private (string Path, byte[] Data, ImageSize Size)? Downloaded(string game, ArtKind kind)
    {
        GameArtSource? source = Source(game, kind);
        string path = CachePath(game, kind);
        if (source == null || !File.Exists(path)) return null;
        byte[]? data = ReadFile(path, _limits.DownloadBytes);
        string? problem = data == null ? $"it is over {_limits.DownloadBytes} bytes or cannot be read"
            : Hex(SHA256.HashData(data)) != source.Sha256 ? "its SHA-256 is not the manifest's"
            : null;
        ImageSize? size = problem == null ? Decode(data!) : null;
        if (problem == null && size == null) problem = "it does not decode";
        if (problem == null) return (path, data!, size!.Value);
        _log($"art: the stored {Name(kind)} of {game}, {path}, is removed: {problem}");
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log($"art: {path} could not be removed: {error.Message}");
        }
        return null;
    }

    /// <summary>The player's files for an image, <c>banner.*</c> or <c>cover.*</c>, in name order.</summary>
    private IEnumerable<string> PlayerFiles(string game, ArtKind kind)
    {
        string folder = _home.ArtDirectory(game);
        if (!Directory.Exists(folder)) return [];
        string name = Name(kind);
        return Directory.EnumerateFiles(folder)
            .Where(file => string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>A local image within the limits that decodes. A profile's cover lies inside its root by its path
    /// (<c>profile.toml</c>'s reader refuses one that does not); a link on the way is allowed, as anywhere in a root
    /// (the user's decision of 2026-11-16, launcher plan §9.7).</summary>
    private (byte[] Data, ImageSize Size)? Local(string path)
    {
        if (!File.Exists(path)) return null;
        byte[]? data = ReadFile(path, _limits.LocalBytes);
        if (data == null)
        {
            _log($"art: {path} is not read: it is over {_limits.LocalBytes} bytes or cannot be read");
            return null;
        }
        if (Decode(data) is { } size) return (data, size);
        _log($"art: {path} is not used: it is not an image the launcher decodes, or it is over {_limits.Side} pixels a side");
        return null;
    }

    private ImageSize? Decode(byte[] data)
        => _decoder(data) is { Width: > 0, Height: > 0 } size && size.Width <= _limits.Side && size.Height <= _limits.Side
            ? size
            : null;

    private static byte[]? ReadFile(string path, long limit)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > limit) return null;
            var data = new byte[stream.Length];
            stream.ReadExactly(data);
            return data;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The body, or null as soon as it passes <paramref name="limit"/>.</summary>
    private static async Task<byte[]?> ReadUpTo(Stream stream, long limit, CancellationToken cancel)
    {
        using var body = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
        {
            if (body.Length + read > limit) return null;
            body.Write(buffer, 0, read);
        }
        return body.ToArray();
    }

    private static string Name(ArtKind kind) => kind == ArtKind.Banner ? "banner" : "cover";

    private static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    /// <summary>A colour from the id: the hue from its SHA-256, a fixed saturation and a lightness dark enough for a
    /// light title.</summary>
    private static uint Colour(string game)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(game));
        double hue = BinaryPrimitives.ReadUInt16BigEndian(hash) % 360 / 60.0;
        const double saturation = 0.45, lightness = 0.32;
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double x = chroma * (1 - Math.Abs(hue % 2 - 1));
        (double r, double g, double b) = (int)hue switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        double m = lightness - chroma / 2;
        uint Channel(double value) => (uint)Math.Round((value + m) * 255);
        return Channel(r) << 16 | Channel(g) << 8 | Channel(b);
    }
}

/// <summary>Core's own image check (spec §8.7): a PNG's or a JPEG's size from its header. The window's decoder replaces
/// it (launcher plan M3).</summary>
public static class ArtProbe
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a];

    public static ImageSize? Header(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(PngSignature))
        {
            if (data.Length < 24 || !data[12..16].SequenceEqual("IHDR"u8)) return null;
            return Size(BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]));
        }
        if (data.Length < 4 || data[0] != 0xff || data[1] != 0xd8) return null;
        int at = 2;
        while (at + 4 <= data.Length)
        {
            if (data[at] != 0xff) return null;
            byte marker = data[at + 1];
            if (marker == 0xff)
            {
                at++;
                continue;
            }
            if (marker is 0x01 or >= 0xd0 and <= 0xd8)
            {
                at += 2;
                continue;
            }
            int length = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
            if (length < 2) return null;
            bool frame = marker is >= 0xc0 and <= 0xcf and not (0xc4 or 0xc8 or 0xcc);
            if (frame)
            {
                if (at + 9 > data.Length) return null;
                return Size(BinaryPrimitives.ReadUInt16BigEndian(data[(at + 7)..]),
                            BinaryPrimitives.ReadUInt16BigEndian(data[(at + 5)..]));
            }
            if (marker is 0xd9 or 0xda) return null;
            at += 2 + length;
        }
        return null;
    }

    private static ImageSize? Size(int width, int height) => width > 0 && height > 0 ? new ImageSize(width, height) : null;
}
