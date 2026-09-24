using Koma.TestSupport;

namespace Koma.Cli.Tests;

/// <summary>
/// What the command line says and what it returns, which is what a script
/// reads.
/// </summary>
public sealed class CommandsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("koma-cli").FullName;

    [Theory]
    [InlineData("valid-minimal.koma", Commands.Opened, "conforming")]
    [InlineData("L3-no-front-cover.koma", Commands.Rejected, "rejected")]
    public void CheckSaysWhatAPublicationIsWorth(string package, int status, string said)
    {
        (int returned, string output, _) = Run("check", Corpus.Package(package));

        Assert.Equal(status, returned);
        Assert.Contains(said, output, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckReadsThePagesWhereInfoOnlyOpensThePackage()
    {
        // Layer 4: a page is only judged when something reads it, so a
        // package can open and still carry a page that is not what the
        // manifest declares.
        string package = Corpus.Package("L4-failed-checksum.koma");

        (int opened, _, _) = Run("info", package);
        (int checked_, string output, _) = Run("check", package);

        Assert.Equal(Commands.Opened, opened);
        Assert.Equal(Commands.Rejected, checked_);
        Assert.Contains("checksum-mismatch", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertWritesAPublicationThatOpens()
    {
        string koma = Path.Combine(folder, "manga.koma");

        (int returned, string output, _) = Run("convert", Corpus.Example("manga.cbz"), koma);

        Assert.Equal(Commands.Opened, returned);
        Assert.Contains("rtl", output, StringComparison.Ordinal);
        Assert.Equal(Commands.Opened, Run("check", koma).Status);
    }

    [Fact]
    public void ConvertRefusesToWriteOverAPublicationThatIsThere()
    {
        string koma = Path.Combine(folder, "taken.koma");
        File.WriteAllText(koma, "not to be lost");

        (int returned, _, string error) = Run("convert", Corpus.Example("bare.cbz"), koma);

        Assert.Equal(Commands.Usage, returned);
        Assert.Contains("exists already", error, StringComparison.Ordinal);
        Assert.Equal("not to be lost", File.ReadAllText(koma));
    }

    [Theory]
    [InlineData("check")]
    [InlineData("nonsense", "x")]
    public void SaysWhatItCannotDo(params string[] args)
    {
        (int returned, _, string error) = Run(args);

        Assert.Equal(Commands.Usage, returned);
        Assert.NotEqual(string.Empty, error);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The operating system's to clean up.
        }
    }

    private static (int Status, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int status = Commands.Run(args, output, error);

        return (status, output.ToString(), error.ToString());
    }
}
