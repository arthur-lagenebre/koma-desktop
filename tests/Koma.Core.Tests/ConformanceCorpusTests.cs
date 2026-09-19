using System.Text.Json;
using System.Text.Json.Serialization;
using Koma.Core.Packaging;

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
/// This build implements part of layer 1 and none of layers 3 and 4, so the
/// cases are sorted into three buckets below. The sorting is the point: a case
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
        ContainerViolationCode.MimetypeContent,
        ContainerViolationCode.MimetypePosition,
        ContainerViolationCode.MimetypeCompression,
        ContainerViolationCode.PathTraversal,
        ContainerViolationCode.AbsolutePath,
        ContainerViolationCode.DuplicateLogicalEntry,
        ContainerViolationCode.CompressionRatioLimit,
        ContainerViolationCode.FrontCoverMissing,
        ContainerViolationCode.FrontCoverDuplicate,
        ContainerViolationCode.FrontCoverNotInSpine,
        ContainerViolationCode.SpineTargetMissing,
        ContainerViolationCode.SpineDuplicateItem,
        ContainerViolationCode.Span2SpreadPosition,
        ContainerViolationCode.DecorativeWithAlternativeText
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

    /// <summary>
    /// Faults outside what this build checks. These packages must open: their
    /// defect is real but lies in a layer the opener does not reach, so
    /// refusing them would be a false positive, not early diligence.
    /// </summary>
    private static readonly HashSet<string> OutOfScope =
    [
        // Layer 1, but only observable while decompressing an entry the opener
        // never reads. BoundedReadStream catches it at the point of use.
        "declared-size-mismatch"
    ];

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

        // A valid package, and one whose only fault is a warning, must open.
        if (outcome is "valid" or "warning")
        {
            Assert.True(result.Outcome == PackageOpenOutcome.Opened, $"{package} should open; got {result.Outcome} with {Describe(result)}.");

            return;
        }

        if (code is not null && Implemented.Contains(code))
        {
            Assert.Equal(PackageOpenOutcome.Rejected, result.Outcome);
            Assert.Contains(result.Violations, v => v.Code == code);

            return;
        }

        if (code is not null && Misnamed.TryGetValue(code, out string? actual))
        {
            Assert.Equal(PackageOpenOutcome.Rejected, result.Outcome);
            Assert.Contains(result.Violations, v => v.Code == actual);

            return;
        }

        // Everything else: layers 3 and 4, and the one layer-1 fault the opener
        // cannot see. The package is defective and this build cannot say so.
        Assert.True(result.Outcome == PackageOpenOutcome.Opened,$"{package} is out of scope for the opener and should open; got {result.Outcome} with {Describe(result)}.");
    }

    [Fact]
    public void CoverageIsWhatWeThinkItIs()
    {
        // Guards the three buckets above against the corpus moving under them.
        // A new case with an implemented code that this build misses would
        // otherwise be filed as out of scope and pass.
        CorpusCase[] cases = LoadExpected();

        Assert.Equal(31, cases.Length);

        int covered = cases.Count(c => c.Code is not null && Implemented.Contains(c.Code));
        int misnamed = cases.Count(c => c.Code is not null && Misnamed.ContainsKey(c.Code));
        int outOfScope = cases.Length - covered - misnamed;

        Assert.Equal(16, covered);
        Assert.Equal(0, misnamed);
        Assert.Equal(15, outOfScope);
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

    /// <summary>
    /// Finds <c>external/koma/corpus</c> by walking up from the test assembly.
    /// </summary>
    private static string CorpusRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "external", "koma", "corpus");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The conformance corpus is not on disk. It is a submodule: run git submodule update --init --recursive.");
    }

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

    private static string Describe(PackageOpenResult result) => result.Violations.Count == 0 ? "no violations" : string.Join(", ", result.Violations.Select(v => v.Code));
}
