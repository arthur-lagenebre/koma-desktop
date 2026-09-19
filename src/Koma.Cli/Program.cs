using System.Globalization;
using Koma.Core;
using Koma.Core.Packaging;

namespace Koma.Cli;

/// <summary>
/// A command line over <see cref="PackageOpener"/>.
/// </summary>
/// <remarks>
/// Everything this prints comes from Koma.Core; there is no format knowledge
/// here. The point of the program is to let a person put a real file in front
/// of the reader and see what it says, which no test does.
/// </remarks>
internal static class Program
{
    private const int ExitOpened = 0;
    private const int ExitRejected = 1;
    private const int ExitUnsupportedVersion = 2;
    private const int ExitUsage = 3;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Usage();
            return args.Length == 0 ? ExitUsage : ExitOpened;
        }

        if (args[0] != "info")
        {
            Console.Error.WriteLine($"koma: unknown command '{args[0]}'.");
            Usage();
            return ExitUsage;
        }

        string[] paths = args[1..];

        if (paths.Length == 0)
        {
            Console.Error.WriteLine("koma info: no file given.");
            return ExitUsage;
        }

        int worst = ExitOpened;

        for (int i = 0; i < paths.Length; i++)
        {
            if (i > 0)
                Console.WriteLine();

            worst = Math.Max(worst, Info(paths[i]));
        }

        return worst;
    }

    private static int Info(string path)
    {
        Console.WriteLine(Path.GetFileName(path));

        if (!File.Exists(path))
        {
            Console.WriteLine("  not found");
            return ExitUsage;
        }

        using FileStream file = File.OpenRead(path);
        PackageOpenResult result = PackageOpener.Open(file, leaveOpen: true);

        switch (result.Outcome)
        {
            case PackageOpenOutcome.Opened:
                using (KomaPackage package = result.Package!)
                {
                    Field("version", package.Version.ToString());
                    Field("mode", package.Mode.ToString());
                    Field("manifest", package.RootManifestPath);
                    Field("entries", package.EntryCount.ToString(CultureInfo.InvariantCulture));
                }

                return ExitOpened;

            case PackageOpenOutcome.UnsupportedVersion:
                // §5.0 keeps this apart from an invalid publication, and the
                // distinction is worth carrying all the way out to a shell: a
                // file from another era of the format is not a broken file, and
                // a script sorting a library should be able to tell them apart.
                Field("version", result.DeclaredVersion?.ToString() ?? "unknown");
                Field("status", "unsupported");
                Console.WriteLine($"  this build reads {KomaVersion.Supported} only (§5.0)");

                return ExitUnsupportedVersion;

            default:
                if (result.DeclaredVersion is KomaVersion declared)
                    Field("version", declared.ToString());

                Field("status", "rejected");

                foreach (ContainerViolation violation in result.Violations)
                {
                    Console.WriteLine($"  {violation.Code}");

                    if (violation.EntryName is not null)
                        Console.WriteLine($"    entry   {violation.EntryName}");

                    Console.WriteLine($"    {violation.Message}");
                }

                return ExitRejected;
        }
    }

    private static void Field(string name, string value) => Console.WriteLine($"  {name,-10}{value}");

    private static void Usage()
    {
        Console.WriteLine("""
            koma — read KOMA publications

            usage:
              koma info <file.koma> [<file.koma> ...]

            exit status:
              0  opened
              1  rejected; the violations are printed with their §15.1 codes
              2  the version is not one this build reads (§5.0)
              3  bad usage, or a file that is not there
            """);
    }
}
