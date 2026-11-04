using Age.Engine.Model;
using Xunit;

/// <summary>AGE's shared volume curve (0x4830d0, 0x4610d0, movie_audio_apply_volume@0x462610):
/// (ln(v) × 14.427 − 132.88) × 100 millibels, truncated, with 0 as −10000.</summary>
public class NativeAudioVolumeTests
{
    [Theory]
    [InlineData(10000, 0)]
    [InlineData(5000, -1000)]      // 10 dB per halving, not the 6 dB of linear amplitude
    [InlineData(2500, -2000)]
    [InlineData(6500, -621)]      // Himegari CONFIG reset: music
    [InlineData(8000, -322)]      // Himegari CONFIG reset: voice
    [InlineData(1000, -3322)]
    [InlineData(10, -9966)]
    [InlineData(0, -10000)]
    public void MillibelsFollowTheNativeExpression(int basisPoints, int millibels)
        => Assert.Equal(millibels, NativeAudioVolume.ToMillibels(basisPoints));

    [Fact]
    public void LevelsPastTheDirectSoundFloorClampToSilence()
    {
        Assert.Equal(-10000, NativeAudioVolume.ToMillibels(1));
        Assert.Equal(-10000, NativeAudioVolume.ToMillibels(-5));
        Assert.Equal(0, NativeAudioVolume.ToMillibels(20000));
        Assert.Equal(-10.0f, NativeAudioVolume.ToDecibels(5000), 3);
    }

    [Fact]
    public void FadeInterpolatesBasisPointsLikeBgmFadeTick()
    {
        Assert.Equal(5000, NativeAudioVolume.InterpolateFade(5000, 10000, 0));
        Assert.Equal(7500, NativeAudioVolume.InterpolateFade(5000, 10000, 50));
        Assert.Equal(10000, NativeAudioVolume.InterpolateFade(5000, 10000, 100));
        Assert.Equal(6600, NativeAudioVolume.InterpolateFade(10000, 0, 34));
        Assert.Equal(100, NativeAudioVolume.FadeSteps(1000));
        Assert.Equal(10, NativeAudioVolume.FadeSteps(999));
    }
}
