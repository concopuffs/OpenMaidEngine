using System.Runtime.InteropServices;

public class FfmpegMovieNativeTests
{
    [Theory]
    [InlineData(true, false, Architecture.X64, "win-x64")]
    [InlineData(true, false, Architecture.Arm64, "win-arm64")]
    [InlineData(false, true, Architecture.X64, "osx-x64")]
    [InlineData(false, true, Architecture.Arm64, "osx-arm64")]
    [InlineData(false, false, Architecture.X64, "linux-x64")]
    [InlineData(false, false, Architecture.Arm64, "linux-arm64")]
    public void ResolverUsesOperatingSystemAndProcessArchitecture(
        bool isWindows,
        bool isMacOS,
        Architecture architecture,
        string expected)
    {
        Assert.Equal(
            expected,
            FfmpegMovieNative.RuntimeIdentifierFor(isWindows, isMacOS, architecture));
    }

    [Fact]
    public void ResolverRejectsUnreservedArchitectures()
    {
        var error = Assert.Throws<PlatformNotSupportedException>(() =>
            FfmpegMovieNative.RuntimeIdentifierFor(false, false, Architecture.Wasm));

        Assert.Contains("Linux/Wasm", error.Message);
    }
}
