using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Koma.Core.Rendering;

namespace Koma.Core.Tests;

/// <summary>
/// The pairing algorithm, against the fixtures of the specification repository.
/// </summary>
/// <remarks>
/// <para>
/// The expectations in <c>corpus/spread-cases.json</c> are written by hand from
/// the prose of §10 and never regenerated from an implementation, so a bug in
/// one cannot become the definition of the format. They are the closest thing
/// this project has to an external examiner: the reference implementation is
/// checked against the same file, and §5.0.1 asks the two to agree.
/// </para>
/// <para>
/// A test that merely reproduced what this code does would be worth nothing
/// here. That is why none of these fixtures live in this repository.
/// </para>
/// </remarks>
public sealed partial class SpreadPaginatorTests
{
    public static TheoryData<string> CaseIds()
    {
        var data = new TheoryData<string>();

        foreach (PairingCase c in Load().Cases)
            data.Add(c.Id);

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void MatchesTheFixture(string id)
    {
        PairingCase fixture = Load().Cases.Single(c => c.Id == id);

        IReadOnlyList<Spread> got = SpreadPaginator.Paginate(
            [.. fixture.Spine.Select(ToEntry)],
            fixture.Direction == "rtl" ? ReadingDirection.RightToLeft : ReadingDirection.LeftToRight,
            fixture.Spread switch
            {
                "none" => SpreadPolicy.None,
                "force" => SpreadPolicy.Force,
                _ => SpreadPolicy.Auto
            },
            fixture.Viewport);

        Spread[] want = [.. fixture.Expected.Select(ToSpread)];

        Assert.Equal(want, got);
    }

    [Fact]
    public void ImplementsTheFifteenFixtures()
    {
        // Guards against a fixture file that failed to load or was truncated:
        // a Theory over an empty set passes, silently.
        Assert.Equal(15, Load().Cases.Length);
    }

    [Fact]
    public void CarriesThePseudocodeOfTheSpecification()
    {
        // §10.4 says a reading system MUST follow the pseudocode, so agreeing
        // with the prose is not enough. The reference implementation pins the
        // same text for the same reason; this is the other half of that pair.
        string spec = File.ReadAllText(Path.Combine(RepositoryRoot(), "external", "koma", "spec", "koma-0.9.md"));
        Match match = PseudocodeBlock().Match(spec);

        Assert.True(match.Success, "The §10.4 pseudocode block was not found in the specification.");

        string fromSpec = match.Groups[1].Value.Replace("\r\n", "\n", StringComparison.Ordinal);
        string carried = SpreadPaginator.Pseudocode.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(fromSpec, carried);
    }

    [GeneratedRegex(@"### 10\.4 Pairing algorithm.*?```text\r?\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex PseudocodeBlock();

    private static SpineEntry ToEntry(FixtureSpineEntry entry) => new()
    {
        Item = entry.Item,
        PageSpan = entry.PageSpan,
        SpreadPosition = entry.SpreadPosition switch
        {
            "left" => SpreadPosition.Left,
            "right" => SpreadPosition.Right,
            "center" => SpreadPosition.Center,
            _ => SpreadPosition.Auto
        },
        Roles = entry.Roles
    };

    /// <summary>
    /// A fixture spread is one of three shapes: a single item, a centered item,
    /// or a pair with either half possibly empty.
    /// </summary>
    private static Spread ToSpread(Dictionary<string, string?> expected)
    {
        if (expected.TryGetValue("single", out string? single))
            return new SingleSpread(single!);

        if (expected.TryGetValue("center", out string? center))
            return new CenteredSpread(center!);

        expected.TryGetValue("left", out string? left);
        expected.TryGetValue("right", out string? right);

        return new PairSpread(left, right);
    }

    private static PairingFixtures Load()
    {
        string path = Path.Combine(RepositoryRoot(), "external", "koma", "corpus", "spread-cases.json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The pairing fixtures are not on disk. They live in the koma submodule: run git submodule update --init --recursive.", path);
        }

        using FileStream file = File.OpenRead(path);

        return JsonSerializer.Deserialize<PairingFixtures>(file) ?? throw new InvalidDataException($"{path} could not be read.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "external", "koma")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The koma submodule is not on disk.");
    }

    private sealed record PairingFixtures
    {
        [JsonPropertyName("cases")]
        public PairingCase[] Cases { get; init; } = [];
    }

    private sealed record PairingCase
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("direction")]
        public string Direction { get; init; } = "";

        [JsonPropertyName("spread")]
        public string Spread { get; init; } = "";

        [JsonPropertyName("viewport")]
        public bool Viewport { get; init; } = true;

        [JsonPropertyName("spine")]
        public FixtureSpineEntry[] Spine { get; init; } = [];

        [JsonPropertyName("expected")]
        public Dictionary<string, string?>[] Expected { get; init; } = [];
    }

    private sealed record FixtureSpineEntry
    {
        [JsonPropertyName("item")]
        public string Item { get; init; } = "";

        [JsonPropertyName("page_span")]
        public int PageSpan { get; init; } = 1;

        [JsonPropertyName("spread_position")]
        public string SpreadPosition { get; init; } = "auto";

        [JsonPropertyName("roles")]
        public string[] Roles { get; init; } = [];
    }
}
