namespace Koma.Core.Tests;

/// <summary>
/// The lexical rules of §5.1, which defer to the <c>Integer</c> type of §4:
/// unsigned decimal, no sign, no leading zeros, no whitespace.
/// </summary>
public sealed class KomaVersionTests
{
    [Theory]
    [InlineData("0.9", 0, 9)]
    [InlineData("1.0", 1, 0)]
    [InlineData("0.0", 0, 0)]
    [InlineData("12.345", 12, 345)]
    public void TryParse_AcceptsWellFormedVersions(string text, int major, int minor)
    {
        Assert.True(KomaVersion.TryParse(text, out var version));
        Assert.Equal(new KomaVersion(major, minor), version);
    }

    [Theory]
    // Leading zeros. Numerically equal to a valid version, lexically forbidden.
    [InlineData("00.9")]
    [InlineData("0.09")]
    [InlineData("01.0")]
    // Signs.
    [InlineData("+0.9")]
    [InlineData("-1.0")]
    [InlineData("0.-9")]
    // Whitespace.
    [InlineData(" 0.9")]
    [InlineData("0.9 ")]
    [InlineData("0. 9")]
    // Wrong shape.
    [InlineData("1")]
    [InlineData("1.0.0")]
    [InlineData("0.")]
    [InlineData(".9")]
    [InlineData(".")]
    [InlineData("")]
    // Not decimal at all.
    [InlineData("0.9beta")]
    [InlineData("v0.9")]
    [InlineData("zero.nine")]
    // Arabic-Indic digits: char.IsDigit would accept these, §4 does not.
    [InlineData("٠.٩")]
    public void TryParse_RejectsMalformedVersions(string text)
    {
        Assert.False(KomaVersion.TryParse(text, out _));
    }

    [Fact]
    public void TryParse_RejectsNull()
    {
        Assert.False(KomaVersion.TryParse(null, out _));
    }

    [Fact]
    public void TryParse_RejectsValuesTooLargeForTheType()
    {
        Assert.False(KomaVersion.TryParse("99999999999.0", out _));
    }

    [Fact]
    public void Parse_ThrowsOnMalformedInput()
    {
        Assert.Throws<FormatException>(() => KomaVersion.Parse("0.09"));
    }

    [Fact]
    public void ToString_RoundTripsThroughTryParse()
    {
        var original = new KomaVersion(0, 9);

        Assert.Equal("0.9", original.ToString());
        Assert.True(KomaVersion.TryParse(original.ToString(), out KomaVersion reparsed));
        Assert.Equal(original, reparsed);
    }

    [Fact]
    public void Supported_IsTheVersionThisBuildImplements()
    {
        // §5.1: a KOMA 0.9 implementation MUST support version="0.9". If this
        // ever needs changing, §5.0 says the serialized value moves from 0.9 to
        // 1.0 at the moment the §5.0.1 criteria are met, and at no other.
        Assert.Equal(new KomaVersion(0, 9), KomaVersion.Supported);
    }

    [Fact]
    public void IsPreRelease_TracksMajorVersionZero()
    {
        Assert.True(new KomaVersion(0, 9).IsPreRelease);
        Assert.False(new KomaVersion(1, 0).IsPreRelease);
    }

    [Fact]
    public void Comparison_OrdersByMajorThenMinor()
    {
        Assert.True(new KomaVersion(0, 9) < new KomaVersion(1, 0));
        Assert.True(new KomaVersion(1, 2) > new KomaVersion(1, 1));
        Assert.True(new KomaVersion(1, 0) <= new KomaVersion(1, 0));
        Assert.True(new KomaVersion(2, 0) > new KomaVersion(1, 99));
    }
}
