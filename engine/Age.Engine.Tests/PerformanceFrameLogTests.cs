using Age.Engine.Model;

public class PerformanceFrameLogTests
{
    [Fact]
    public void WritesStableCsvSchemaAndFrameWorkload()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"age-perf-{Guid.NewGuid():N}.csv");
        try
        {
            using (var log = new PerformanceFrameLog(path))
            {
                log.BeginFrame(17, 1234, 1.0 / 60.0, "SC0000.BIN", 0x2a91, 0x20c);
                log.BeginRecomposite(false);
                log.RecordPresentationReasons(1 | 4 | 16);
                log.RecordRecomposeAllocation(4096);
                log.RecordSnapshotAllocation(512);
                log.RecordCompositeAllocation(2048);
                log.RecordSourcePrepAllocation(128);
                log.RecordSetDataAllocation(1024);
                log.RecordUiAllocation(64);
                log.RecordPresentationCoordinate("SC0000.BIN", 0x2aaa, 0x21c);
                log.RecordObject(timeVarying: true);
                log.RecordRaster(800, 600, new Affine2D(1, 0, 0, 1, 0, 0),
                                 800, 600, dynamic: false, BlendKind.Alpha, ticks: 10);
                log.RecordGpuLayer(40, 20, new Affine2D(1, 0, 0, 1, 12, 13),
                                   800, 600, dynamic: true, BlendKind.Additive);
                log.RecordGpu(drawItems: 2, textureUploads: 1, textureUploadTicks: 10);
                log.RecordFillLayer();
                log.RecordSkippedLayer();
                log.EndRecomposite();
                log.EndFrame();
                Assert.Equal(1, log.FrameCount);
                Assert.Equal(1, log.RecompositeCount);
            }

            string[] lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            string[] header = lines[0].Split(',');
            string[] row = lines[1].Split(',');
            Assert.Equal(header.Length, row.Length);
            Assert.Equal("frame", header[0]);
            Assert.Equal("17", row[0]);
            Assert.Equal("1", row[Array.IndexOf(header, "recomposited")]);
            Assert.Equal("1", row[Array.IndexOf(header, "render_backend")]);
            Assert.Equal("2", row[Array.IndexOf(header, "gpu_draw_items")]);
            Assert.Equal("1", row[Array.IndexOf(header, "gpu_texture_uploads")]);
            Assert.Equal("1", row[Array.IndexOf(header, "present_host_request")]);
            Assert.Equal("1", row[Array.IndexOf(header, "present_retained_mutation")]);
            Assert.Equal("1", row[Array.IndexOf(header, "present_discrete_cell")]);
            Assert.Equal("0", row[Array.IndexOf(header, "present_continuous_channel")]);
            Assert.Equal("4096", row[Array.IndexOf(header, "recompose_allocated_bytes")]);
            Assert.Equal("512", row[Array.IndexOf(header, "snapshot_allocated_bytes")]);
            Assert.Equal("2048", row[Array.IndexOf(header, "composite_allocated_bytes")]);
            Assert.Equal("128", row[Array.IndexOf(header, "source_prep_allocated_bytes")]);
            Assert.Equal("1024", row[Array.IndexOf(header, "set_data_allocated_bytes")]);
            Assert.Equal("64", row[Array.IndexOf(header, "ui_allocated_bytes")]);
            Assert.Equal("1", row[Array.IndexOf(header, "time_varying_objects")]);
            Assert.Equal("480800", row[Array.IndexOf(header, "candidate_pixels")]);
            Assert.Equal("1", row[Array.IndexOf(header, "dynamic_layers")]);
            Assert.Equal("1", row[Array.IndexOf(header, "additive_layers")]);
            Assert.Equal("SC0000.BIN", row[Array.IndexOf(header, "script")].Trim('"'));
            Assert.Equal("10897", row[Array.IndexOf(header, "offset")]);
            Assert.Equal("524", row[Array.IndexOf(header, "opcode")]);
            Assert.Equal("10922", row[Array.IndexOf(header, "present_offset")]);
            Assert.Equal("540", row[Array.IndexOf(header, "present_opcode")]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0, 0, 10, 10, 100)]
    [InlineData(-5, -5, 10, 10, 25)]
    [InlineData(95, 95, 10, 10, 25)]
    [InlineData(200, 200, 10, 10, 0)]
    public void CandidatePixelsClipsIntegerTranslations(int x, int y, int width, int height, long expected)
    {
        var transform = new Affine2D(1, 0, 0, 1, x, y);
        Assert.True(PerformanceFrameLog.IsIntegerTranslation(transform));
        Assert.Equal(expected,
            PerformanceFrameLog.EstimateCandidatePixels(transform, width, height, 100, 100));
    }

    [Fact]
    public void CandidatePixelsUsesAffineBoundingBoxAndDetectsNonIntegerPath()
    {
        var scaled = new Affine2D(2, 0, 0, 3, 1.5, 2.5);
        Assert.False(PerformanceFrameLog.IsIntegerTranslation(scaled));
        Assert.Equal(651,
            PerformanceFrameLog.EstimateCandidatePixels(scaled, 10, 10, 100, 100));
    }

    [Fact]
    public void RasterClassificationSeparatesTranslationScaleAndGeneralAffine()
    {
        Assert.True(PerformanceFrameLog.IsTranslation(new Affine2D(1, 0, 0, 1, 0.25, -0.5)));
        Assert.False(PerformanceFrameLog.IsIntegerTranslation(new Affine2D(1, 0, 0, 1, 0.25, -0.5)));
        Assert.True(PerformanceFrameLog.IsAxisAligned(new Affine2D(2, 0, 0, 3, 0, 0)));
        Assert.False(PerformanceFrameLog.IsAxisAligned(new Affine2D(1, 0.25, 0, 1, 0, 0)));
    }
}
