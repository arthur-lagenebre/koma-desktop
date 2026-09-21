using Koma.Core.Model;
using Koma.Core.Packaging;
using System.Text.Json;
using Koma.TestSupport;
using System.Text.Json.Serialization;

namespace Koma.Core.Tests;

/// <summary>
/// Runs the opener over the upstream conformance corpus.
/// </summary>
/// <remarks>
/// <para>
/// §15.2 makes the corpus normative by example: <c>expected.json</c> states the
/// outcome a conforming implementation must report for each package. These are
/// not fixtures written here to match what the code does, which is the failure
/// mode of a test suite that grades its own homework.
/// </para>
/// <para>
/// This build implements part of each of the four layers, so the cases are
/// sorted into three buckets below. The sorting is the point: a case
/// that moves bucket is either progress or a regression, and either way the
/// test says so instead of quietly passing.
/// </para>
/// </remarks>
public sealed class ConformanceCorpusTests
{
    /// <summary>
    /// Codes this build detects, with the spelling §15.1 and the corpus give.
    /// </summary>
    private static readonly HashSet<string> Implemented =
    [
        ContainerViolationCode.AbsolutePath,
        ContainerViolationCode.AccessibilityHazardConflict,
        ContainerViolationCode.AnimatedPageResource,
        ContainerViolationCode.ChecksumMismatch,
        ContainerViolationCode.ColorLowercase,
        ContainerViolationCode.CompressionRatioLimit,
        ContainerViolationCode.DecorativeWithAlternativeText,
        ContainerViolationCode.DimensionsMismatch,
        ContainerViolationCode.DuplicateLogicalEntry,
        ContainerViolationCode.ExifOrientationResidue,
        ContainerViolationCode.FrontCoverDuplicate,
        ContainerViolationCode.FrontCoverMissing,
        ContainerViolationCode.FrontCoverNotInSpine,
        ContainerViolationCode.MediaTypeMismatch,
        ContainerViolationCode.MimetypeCompression,
        ContainerViolationCode.MimetypeContent,
        ContainerViolationCode.MimetypePosition,
        ContainerViolationCode.NavigationDeclarationMismatch,
        ContainerViolationCode.NavigationTargetOutsideSpine,
        ContainerViolationCode.PagePixelLimit,
        ContainerViolationCode.PageTargetDuplicate,
        ContainerViolationCode.PathTraversal,
        ContainerViolationCode.Span1PageTargetPosition,
        ContainerViolationCode.Span2SpreadPosition,
        ContainerViolationCode.SpineDuplicateItem,
        ContainerViolationCode.SpineTargetMissing,
        ContainerViolationCode.TokenListDuplicate,
        ContainerViolationCode.UnknownToken,
        ContainerViolationCode.UnnamespacedElementInExtensions
    ];

    /// <summary>
    /// Faults this build sees but names differently, with the code it produces.
    /// </summary>
    /// <remarks>
    /// §2.1 fixes the first 62 bytes, so a mimetype entry that is deflated or
    /// not first pushes the media type off offset 38 and fails the sniff. The
    /// package is refused, which is right, but the reason given is wrong: it
    /// says the media type is not there rather than that the entry is in the
    /// wrong place or compressed. Telling them apart means reading the local
    /// header fields rather than only the 24 bytes at the offset.
    /// </remarks>
    private static readonly Dictionary<string, string> Misnamed = [];

    public static TheoryData<string, string, string?> Cases()
    {
        var data = new TheoryData<string, string, string?>();

        foreach (CorpusCase c in LoadExpected())
            data.Add(c.Package, c.Outcome, c.Code);

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesTheCorpus(string package, string outcome, string? code)
    {
        string path = Path.Combine(CorpusRoot(), "packages", package);

        Assert.True(File.Exists(path), $"{package} is missing from the corpus.");

        using FileStream file = File.OpenRead(path);
        PackageOpenResult result = PackageOpener.Open(file, leaveOpen: true);
        List<ContainerViolation> violations = [.. result.Violations];

        // The resource layer is a pass of its own, because it reads every page.
        // A validator runs it over everything, which is what this test is; a
        // reading system verifies a page when it reaches that page.
        if (result.Outcome == PackageOpenOutcome.Opened)
        {
            using KomaPackage opened = result.Package!;
            violations.AddRange(PageResourceChecks.CheckAll(opened));
        }

        // Refused as a validator refuses: any error. A reading system may still
        // read past an unknown token (§16), which ReadsPastAnUnknownToken pins.
        bool refused = result.Outcome != PackageOpenOutcome.Opened || violations.Any(v => v.Severity == ViolationSeverity.Error);
        string found = violations.Count == 0 ? "no violations" : string.Join(", ", violations.Select(v => v.Code));

        // A valid package, and one whose only fault is a warning, must be read.
        if (outcome is "valid" or "warning")
        {
            Assert.False(refused, $"{package} should be readable; got {result.Outcome} with {found}.");

            return;
        }

        if (code is not null && Implemented.Contains(code))
        {
            Assert.True(refused, $"{package} should be refused; got {found}.");
            Assert.Contains(violations, v => v.Code == code);

            return;
        }

        if (code is not null && Misnamed.TryGetValue(code, out string? actual))
        {
            Assert.True(refused, $"{package} should be refused; got {found}.");
            Assert.Contains(violations, v => v.Code == actual);

            return;
        }

        // Everything else is a defect this build cannot see, and the package
        // must be readable: refusing it would be a false positive.
        Assert.False(refused, $"{package} is out of scope and should be readable; got {found}.");
    }

    [Fact]
    public void ReadsPastAnUnknownToken()
    {
        // §16: an unknown token is an error of the publication that §4.5.1
        // gives a fallback, so the reader opens it and carries the error.
        using FileStream file = File.OpenRead(Path.Combine(CorpusRoot(), "packages", "L3-unknown-token.koma"));
        PackageOpenResult result = PackageOpener.Open(file, leaveOpen: true);

        using KomaPackage? package = result.Package;

        Assert.Equal(PackageOpenOutcome.Opened, result.Outcome);
        ContainerViolation unknown = Assert.Single(result.Violations, v => v.Code == ContainerViolationCode.UnknownToken);
        Assert.Equal(ViolationSeverity.Error, unknown.Severity);
    }

    [Fact]
    public void CoverageIsWhatWeThinkItIs()
    {
        // Guards the three buckets above against the corpus moving under them.
        // A new case with an implemented code that this build misses would
        // otherwise be filed as out of scope and pass.
        CorpusCase[] cases = LoadExpected();

        Assert.Equal(43, cases.Length);

        int covered = cases.Count(c => c.Code is not null && Implemented.Contains(c.Code));
        int misnamed = cases.Count(c => c.Code is not null && Misnamed.ContainsKey(c.Code));
        int outOfScope = cases.Length - covered - misnamed;

        Assert.Equal(33, covered);
        Assert.Equal(0, misnamed);
        Assert.Equal(10, outOfScope);
    }

    [Fact]
    public void TheCorpusMatchesTheSpecificationThisBuildTargets()
    {
        using FileStream file = File.OpenRead(Path.Combine(CorpusRoot(), "expected.json"));
        using JsonDocument document = JsonDocument.Parse(file);

        string? specification = document.RootElement.GetProperty("specification").GetString();

        // §5.0: a package built against another 0.x is not something this build may
        // read, and neither is a corpus built against one. The field is prose
        // rather than a bare version, so the version is matched inside it.
        Assert.NotNull(specification);
        Assert.EndsWith(KomaVersion.Supported.ToString(), specification, StringComparison.Ordinal);
    }

    private static CorpusCase[] LoadExpected()
    {
        string path = Path.Combine(CorpusRoot(), "expected.json");

        using FileStream file = File.OpenRead(path);

        return JsonSerializer.Deserialize<CorpusExpectations>(file)?.Cases ?? throw new InvalidDataException($"{path} has no cases.");
    }

    /// <summary>The corpus folder, shared with the tests that open one package by name.</summary>
    internal static string CorpusRoot() => Corpus.Root();

    private sealed record CorpusExpectations
    {
        [JsonPropertyName("specification")]
        public string? Specification { get; init; }

        [JsonPropertyName("cases")]
        public CorpusCase[] Cases { get; init; } = [];
    }

    private sealed record CorpusCase
    {
        [JsonPropertyName("package")]
        public string Package { get; init; } = "";

        [JsonPropertyName("outcome")]
        public string Outcome { get; init; } = "";

        [JsonPropertyName("code")]
        public string? Code { get; init; }
    }
}
