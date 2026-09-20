using Koma.Core.Model;
using Koma.Core.Packaging;

namespace Koma.Core.Tests;

/// <summary>
/// The line §16 draws through the resource layer.
/// </summary>
/// <remarks>
/// Only two of the four withholding faults have a corpus package that reaches
/// a page, so the rule is pinned here code by code, every code of the layer
/// included: a new one lands on the wrong side of the line by omission, and
/// this is where that shows.
/// </remarks>
public sealed class PageResourceChecksTests
{
    [Theory]
    [InlineData(ContainerViolationCode.MissingPageResource, true)]
    [InlineData(ContainerViolationCode.UnreadablePageResource, true)]
    [InlineData(ContainerViolationCode.DeclaredSizeMismatch, true)]
    [InlineData(ContainerViolationCode.PagePixelLimit, true)]
    [InlineData(ContainerViolationCode.MediaTypeMismatch, false)]
    [InlineData(ContainerViolationCode.DimensionsMismatch, false)]
    [InlineData(ContainerViolationCode.AnimatedPageResource, false)]
    [InlineData(ContainerViolationCode.ChecksumMismatch, false)]
    [InlineData(ContainerViolationCode.ExifOrientationResidue, false)]
    public void Withholds_OnlyWhatLeavesNothingSafeToShow(string code, bool withheld)
    {
        var violation = new ContainerViolation(code, "pages/001.jpg", "Stated for the test.");

        Assert.Equal(withheld, PageResourceChecks.Withholds(violation));
    }
}
