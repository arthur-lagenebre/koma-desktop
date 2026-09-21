namespace Koma.TestSupport;

/// <summary>
/// Finds the conformance corpus, which is a submodule beside the code.
/// </summary>
/// <remarks>
/// Here rather than in each test project: three of them read the corpus, and
/// a copy of this walk in each is three places to fix when the submodule is
/// not where it is expected.
/// </remarks>
public static class Corpus
{
    /// <summary>The <c>corpus</c> folder of the specification repository.</summary>
    public static string Root()
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

    /// <summary>One package of the corpus, by file name.</summary>
    public static string Package(string name) => Path.Combine(Root(), "packages", name);

    /// <summary>
    /// One of the CBZ archives the specification repository converts with
    /// its reference converter, by file name.
    /// </summary>
    public static string Example(string name) => Path.Combine(Root(), "..", "examples", name);
}
