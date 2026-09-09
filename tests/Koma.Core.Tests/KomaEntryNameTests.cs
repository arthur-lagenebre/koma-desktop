using System.Text;
using Koma.Core.Packaging;

namespace Koma.Core.Tests;

/// <summary>
/// The entry naming rules of §3, and the uniqueness comparison they define.
/// </summary>
public sealed class KomaEntryNameTests
{
    [Theory]
    [InlineData("mimetype")]
    [InlineData("META-INF/container.xml")]
    [InlineData("koma/manifest.xml")]
    [InlineData("images/page-001.webp")]
    [InlineData("images/planche 1.webp")]
    [InlineData("images/été.webp")]
    public void TryValidate_AcceptsWellFormedNames(string name)
    {
        Assert.True(KomaEntryName.TryValidate(name, out EntryNameProblem problem));
        Assert.Equal(EntryNameProblem.None, problem);
    }

    [Theory]
    [InlineData("", EntryNameProblem.Empty)]
    [InlineData("   ", EntryNameProblem.Empty)]
    [InlineData("/koma/manifest.xml", EntryNameProblem.Absolute)]
    [InlineData("C:/koma/manifest.xml", EntryNameProblem.Absolute)]
    [InlineData("koma\\manifest.xml", EntryNameProblem.Backslash)]
    [InlineData("images\\..\\..\\etc", EntryNameProblem.Backslash)]
    [InlineData("koma//manifest.xml", EntryNameProblem.EmptySegment)]
    [InlineData("koma/", EntryNameProblem.EmptySegment)]
    [InlineData("./koma/manifest.xml", EntryNameProblem.CurrentDirectorySegment)]
    [InlineData("koma/./manifest.xml", EntryNameProblem.CurrentDirectorySegment)]
    [InlineData("../etc/passwd", EntryNameProblem.ParentDirectorySegment)]
    [InlineData("koma/../../etc/passwd", EntryNameProblem.ParentDirectorySegment)]
    [InlineData("images/../../../../../../Windows/System32", EntryNameProblem.ParentDirectorySegment)]
    public void TryValidate_RejectsAndExplains(string name, EntryNameProblem expected)
    {
        Assert.False(KomaEntryName.TryValidate(name, out EntryNameProblem problem));
        Assert.Equal(expected, problem);
    }

    [Fact]
    public void TryValidate_AcceptsDotsInsideASegment()
    {
        // Only whole segments of "." and ".." are traversal. A file called
        // "..hidden" or "page..webp" is unusual but not a path construct.
        Assert.True(KomaEntryName.TryValidate("images/..hidden.webp", out _));
        Assert.True(KomaEntryName.TryValidate("images/page..webp", out _));
    }

    [Fact]
    public void TryValidate_RejectsDecomposedNames()
    {
        // "été" with a combining acute rather than a precomposed é. Same
        // logical name, different bytes; §3 requires NFC in the archive.
        string decomposed = "images/e\u0301te\u0301.webp";

        Assert.False(decomposed.IsNormalized(NormalizationForm.FormC));
        Assert.False(KomaEntryName.TryValidate(decomposed, out EntryNameProblem problem));
        Assert.Equal(EntryNameProblem.NotNormalized, problem);
    }

    [Fact]
    public void FoldForUniqueness_CollapsesNormalizationDifferences()
    {
        // The decomposed form is not a legal entry name, but uniqueness is
        // compared after normalization, so a package cannot smuggle a second
        // entry past the check by decomposing it.
        Assert.True(KomaEntryName.AreSameLogicalName("images/été.webp", "images/e\u0301te\u0301.webp"));
    }

    [Fact]
    public void FoldForUniqueness_CollapsesCaseDifferences()
    {
        Assert.True(KomaEntryName.AreSameLogicalName("koma/Manifest.xml", "koma/manifest.xml"));
        Assert.True(KomaEntryName.AreSameLogicalName("IMAGES/PAGE.WEBP", "images/page.webp"));
    }

    [Fact]
    public void FoldForUniqueness_CollapsesBothGreekSigmas()
    {
        // Final sigma and medial sigma are the same letter. Upper-casing maps
        // both onto Σ; lower-casing would leave them distinct.
        Assert.True(KomaEntryName.AreSameLogicalName("οδος.webp", "ΟΔΟΣ.webp"));
        Assert.True(KomaEntryName.AreSameLogicalName("σ.webp", "ς.webp"));
    }

    [Fact]
    public void FoldForUniqueness_KeepsAccentsApart()
    {
        // ό upper-cases to Ό, not Ο: an accent is not a case difference, so the
        // two names stay distinct. Greek typography drops accents in capitals,
        // which makes this look like a bug to anyone who reads it as text rather
        // than as bytes. It is not one, and §3 folds case only.
        Assert.False(KomaEntryName.AreSameLogicalName("οδός.webp", "ΟΔΟΣ.webp"));
    }

    [Fact]
    public void FoldForUniqueness_DoesNotExpandSharpS()
    {
        // Documents the choice rather than endorsing it. §3 says "case folding"
        // without saying whether it means simple or full folding; under full
        // folding these two would collide. See the remarks on FoldForUniqueness.
        Assert.False(KomaEntryName.AreSameLogicalName("straße.webp", "STRASSE.webp"));
    }

    [Fact]
    public void FoldForUniqueness_KeepsGenuinelyDifferentNamesApart()
    {
        Assert.False(KomaEntryName.AreSameLogicalName("koma/manifest.xml", "koma/metadata.xml"));
        Assert.False(KomaEntryName.AreSameLogicalName("images/page-001.webp", "images/page-002.webp"));
    }
}
