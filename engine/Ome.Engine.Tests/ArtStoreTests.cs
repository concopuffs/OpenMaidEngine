using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Ome.Engine.Profiles;
using Ome.Launcher.Core;

/// <summary>
/// Launcher spec §8.7 (launcher plan §5.13, gate 13): a game's art, through a seam for the manifests and the HTTP
/// handler. No test opens a connection, and every image is a synthetic PNG made here: the repository holds no game art.
/// </summary>
public sealed class ArtStoreTests : IDisposable
{
    private const string Game = "kamidori";
    private static readonly byte[] Banner = Png(10, 2), Cover = Png(32, 45);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-art-" + Guid.NewGuid().ToString("N"));
    private readonly OmeHome _home;
    private readonly ScriptedHandler _handler = new();
    private readonly List<string> _log = [];
    private int _decoded;

    public ArtStoreTests()
    {
        _home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A minimal PNG of that size: its signature, its header and one empty data chunk.</summary>
    private static byte[] Png(int width, int height)
    {
        using var file = new MemoryStream();
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a]);
        void Chunk(string type, byte[] body)
        {
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
            file.Write(length);
            file.Write(Encoding.ASCII.GetBytes(type));
            file.Write(body);
            file.Write(new byte[4]); // the CRC: Core's probe reads the header only
        }
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        Chunk("IHDR", header);
        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(new byte[height * (1 + width * 3)]);
        Chunk("IDAT", pixels.ToArray());
        Chunk("IEND", []);
        return file.ToArray();
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>The built-in registry with Kamidori's <c>launcher</c> entry replaced: its images at <c>art.test</c> with
    /// the synthetic files' hashes, unless given.</summary>
    private static GameProfileRegistry Registry(string? bannerHash = null, string bannerUrl = "https://art.test/banner.png")
    {
        GameProfileManifest kamidori = GameProfileRegistry.BuiltIn.Find(Game)!;
        var art = new GameLauncherArt("https://art.test/", new GameArtSource(bannerUrl, bannerHash ?? Sha(Banner)),
                                      new GameArtSource("https://art.test/cover.png", Sha(Cover)));
        return new GameProfileRegistry(GameProfileRegistry.BuiltIn.Profiles.Where(profile => profile.Id != Game).Append(
            new GameProfileManifest(kamidori.Id, kamidori.DisplayTitle, kamidori.CatalogIdentities, kamidori.ScriptRevisions,
                                    kamidori.SysFrontendId, kamidori.EngineBuildId, kamidori.EngineAbiId,
                                    kamidori.NaturalBootScript, kamidori.MetadataReferences, kamidori.Persistence,
                                    kamidori.DirectSceneDiagnostics, kamidori.DebugSceneLaunch, art)));
    }

    private ArtStore Store(GameProfileRegistry? registry = null, ArtLimits? limits = null)
        => new(_home, new ArtOptions(registry ?? Registry(), _handler, Decoder: data =>
        {
            _decoded++;
            return ArtProbe.Header(data);
        }, limits, _log.Add));

    private void Serve(string path, byte[] body, bool length = true)
        => _handler.Routes[path] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = length ? new ByteArrayContent(body) : new StreamContent(new UnknownLengthStream(body)),
        };

    [Fact]
    public void TheManifestsNameThePublishersImagesAndTheReaderRefusesAnythingButHttpsAndAFullHash()
    {
        GameLauncherArt himegari = GameProfileRegistry.BuiltIn.Find("himegari")!.Launcher!;
        Assert.Equal("https://www.eukleia.co.jp/eushully/", himegari.Site.ToString());
        Assert.Equal("https://www.eukleia.co.jp/eushully/eu10/banner_eu10a.jpg", himegari.Banner!.Url.ToString());
        Assert.Equal("3fea5a5c47077be9ed49c46d4a77cd9512c44f32f6279dc5d412e589a342daef", himegari.Cover!.Sha256);
        GameLauncherArt kamidori = GameProfileRegistry.BuiltIn.Find(Game)!.Launcher!;
        Assert.Equal("https://www.eukleia.co.jp/eushully/eu12/image/eu12_package.jpg", kamidori.Cover!.Url.ToString());
        Assert.Equal("962de2e9bda6634d89211b7938a5e517f5c29444aae3586a672eaa58d3f594da", kamidori.Banner!.Sha256);

        Assert.Throws<ArgumentException>(() => new GameArtSource("http://art.test/banner.png", Sha(Banner)));
        Assert.Throws<ArgumentException>(() => new GameArtSource("https://user@art.test/banner.png", Sha(Banner)));
        Assert.Throws<ArgumentException>(() => new GameArtSource("https://art.test/banner.png", Sha(Banner)[..63]));
        Assert.Throws<ArgumentException>(() => new GameArtSource("https://art.test/banner.png", Sha(Banner).ToUpperInvariant()));
        Assert.Throws<ArgumentException>(() => new GameLauncherArt("ftp://art.test/", null, null));

        string json = File.ReadAllText(Path.Combine(Ome.Engine.Sys4.Paths.Repo, "engine", "Ome.Engine", "Profiles", "kamidori.json"));
        GameProfileManifest Read(string text)
            => GameProfileRegistry.Parse(new MemoryStream(Encoding.UTF8.GetBytes(text)), "test.json");
        Assert.Equal(kamidori.Banner.Url, Read(json).Launcher!.Banner!.Url);
        var unknown = Assert.Throws<InvalidDataException>(() => Read(json.Replace("\"site\":", "\"page\": \"x\", \"site\":")));
        Assert.Contains("page", unknown.Message);
        Assert.Throws<InvalidDataException>(() => Read(json.Replace("https://www.eukleia.co.jp/eushully/eu12/banner",
                                                                    "http://www.eukleia.co.jp/eushully/eu12/banner")));
    }

    [Fact]
    public void AnImageWhoseHashMatchesIsStoredAndShownAndNotRequestedAgain()
    {
        Serve("/banner.png", Banner);
        Serve("/cover.png", Cover);
        using (ArtStore store = Store())
        {
            Assert.Equal(ArtOrigin.Card, store.Load(Game, ArtKind.Banner).Origin);
            Assert.Equal(["art.test"], store.MissingHosts([Game, "himegari", Game]).Where(host => host == "art.test"));
            IReadOnlyList<ArtDownload> first = store.DownloadMissingAsync([Game], ArtConsent.Always).Result;
            Assert.All(first, download => Assert.Equal(ArtDownloadStatus.Stored, download.Status));
            Assert.Equal(2, _handler.Requests.Count);
            Assert.All(_handler.Requests, request => Assert.Equal(ArtStore.UserAgent, request.UserAgent));

            ArtImage banner = store.Load(Game, ArtKind.Banner);
            Assert.Equal(ArtOrigin.Downloaded, banner.Origin);
            Assert.Equal(Banner, banner.Data);
            Assert.Equal(new ImageSize(10, 2), banner.Size);
            Assert.Equal(store.CachePath(Game, ArtKind.Banner), banner.Path);
            Assert.StartsWith(_home.ArtCacheDirectory(Game), banner.Path);

            Assert.Equal(ArtDownloadStatus.AlreadyStored, store.DownloadAsync(Game, ArtKind.Banner).Result.Status);
            Assert.Empty(store.DownloadMissingAsync([Game], ArtConsent.Always).Result);
        }
        using (ArtStore restarted = Store())
            Assert.Empty(restarted.DownloadMissingAsync([Game], ArtConsent.Always).Result);
        Assert.Equal(2, _handler.Requests.Count);

        // The stored file's hash is checked on each load: a changed file is removed, and the card shows.
        File.WriteAllBytes(Path.Combine(_home.ArtCacheDirectory(Game), "banner.png"), Png(10, 3));
        using (ArtStore changed = Store())
        {
            Assert.Equal(ArtOrigin.Card, changed.Load(Game, ArtKind.Banner).Origin);
            Assert.False(File.Exists(changed.CachePath(Game, ArtKind.Banner)));
        }
    }

    [Fact]
    public void AWrongHashAnOversizedBodyAndAFileThatIsNotAnImageLeaveTheCardAndStoreNothing()
    {
        byte[] notAnImage = Encoding.ASCII.GetBytes("<html>moved</html>");
        Serve("/wrong.png", Png(10, 2).Concat(new byte[] { 0 }).ToArray());
        Serve("/large.png", Banner, length: false);
        Serve("/large-declared.png", Banner);
        Serve("/page.png", notAnImage);
        var limited = new ArtLimits(DownloadBytes: Banner.Length - 1);

        ArtDownload Fetch(GameProfileRegistry registry, ArtLimits? limits = null)
        {
            using ArtStore store = Store(registry, limits);
            ArtDownload download = store.DownloadAsync(Game, ArtKind.Banner).Result;
            Assert.Equal(ArtOrigin.Card, store.Load(Game, ArtKind.Banner).Origin);
            return download;
        }

        ArtDownload wrong = Fetch(Registry(bannerUrl: "https://art.test/wrong.png"));
        Assert.Equal(ArtDownloadStatus.Failed, wrong.Status);
        Assert.Contains("not the manifest's", wrong.Problem);
        Assert.Equal(0, _decoded);

        Assert.Contains("over", Fetch(Registry(bannerUrl: "https://art.test/large.png"), limited).Problem);
        Assert.Contains("over", Fetch(Registry(bannerUrl: "https://art.test/large-declared.png"), limited).Problem);
        Assert.Equal(0, _decoded);

        ArtDownload page = Fetch(Registry(Sha(notAnImage), "https://art.test/page.png"));
        Assert.Contains("not an image", page.Problem);
        Assert.Equal(1, _decoded);

        Assert.False(Directory.Exists(_home.ArtCacheDirectory(Game)) &&
                     Directory.EnumerateFileSystemEntries(_home.ArtCacheDirectory(Game)).Any());
        Assert.Equal(4, _log.Count(line => line.StartsWith("art: kamidori's banner from https://art.test/")));
    }

    [Fact]
    public void ARedirectIsRefusedAndWithNeverOrBeforeAnAnswerNoRequestIsMade()
    {
        _handler.Routes["/banner.png"] = () => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("https://elsewhere.test/banner.png") },
        };
        Serve("/cover.png", Cover);
        using ArtStore store = Store();

        Assert.Empty(store.DownloadMissingAsync([Game], ArtConsent.Never).Result);
        Assert.Empty(store.DownloadMissingAsync([Game], ArtConsent.Ask).Result);
        Assert.Empty(_handler.Requests);

        ArtDownload redirect = store.DownloadAsync(Game, ArtKind.Banner).Result;
        Assert.Equal(ArtDownloadStatus.Failed, redirect.Status);
        Assert.Contains("redirect", redirect.Problem);
        Assert.Equal(["/banner.png"], _handler.Requests.Select(request => request.Path));

        // With always, the failed banner is tried once per start, and the cover is stored.
        Assert.Equal(2, store.DownloadMissingAsync([Game], ArtConsent.Always).Result.Count);
        Assert.Empty(store.DownloadMissingAsync([Game], ArtConsent.Always).Result);
        Assert.Equal(3, _handler.Requests.Count);
        Assert.Equal(ArtOrigin.Downloaded, store.Load(Game, ArtKind.Cover).Origin);

        store.RemoveDownloaded(Game);
        Assert.False(Directory.Exists(_home.ArtCacheDirectory(Game)));
    }

    [Fact]
    public void AProfilesOwnCoverAndThePlayersImageEachWinOverTheDownload()
    {
        Serve("/banner.png", Banner);
        Serve("/cover.png", Cover);
        using ArtStore store = Store(limits: new ArtLimits(LocalBytes: 4096, Side: 64));
        Assert.Equal(2, store.DownloadMissingAsync([Game], ArtConsent.Always).Result.Count);

        string art = _home.ArtDirectory(Game);
        Directory.CreateDirectory(art);
        byte[] players = Png(20, 4);
        File.WriteAllBytes(Path.Combine(art, "banner.png"), players);
        ArtImage banner = store.Load(Game, ArtKind.Banner);
        Assert.Equal(ArtOrigin.Player, banner.Origin);
        Assert.Equal(players, banner.Data);
        Assert.False(store.IsMissing(Game, ArtKind.Banner));

        // Local art has limits: a file too large, and an image too wide, are skipped.
        File.WriteAllBytes(Path.Combine(art, "banner.png"), Png(65, 4));
        Assert.Equal(ArtOrigin.Downloaded, store.Load(Game, ArtKind.Banner).Origin);
        File.WriteAllBytes(Path.Combine(art, "banner.png"), new byte[4097]);
        Assert.Equal(ArtOrigin.Downloaded, store.Load(Game, ArtKind.Banner).Origin);

        File.WriteAllBytes(Path.Combine(art, "cover.jpg"), Png(32, 45));
        Assert.Equal(ArtOrigin.Player, store.Load(Game, ArtKind.Cover).Origin);

        string profileRoot = Path.Combine(_root, "profile");
        Directory.CreateDirectory(profileRoot);
        string own = Path.Combine(profileRoot, "mine.png");
        File.WriteAllBytes(own, Png(16, 22));
        ProfileEntry profile = Profile(profileRoot, own);
        ArtImage cover = store.Load(Game, ArtKind.Cover, profile);
        Assert.Equal(ArtOrigin.Profile, cover.Origin);
        Assert.Equal(new ImageSize(16, 22), cover.Size);

        // A cover reached through a link inside the root is read: a root may hold links (the user's decision of
        // 2026-11-16, launcher plan §9.7).
        string outside = Path.Combine(_root, "outside"), linked = Path.Combine(profileRoot, "linked");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "far.png"), Png(16, 22));
        Ome.Engine.Tests.ProfileStoreTests.Junction(linked, outside);
        try
        {
            Assert.Equal(ArtOrigin.Profile, store.Load(Game, ArtKind.Cover, Profile(profileRoot, Path.Combine(linked, "far.png"))).Origin);
        }
        finally
        {
            Directory.Delete(linked);
        }

        // A root not yet reviewed is not read, and the banner has no profile image.
        Assert.Equal(ArtOrigin.Player, store.Load(Game, ArtKind.Cover, profile with { NeedsReview = true }).Origin);
        Assert.Equal(ArtOrigin.Downloaded, store.Load(Game, ArtKind.Banner, profile).Origin);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public void TheCardIsTheTitleOnAColourFromTheIdAndTheProbeReadsPngAndJpegHeaders()
    {
        using ArtStore store = Store();
        ArtImage card = store.Load(Game, ArtKind.Cover);
        Assert.Equal(ArtOrigin.Card, card.Origin);
        Assert.Null(card.Data);
        Assert.Equal("Kamidori Alchemy Meister", card.Card.Title);
        Assert.Equal("神採りアルケミーマイスター", card.Card.OriginalTitle);
        Assert.Equal(card.Card, store.Card(Game));
        Assert.NotEqual(store.Card("himegari").Rgb, card.Card.Rgb);
        Assert.Equal("unknown", store.Card("unknown").Title);

        Assert.Equal(new ImageSize(32, 45), ArtProbe.Header(Cover));
        byte[] jpeg = [0xff, 0xd8, 0xff, 0xe0, 0x00, 0x04, 0x00, 0x00, 0xff, 0xc0, 0x00, 0x11, 0x08, 0x01, 0xc2, 0x01, 0x40];
        Assert.Equal(new ImageSize(320, 450), ArtProbe.Header(jpeg));
        Assert.Null(ArtProbe.Header(jpeg[..12]));
        Assert.Null(ArtProbe.Header(Encoding.ASCII.GetBytes("GIF89a")));
    }

    private ProfileEntry Profile(string root, string cover)
    {
        var document = ProfileDocument.New("Art", Game, Path.Combine(_root, "install"), null, _home) with
        {
            Launcher = ProfileLauncherTable.Defaults with { Cover = cover },
        };
        return new ProfileEntry("art", root, true, new LoadedProfile(root, null!, document, []), null);
    }

    /// <summary>Answers each request by its path; one with no route is a test failure.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new(StringComparer.Ordinal);
        public List<(string Path, string UserAgent)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, request.Headers.UserAgent.ToString()));
            Assert.Equal("art.test", request.RequestUri.Host);
            Assert.True(Routes.TryGetValue(request.RequestUri.AbsolutePath, out var answer),
                        $"no route for {request.RequestUri}");
            return Task.FromResult(answer!());
        }
    }

    /// <summary>A body whose length is not known before it arrives.</summary>
    private sealed class UnknownLengthStream(byte[] body) : MemoryStream(body)
    {
        public override bool CanSeek => false;
    }
}
