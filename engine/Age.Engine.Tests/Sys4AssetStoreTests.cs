using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using Age.Engine.Sys4;
using Xunit;

public class Sys4AssetStoreTests
{
    [Fact]
    public void InstalledAppendCatalogHasNativePackSelectionAndStableDirectory()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var append = Assert.Single(catalog.AppendPacks).Value;

        Assert.Equal(1, append.PackId);
        Assert.Equal("S4AC422 ", append.Magic);
        Assert.Equal(catalog.Title, append.Title);
        Assert.Equal(new[] { "APPEND01.ALF" }, append.Archives);
        Assert.Equal(81, append.RawSlots.Count);
        Assert.Equal(81, append.Files.Count);
        Assert.All(append.Files, entry =>
        {
            Assert.Equal(1, entry.PackId);
            Assert.StartsWith("$1$", entry.Name);
            Assert.Equal("APPEND01.ALF", entry.Archive);
        });

        Assert.Same(append.RawSlots[0], catalog.ResolvePacked(0x01000000));
        Assert.Same(append.RawSlots[^1], catalog.ResolvePacked(0x01000050));
        Assert.Null(catalog.ResolvePacked(0x02000000));
        Assert.Null(catalog.ResolvePacked(0x80000000));
        Assert.Null(catalog.ResolveName(append.RawSlots[0].Name));

        string directory = string.Join("\n", append.RawSlots.Select(e =>
            $"{e.RawIndex}|{e.Name}|{e.ArchiveId}|{e.Archive}|{e.FileNumber}|{e.Offset}|{e.Size}"));
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory)));
        Assert.Equal("23F0C104A45C099CEFB7D333362716EDE6F20B9EC53E4C3705A8E3A87063708E", digest);
    }

    [Fact]
    public void CompleteAppendDirectoryAndPayloadsMatchBinExtractAlf()
    {
        string temp = Path.Combine(Path.GetTempPath(), "age-vfsb-oracle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var start = new ProcessStartInfo(Paths.BinExtractAlf)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(Paths.Append01Aai);
            start.ArgumentList.Add(temp);
            using var process = Process.Start(start)!;
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, stdout + stderr);

            var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
            var append = catalog.AppendPacks[1];
            var store = new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
            string output = Path.Combine(temp, "APPEND01");
            var oracleNames = Directory.EnumerateFiles(output).Select(Path.GetFileName)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var actualNames = append.Files.Select(e => e.Name)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.Equal(actualNames, oracleNames);

            long archiveLength = new FileInfo(Path.Combine(Paths.GameDir, "APPEND01.ALF")).Length;
            foreach (var entry in append.Files)
            {
                Assert.InRange(entry.Offset, 0, archiveLength);
                Assert.InRange(entry.Size, 0, archiveLength - entry.Offset);
                byte[] oracle = File.ReadAllBytes(Path.Combine(output, entry.Name));
                Assert.Equal(entry.Size, oracle.LongLength);
                Assert.Equal(oracle, store.ReadAll(entry));
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void RuntimeCatalogMatchesDiagnosticCatalogAndSceneViews()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        using var index = JsonDocument.Parse(File.ReadAllText(Paths.AssetIndexJson));
        var expected = index.RootElement;

        Assert.Equal(expected.GetProperty("magic").GetString(), catalog.Magic);
        Assert.Equal(expected.GetProperty("file_count").GetInt32(), catalog.RawSlots.Count);
        Assert.Equal(expected.GetProperty("entry_count").GetInt32(), catalog.Files.Count);
        Assert.Equal(13208, catalog.RawSlots.Count);
        Assert.Equal(13206, catalog.Files.Count);
        Assert.Equal(2, catalog.RawSlots.Count(r => r.IsPlaceholder));
        Assert.Equal(expected.GetProperty("archives").EnumerateArray().Select(a => a.GetString()), catalog.Archives);

        var jsonFiles = expected.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(jsonFiles.Length, catalog.Files.Count);
        for (int i = 0; i < jsonFiles.Length; i++)
        {
            var j = jsonFiles[i];
            var actual = catalog.Files[i];
            Assert.Equal(j.GetProperty("raw_index").GetInt32(), actual.RawIndex);
            Assert.Equal(j.GetProperty("name").GetString(), actual.Name);
            Assert.Equal(j.GetProperty("archive").GetString(), actual.Archive);
            Assert.Equal(j.GetProperty("arc_id").GetInt32(), actual.ArchiveId);
            Assert.Equal(j.GetProperty("file_number").GetInt32(), actual.FileNumber);
            Assert.Equal(j.GetProperty("offset").GetInt64(), actual.Offset);
            Assert.Equal(j.GetProperty("size").GetInt64(), actual.Size);
        }

        Assert.Equal("SO001.AGF", catalog.ResolveRaw(0x337e)?.Name);
        Assert.Equal("BGM005.OGG", catalog.ResolveName("bgm005.ogg")?.Name);
        Assert.Null(catalog.ResolveRaw(-1));
        Assert.Null(catalog.ResolveRaw(catalog.RawSlots.Count));

        using var sections = JsonDocument.Parse(File.ReadAllText(Paths.AssetSectionsJson));
        foreach (var scene in sections.RootElement.GetProperty("scene_base").EnumerateObject())
        {
            int start = scene.Value.GetInt32();
            int end = start;
            while (end + 1 < catalog.Files.Count
                   && catalog.Files[end + 1].FileNumber > catalog.Files[end].FileNumber) end++;
            for (int i = start; i <= end; i++)
                Assert.Same(catalog.Files[i], catalog.ResolveScene(scene.Name, i - start));
            Assert.Null(catalog.ResolveScene(scene.Name, -1));
            Assert.Null(catalog.ResolveScene(scene.Name, end - start + 1));
        }
    }

    [Fact]
    public void EveryCatalogRangeFitsAndRepresentativePayloadsMatchExtractedData()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        foreach (var entry in catalog.Files)
        {
            long archiveLength = new FileInfo(Path.Combine(Paths.GameDir, entry.Archive)).Length;
            Assert.InRange(entry.Offset, 0, archiveLength);
            Assert.InRange(entry.Size, 0, archiveLength - entry.Offset);
        }

        var store = new Sys4AssetStore(catalog, Paths.GameDir);
        var samples = catalog.Archives.SelectMany(archive =>
        {
            var entries = catalog.Files.Where(e => e.Archive.Equals(archive, StringComparison.OrdinalIgnoreCase)).ToArray();
            return new[] { entries[0], entries[entries.Length / 2], entries[^1] };
        }).Concat(new[]
        {
            catalog.ResolveName("MENU.BIN")!,
            catalog.ResolveName("SO001.AGF")!,
            catalog.ResolveName("BGM005.OGG")!,
        }).DistinctBy(e => e.RawIndex);

        foreach (var entry in samples)
        {
            string folder = Path.GetFileNameWithoutExtension(entry.Archive);
            string extracted = Path.Combine(Paths.Extracted, folder, entry.Name);
            Assert.True(File.Exists(extracted), $"missing extracted oracle: {folder}/{entry.Name}");
            Assert.Equal(File.ReadAllBytes(extracted), store.ReadAll(entry));
        }
    }

    [Fact]
    public void Sc0000RoomAndTitleAudioPayloadsReadDirectlyFromTheirNativeAddressSpaces()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var archiveOnly = new Sys4AssetStore(catalog, Paths.GameDir);
        var resources = new ResourceMap(catalog, archiveOnly);

        var bgm = resources.ResolveBgm(5);
        Assert.Equal("BGM005.OGG", bgm?.Name);
        AssertOgg(resources.ReadAudio(bgm!));

        var voice = resources.ResolveVoice("SC0000", 0x24);
        Assert.Equal("MAN999.OGG", voice?.Name);
        AssertOgg(resources.ReadAudio(voice!));

        var roomVoice = resources.ResolveVoice("ROOM", 0x3365);
        Assert.Equal("EUA0016.OGG", roomVoice?.Name);
        AssertOgg(resources.ReadAudio(roomVoice!));
        Assert.Null(resources.ResolveVoice("ROOM", 0x337e)); // SO001.AGF is not voice audio.

        var sfx = resources.ResolveSoundEffect(0x28);
        Assert.Equal("E0808.WAV", sfx?.Name);
        var wav = resources.ReadAudio(sfx!);
        Assert.Equal("E0808.WAV", wav.Name);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav.Bytes, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav.Bytes, 8, 4));

        Assert.Null(resources.Resolve("TITLE", 0x2aea));
        Assert.Equal("SE020.WAV", resources.ResolveSoundEffect(0x2aea)?.Name);
        Assert.Equal("SE013.WAV", resources.ResolveSoundEffect(0x2aeb)?.Name);
        Assert.Equal("SE015.WAV", resources.ResolveSoundEffect(0x3321)?.Name);
        Assert.Null(resources.ResolveSoundEffect(0x337e)); // SO001.AGF is not audio.
        Assert.Null(resources.ResolveSoundEffect(0x02000000)); // unmounted append selector.

        Assert.Throws<InvalidDataException>(() => resources.ReadAudio(catalog.ResolveName("SO001.AGF")!));
    }

    [Fact]
    public void AllInstalledLooseScriptOverridesShadowArchiveCopies()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var archiveOnly = new Sys4AssetStore(catalog, Paths.GameDir);
        var looseFirst = new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
        var rootBins = Directory.EnumerateFiles(Paths.GameDir, "*.BIN")
            .Where(path => catalog.ResolveName(Path.GetFileName(path)) is { } entry
                           && entry.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();

        // This installed v1.03 tree currently has 49 archive-backed overrides plus the two root-only
        // engine catalogs SYS4AB.BIN/SYS4INI.BIN. Exercise every archive-backed override, not a sample.
        Assert.Equal(49, rootBins.Length);
        foreach (string path in rootBins)
        {
            var entry = catalog.ResolveName(Path.GetFileName(path))!;
            byte[] loose = File.ReadAllBytes(path);
            Assert.Equal(loose, looseFirst.ReadAll(entry));
            Assert.NotEqual(loose, archiveOnly.ReadAll(entry));
        }
    }

    [Fact]
    public async Task SyntheticStoreIsBoundedLooseFirstThreadSafeAndRejectsTraversal()
    {
        string temp = Path.Combine(Path.GetTempPath(), "age-vfs-" + Guid.NewGuid().ToString("N"));
        string archives = Path.Combine(temp, "archives"), loose = Path.Combine(temp, "loose");
        Directory.CreateDirectory(archives);
        Directory.CreateDirectory(loose);
        try
        {
            File.WriteAllBytes(Path.Combine(archives, "DATA1.ALF"), new byte[] { 9, 8, 1, 2, 3, 7 });
            var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
            var entry = new AssetEntry("TEST.BIN", "DATA1.ALF", 2, 3);
            var store = new Sys4AssetStore(catalog, archives, loose);

            using (var stream = store.Open(entry))
            {
                Assert.Equal(3, stream.Length);
                Assert.Equal(new byte[] { 1, 2, 3 }, ReadToEnd(stream));
                Assert.Equal(-1, stream.ReadByte());
                Assert.Throws<IOException>(() => stream.Seek(1, SeekOrigin.End));
            }

            File.WriteAllBytes(Path.Combine(loose, "TEST.BIN"), new byte[] { 4, 5 });
            Assert.Equal(new byte[] { 4, 5 }, store.ReadAll(entry));
            File.Delete(Path.Combine(loose, "TEST.BIN"));
            Assert.Equal(new byte[] { 1, 2, 3 }, store.ReadAll(entry));

            var reads = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.ReadAll(entry))));
            Assert.All(reads, bytes => Assert.Equal(new byte[] { 1, 2, 3 }, bytes));
            Assert.Throws<InvalidDataException>(() => store.Open(entry with { Name = "../TEST.BIN" }));
            Assert.Throws<InvalidDataException>(() => store.Open(entry with { Archive = "../DATA1.ALF" }));
            Assert.Throws<InvalidDataException>(() => store.Open(entry with { Offset = 5, Size = 2 }));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static byte[] ReadToEnd(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static void AssertOgg(AudioPayload payload)
    {
        Assert.EndsWith(".OGG", payload.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("OggS", Encoding.ASCII.GetString(payload.Bytes, 0, 4));
    }
}
