using System.Diagnostics;
using Age.Engine.Sys4;

internal sealed record MovieCorpusInput(long PackedId, string Name, Func<MoviePayload> ReadPayload);

internal sealed record MovieCorpusItemResult(
    long PackedId,
    string PackedIdHex,
    string Name,
    long PayloadBytes,
    int ExpectedWidth,
    int ExpectedHeight,
    int Width,
    int Height,
    long StopTimeMs,
    int FrameRateNumerator,
    int FrameRateDenominator,
    bool HasAudio,
    int AudioSampleRate,
    int AudioChannels,
    long AudioBlockCount,
    long AudioFrameCount,
    long FirstAudioPresentationTimeMs,
    long LastAudioPresentationTimeMs,
    bool AudioHasSignal,
    long FrameCount,
    long FirstPresentationTimeMs,
    long LastPresentationTimeMs,
    bool FramesChanged,
    long ReadMilliseconds,
    long OpenMilliseconds,
    long DecodeMilliseconds,
    long AudioDecodeMilliseconds,
    long DisposeMilliseconds,
    bool Passed,
    string? Error);

internal sealed record MovieCorpusReport(
    string StartedUtc,
    string CompletedUtc,
    int ExpectedCount,
    int CandidateCount,
    int PassedCount,
    int FailedCount,
    long ElapsedMilliseconds,
    bool Passed,
    IReadOnlyList<string> SelectionErrors,
    IReadOnlyList<MovieCorpusItemResult> Movies);

internal static class MovieCorpusDiscovery
{
    private static ReadOnlySpan<byte> MpegPackStart => [0, 0, 1, 0xba];

    public static IReadOnlyList<PackedAssetEntry> DiscoverMpegMovies(
        Sys4AssetCatalog catalog, IAssetStore store)
        => DiscoverMpegMovies(catalog.EnumerateAssets(), store);

    /// <summary>Selects movies by their MPEG pack header rather than their name: Himegari stores them under
    /// <c>.AGF</c> names (shared with still images) and Kamidori under <c>.MPG</c> names.</summary>
    public static IReadOnlyList<PackedAssetEntry> DiscoverMpegMovies(
        IEnumerable<PackedAssetEntry> assets, IAssetStore store)
    {
        var movies = new List<PackedAssetEntry>();
        Span<byte> signature = stackalloc byte[4];
        foreach (var packed in assets)
        {
            if (packed.Asset.IsPlaceholder) continue;
            using Stream stream = store.Open(packed.Asset);
            int length = 0;
            while (length < signature.Length)
            {
                int read = stream.Read(signature[length..]);
                if (read == 0) break;
                length += read;
            }
            if (length == signature.Length && signature.SequenceEqual(MpegPackStart)) movies.Add(packed);
        }
        return movies;
    }

    public static bool TryReadSequenceDimensions(ReadOnlySpan<byte> payload, out int width, out int height)
    {
        for (int offset = 0; offset <= payload.Length - 7; offset++)
        {
            if (payload[offset] != 0 || payload[offset + 1] != 0
                || payload[offset + 2] != 1 || payload[offset + 3] != 0xb3) continue;
            width = (payload[offset + 4] << 4) | (payload[offset + 5] >> 4);
            height = ((payload[offset + 5] & 0x0f) << 8) | payload[offset + 6];
            return width > 0 && height > 0;
        }
        width = 0;
        height = 0;
        return false;
    }
}

internal sealed class MovieCorpusGate
{
    private readonly Func<MoviePayload, IFfmpegFrameSource> _open;

    public MovieCorpusGate(Func<MoviePayload, IFfmpegFrameSource> open)
        => _open = open ?? throw new ArgumentNullException(nameof(open));

    public MovieCorpusReport Run(IReadOnlyList<MovieCorpusInput> inputs, int expectedCount,
                                 long maximumItemMilliseconds,
                                 Action<int, int, MovieCorpusItemResult>? progress = null)
    {
        if (expectedCount < 0) throw new ArgumentOutOfRangeException(nameof(expectedCount));
        if (maximumItemMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(maximumItemMilliseconds));
        DateTimeOffset started = DateTimeOffset.UtcNow;
        var total = Stopwatch.StartNew();
        var selectionErrors = new List<string>();
        if (inputs.Count != expectedCount)
            selectionErrors.Add($"expected {expectedCount} MPEG movies, discovered {inputs.Count}");

        var results = new List<MovieCorpusItemResult>(inputs.Count);
        for (int index = 0; index < inputs.Count; index++)
        {
            MovieCorpusItemResult result = RunOne(inputs[index], maximumItemMilliseconds);
            results.Add(result);
            progress?.Invoke(index + 1, inputs.Count, result);
        }

        total.Stop();
        int passed = results.Count(result => result.Passed);
        int failed = results.Count - passed;
        return new MovieCorpusReport(
            started.ToString("O"), DateTimeOffset.UtcNow.ToString("O"), expectedCount, inputs.Count,
            passed, failed, total.ElapsedMilliseconds, selectionErrors.Count == 0 && failed == 0,
            selectionErrors, results);
    }

    private MovieCorpusItemResult RunOne(MovieCorpusInput input, long maximumItemMilliseconds)
    {
        long payloadBytes = 0;
        int expectedWidth = 0, expectedHeight = 0, width = 0, height = 0;
        long stopTimeMs = 0, frameCount = 0, firstPts = -1, lastPts = -1;
        int frameRateNumerator = 0, frameRateDenominator = 0;
        bool hasAudio = false, framesChanged = false;
        int audioSampleRate = 0, audioChannels = 0;
        long audioBlockCount = 0, audioFrameCount = 0, firstAudioPts = -1, lastAudioPts = -1;
        bool audioHasSignal = false;
        long readMs = 0, openMs = 0, decodeMs = 0, audioDecodeMs = 0, disposeMs = 0;
        string? error = null;
        IFfmpegFrameSource? source = null;
        var itemTime = Stopwatch.StartNew();

        try
        {
            var phase = Stopwatch.StartNew();
            MoviePayload payload = input.ReadPayload();
            phase.Stop();
            readMs = phase.ElapsedMilliseconds;
            payloadBytes = payload.Bytes.LongLength;
            if (!MovieCorpusDiscovery.TryReadSequenceDimensions(payload.Bytes, out expectedWidth, out expectedHeight))
                throw new InvalidDataException("MPEG sequence header was not found");

            phase.Restart();
            source = _open(payload);
            phase.Stop();
            openMs = phase.ElapsedMilliseconds;
            FfmpegMovieInfo info = source.Info;
            width = info.Width;
            height = info.Height;
            stopTimeMs = info.StopTimeMs;
            frameRateNumerator = info.FrameRateNumerator;
            frameRateDenominator = info.FrameRateDenominator;
            hasAudio = info.HasAudio;
            audioSampleRate = info.AudioSampleRate;
            audioChannels = info.AudioChannels;
            if (width != expectedWidth || height != expectedHeight)
                throw new InvalidDataException(
                    $"decoder dimensions {width}x{height} differ from MPEG sequence {expectedWidth}x{expectedHeight}");
            if (width <= 0 || height <= 0 || stopTimeMs <= 0
                || frameRateNumerator <= 0 || frameRateDenominator <= 0)
                throw new InvalidDataException(
                    $"invalid metadata {width}x{height}, {stopTimeMs} ms, " +
                    $"rate {frameRateNumerator}/{frameRateDenominator}");

            var decodeTime = Stopwatch.StartNew();
            byte[]? firstPixels = null;
            try
            {
                while (source.TryDecodeNextVideoFrame(out FfmpegVideoFrame frame))
                {
                    if (frame.Image.Width != width || frame.Image.Height != height)
                        throw new InvalidDataException(
                            $"frame {frameCount} dimensions are {frame.Image.Width}x{frame.Image.Height}, expected {width}x{height}");
                    int expectedBytes = checked(width * height * 4);
                    if (frame.Image.Pixels.Length != expectedBytes)
                        throw new InvalidDataException(
                            $"frame {frameCount} has {frame.Image.Pixels.Length} RGBA bytes, expected {expectedBytes}");
                    if (frame.PresentationTimeMs < 0 || frame.PresentationTimeMs < lastPts)
                        throw new InvalidDataException(
                            $"frame {frameCount} timestamp {frame.PresentationTimeMs} follows {lastPts}");

                    if (frameCount == 0)
                    {
                        firstPts = frame.PresentationTimeMs;
                        firstPixels = frame.Image.Pixels;
                    }
                    else if (!framesChanged && !frame.Image.Pixels.AsSpan().SequenceEqual(firstPixels))
                        framesChanged = true;
                    lastPts = frame.PresentationTimeMs;
                    frameCount++;
                    if (itemTime.ElapsedMilliseconds > maximumItemMilliseconds)
                        throw new TimeoutException($"item exceeded {maximumItemMilliseconds} ms before EOF");
                }
            }
            finally
            {
                decodeTime.Stop();
                decodeMs = decodeTime.ElapsedMilliseconds;
            }
            if (frameCount == 0) throw new InvalidDataException("decoder reached EOF without a video frame");

            var audioTime = Stopwatch.StartNew();
            try
            {
                if (hasAudio)
                {
                    if (audioSampleRate <= 0 || audioChannels != 2 || info.AudioFrameSamples <= 0)
                        throw new InvalidDataException(
                            $"invalid audio metadata {audioSampleRate} Hz, {audioChannels} channels, " +
                            $"{info.AudioFrameSamples} frame capacity");
                    while (source.TryDecodeNextAudioChunk(out FfmpegAudioChunk chunk))
                    {
                        if (chunk.FrameCount <= 0
                            || chunk.InterleavedStereo.Length != checked(chunk.FrameCount * 2))
                            throw new InvalidDataException(
                                $"audio block {audioBlockCount} has invalid shape " +
                                $"{chunk.FrameCount}f/{chunk.InterleavedStereo.Length} samples");
                        if (chunk.PresentationTimeMs < 0 || chunk.PresentationTimeMs < lastAudioPts)
                            throw new InvalidDataException(
                                $"audio block {audioBlockCount} timestamp {chunk.PresentationTimeMs} follows {lastAudioPts}");
                        foreach (float sample in chunk.InterleavedStereo)
                        {
                            if (!float.IsFinite(sample))
                                throw new InvalidDataException(
                                    $"audio block {audioBlockCount} contains non-finite PCM");
                            if (Math.Abs(sample) > 0.00001f) audioHasSignal = true;
                        }
                        if (audioBlockCount == 0) firstAudioPts = chunk.PresentationTimeMs;
                        lastAudioPts = chunk.PresentationTimeMs;
                        audioBlockCount++;
                        audioFrameCount += chunk.FrameCount;
                        if (itemTime.ElapsedMilliseconds > maximumItemMilliseconds)
                            throw new TimeoutException(
                                $"item exceeded {maximumItemMilliseconds} ms before audio EOF");
                    }
                    if (audioBlockCount == 0)
                        throw new InvalidDataException("audio stream reached EOF without a PCM block");
                }
                else if (audioSampleRate != 0 || audioChannels != 0
                         || source.TryDecodeNextAudioChunk(out _))
                    throw new InvalidDataException("video-only stream exposed unexpected audio metadata or PCM");
            }
            finally
            {
                audioTime.Stop();
                audioDecodeMs = audioTime.ElapsedMilliseconds;
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            if (source != null)
            {
                var disposeTime = Stopwatch.StartNew();
                try { source.Dispose(); }
                catch (Exception exception)
                {
                    error = error == null ? $"dispose failed: {exception.Message}"
                                          : $"{error}; dispose failed: {exception.Message}";
                }
                disposeTime.Stop();
                disposeMs = disposeTime.ElapsedMilliseconds;
            }
        }

        itemTime.Stop();
        if (error == null && itemTime.ElapsedMilliseconds > maximumItemMilliseconds)
            error = $"item took {itemTime.ElapsedMilliseconds} ms, limit is {maximumItemMilliseconds} ms";
        return new MovieCorpusItemResult(
            input.PackedId, $"0x{input.PackedId:x}", input.Name, payloadBytes,
            expectedWidth, expectedHeight, width, height, stopTimeMs,
            frameRateNumerator, frameRateDenominator, hasAudio,
            audioSampleRate, audioChannels, audioBlockCount, audioFrameCount,
            firstAudioPts, lastAudioPts, audioHasSignal,
            frameCount, firstPts, lastPts, framesChanged,
            readMs, openMs, decodeMs, audioDecodeMs, disposeMs, error == null, error);
    }

}
