using Koma.Core.Versioning;

namespace Koma.Core.Tests;

/// <summary>
/// The mode selection table of §5.3, including the pre-release rules of §5.0.
/// </summary>
public sealed class VersionPortalTests
{
    [Fact]
    public void IdenticalPreReleaseVersions_AreStrict()
    {
        // §5.0: identical 0.x versions are processed in strict mode.
        ProcessingMode mode = VersionPortal.SelectMode(new KomaVersion(0, 9), new KomaVersion(0, 9));

        Assert.Equal(ProcessingMode.Strict, mode);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(0, 10)]
    [InlineData(0, 0)]
    public void DifferentPreReleaseMinor_IsUnsupported(int major, int minor)
    {
        // §5.0: a reader supporting 0.x MUST reject any document declaring 0.y
        // where y differs. Lower or higher makes no difference — this is not an
        // ordering question.
        ProcessingMode mode = VersionPortal.SelectMode(new KomaVersion(major, minor), new KomaVersion(0, 9));

        Assert.Equal(ProcessingMode.UnsupportedMajor, mode);
    }

    [Fact]
    public void PreReleaseDocument_ReadByAStableReader_IsUnsupported()
    {
        // The clause is "either version has a major version of 0", not "both".
        ProcessingMode mode = VersionPortal.SelectMode(new KomaVersion(0, 9), new KomaVersion(1, 0));

        Assert.Equal(ProcessingMode.UnsupportedMajor, mode);
    }

    [Fact]
    public void StableDocument_ReadByThisPreReleaseBuild_IsUnsupported()
    {
        // The same clause seen from the other side. A 1.0 file is not something
        // this build may attempt, however close 0.9 is meant to be to it.
        ProcessingMode mode = VersionPortal.SelectMode(new KomaVersion(1, 0), new KomaVersion(0, 9));

        Assert.Equal(ProcessingMode.UnsupportedMajor, mode);
    }

    [Fact]
    public void DifferentMajorVersions_AreUnsupported()
    {
        ProcessingMode mode = VersionPortal.SelectMode(new KomaVersion(2, 0), new KomaVersion(1, 5));

        Assert.Equal(ProcessingMode.UnsupportedMajor, mode);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 4)]
    [InlineData(1, 5)]
    public void OlderOrEqualMinor_WithinAStableMajor_IsStrict(int major, int minor)
    {
        ProcessingMode mode = VersionPortal.SelectMode(new KomaVersion(major, minor), new KomaVersion(1, 5));

        Assert.Equal(ProcessingMode.Strict, mode);
    }

    [Fact]
    public void NewerMinor_WithinAStableMajor_IsForwardCompatible()
    {
        ProcessingMode mode = VersionPortal.SelectMode(new KomaVersion(1, 6), new KomaVersion(1, 5));

        Assert.Equal(ProcessingMode.ForwardCompatible, mode);
    }

    [Fact]
    public void ForwardCompatibleMode_IsNeverReachedFromAPreReleaseReader()
    {
        // §5.0 forbids entering forward-compatible mode for major version 0.
        // Exhaustive over a range wide enough to catch a portal that treated
        // 0.x like any other major version.
        for (int minor = 0; minor <= 20; minor++)
        {
            ProcessingMode mode = VersionPortal.SelectMode(new KomaVersion(0, minor), new KomaVersion(0, 9));

            Assert.NotEqual(ProcessingMode.ForwardCompatible, mode);
        }
    }

    [Fact]
    public void SelectMode_DefaultsToTheSupportedVersion()
    {
        Assert.Equal(ProcessingMode.Strict, VersionPortal.SelectMode(new KomaVersion(0, 9)));
        Assert.Equal(ProcessingMode.UnsupportedMajor, VersionPortal.SelectMode(new KomaVersion(0, 8)));
    }

    [Fact]
    public void CanProcess_AgreesWithSelectMode()
    {
        Assert.True(VersionPortal.CanProcess(new KomaVersion(0, 9)));
        Assert.False(VersionPortal.CanProcess(new KomaVersion(0, 8)));
        Assert.False(VersionPortal.CanProcess(new KomaVersion(1, 0)));
    }
}
