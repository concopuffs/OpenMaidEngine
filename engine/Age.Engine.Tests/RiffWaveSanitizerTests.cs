using System.Buffers.Binary;
using System.Text;
using Age.Engine.Sys4;

public class RiffWaveSanitizerTests
{
    [Fact]
    public void RemovesOnlyInfoListAndPreservesFunctionalChunksAndPadding()
    {
        byte[] fmt = { 1, 0, 1, 0 };
        byte[] info = { (byte)'I', (byte)'N', (byte)'F', (byte)'O',
                        (byte)'I', (byte)'P', (byte)'R', (byte)'D',
                        3, 0, 0, 0, 0x81, 0x45, 0 };
        byte[] sampleLoop = { 9, 8, 7, 6 };
        byte[] data = { 1, 2, 3 };
        byte[] source = Wave(("fmt ", fmt), ("LIST", info), ("smpl", sampleLoop), ("data", data));

        byte[] sanitized = RiffWaveSanitizer.PrepareForGodot(source);

        Assert.NotSame(source, sanitized);
        Assert.Equal(sanitized.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(sanitized.AsSpan(4, 4)));
        Assert.Equal(new[] { "fmt ", "smpl", "data" }, ChunkIds(sanitized));
        Assert.Equal(fmt, Chunk(sanitized, "fmt "));
        Assert.Equal(sampleLoop, Chunk(sanitized, "smpl"));
        Assert.Equal(data, Chunk(sanitized, "data"));
    }

    [Fact]
    public void LeavesWaveWithoutInfoMetadataUnchanged()
    {
        byte[] source = Wave(("fmt ", new byte[] { 1, 0 }), ("data", new byte[] { 1, 2 }));

        Assert.Same(source, RiffWaveSanitizer.PrepareForGodot(source));
    }

    [Fact]
    public void DropsBytesAfterFirstDeclaredRiffExtent()
    {
        byte[] first = Wave(("fmt ", new byte[] { 1, 0 }), ("data", new byte[] { 1, 2, 3, 4 }));
        byte[] second = Wave(("fmt ", new byte[] { 1, 0 }), ("data", new byte[] { 5, 6 }));
        byte[] separator = Encoding.ASCII.GetBytes("Content-Disposition: form-data\r\n\r\n");
        byte[] source = [.. first, .. separator, .. second];

        byte[] sanitized = RiffWaveSanitizer.PrepareForGodot(source);

        Assert.NotSame(source, sanitized);
        Assert.Equal(first, sanitized);
    }

    [Fact]
    public void InstalledSc0000GlowSoundDropsCp932InfoBlockWithoutChangingPcmData()
    {
        var resources = ResourceMap.Load();
        AudioPayload audio = resources.ReadAudio(resources.ResolveSoundEffect(0x28)!);

        byte[] sanitized = RiffWaveSanitizer.PrepareForGodot(audio.Bytes);

        Assert.Equal("E0808.WAV", audio.Name);
        Assert.Equal(688_570, audio.Bytes.Length);
        Assert.Equal(688_336, sanitized.Length);
        Assert.DoesNotContain("LIST", ChunkIds(sanitized));
        Assert.Equal(Chunk(audio.Bytes, "data"), Chunk(sanitized, "data"));
    }

    [Fact]
    public void InstalledFirstBossSoundStopsAtFirstDeclaredRiff()
    {
        var resources = ResourceMap.Load();
        AudioPayload audio = resources.ReadAudio(resources.ResolveSoundEffect(0x125)!);

        byte[] sanitized = RiffWaveSanitizer.PrepareForGodot(audio.Bytes);

        Assert.Equal("A1215.WAV", audio.Name);
        Assert.Equal(323_009, audio.Bytes.Length);
        Assert.Equal(157_940, sanitized.Length);
        Assert.Equal(157_940,
            8 + BinaryPrimitives.ReadInt32LittleEndian(sanitized.AsSpan(4, 4)));
        Assert.Equal(157_896, Chunk(sanitized, "data").Length);
        Assert.Equal(audio.Bytes.AsSpan(0, sanitized.Length).ToArray(), sanitized);
    }

    private static byte[] Wave(params (string Id, byte[] Payload)[] chunks)
    {
        int length = 12 + chunks.Sum(chunk => 8 + chunk.Payload.Length + (chunk.Payload.Length & 1));
        var result = new byte[length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(result, 0);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), length - 8);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(result, 8);
        int offset = 12;
        foreach (var chunk in chunks)
        {
            Encoding.ASCII.GetBytes(chunk.Id).CopyTo(result, offset);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset + 4, 4), chunk.Payload.Length);
            chunk.Payload.CopyTo(result, offset + 8);
            offset += 8 + chunk.Payload.Length + (chunk.Payload.Length & 1);
        }
        return result;
    }

    private static string[] ChunkIds(byte[] wave)
    {
        var ids = new List<string>();
        int offset = 12;
        while (offset < wave.Length)
        {
            ids.Add(Encoding.ASCII.GetString(wave, offset, 4));
            int length = BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(offset + 4, 4));
            offset += 8 + length + (length & 1);
        }
        return ids.ToArray();
    }

    private static byte[] Chunk(byte[] wave, string wanted)
    {
        int offset = 12;
        while (offset < wave.Length)
        {
            string id = Encoding.ASCII.GetString(wave, offset, 4);
            int length = BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(offset + 4, 4));
            if (id == wanted) return wave.AsSpan(offset + 8, length).ToArray();
            offset += 8 + length + (length & 1);
        }
        throw new InvalidDataException($"missing RIFF chunk {wanted}");
    }
}
