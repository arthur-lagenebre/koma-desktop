using System.Globalization;
using Koma.Core;
using Koma.Core.Importing;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Rendering;
using Koma.Core.Writing;
using Koma.Imaging;

namespace Koma.Cli;

/// <summary>
/// What the command line does, apart from the console it writes to.
/// </summary>
/// <remarks>
/// Everything printed comes from Koma.Core and Koma.Imaging; there is no
/// format knowledge here. Writers are passed in rather than taken from the
/// console, so that what the commands say can be read by a test as a shell
/// reads it.
/// </remarks>
internal static class Commands
{
    public const int Opened = 0;
    public const int Rejected = 1;
    public const int UnsupportedVersion = 2;
    public const int Usage = 3;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Help(output);

            return args.Length == 0 ? Usage : Opened;
        }

        string[] rest = args[1..];

        return args[0] switch
        {
            "info" => Each(rest, output, error, "info", path => Info(path, output)),
            "check" => Each(rest, output, error, "check", path => Check(path, output)),
            "convert" => Convert(rest, output, error),
            _ => Unknown(args[0], output, error)
        };
    }

    /// <summary>
    /// Runs a command over every file given, and answers the worst of what
    /// they returned: a shell asking about a library wants to know whether
    /// anything in it is wrong, not what the last file happened to be.
    /// </summary>
    private static int Each(string[] paths, TextWriter output, TextWriter error, string command, Func<string, int> run)
    {
        if (paths.Length == 0)
        {
            error.WriteLine($"koma {command}: no file given.");

            return Usage;
        }

        int worst = Opened;

        for (int i = 0; i < paths.Length; i++)
        {
            if (i > 0)
                output.WriteLine();

            output.WriteLine(Path.GetFileName(paths[i]));

            worst = Math.Max(worst, File.Exists(paths[i]) ? run(paths[i]) : Missing(paths[i], output));
        }

        return worst;
    }

    private static int Missing(string path, TextWriter output)
    {
        output.WriteLine("  not found");

        return Usage;
    }

    private static int Info(string path, TextWriter output)
    {
        using FileStream file = File.OpenRead(path);
        PackageOpenResult result = PackageOpener.Open(file, leaveOpen: true);

        switch (result.Outcome)
        {
            case PackageOpenOutcome.Opened:
                using (KomaPackage package = result.Package!)
                {
                    Field(output, "version", package.Version.ToString());
                    Field(output, "mode", package.Mode.ToString());
                    Field(output, "navigation", package.Manifest.DeclaresNavigation ? "present" : "none");
                    Field(output, "resources", Count(package.Manifest.Items.Count));
                    Field(output, "spine", Count(package.Manifest.Spine.Count));
                    Field(output, "entries", Count(package.EntryCount));
                    Field(output, "direction", package.Metadata.Direction == ReadingDirection.RightToLeft ? "rtl" : "ltr");
                    Field(output, "spread", package.Metadata.Spread.ToString().ToLowerInvariant());
                    Field(output, "spreads", Count(package.Paginate().Count));
                }

                // Warnings do not stop a package from being read, so they come
                // after what was read rather than in place of it.
                Report(output, result.Violations);

                return Opened;

            case PackageOpenOutcome.UnsupportedVersion:
                // §5.0 keeps this apart from an invalid publication, and the
                // distinction is worth carrying all the way out to a shell: a
                // file from another era of the format is not a broken file, and
                // a script sorting a library should be able to tell them apart.
                Field(output, "version", result.DeclaredVersion?.ToString() ?? "unknown");
                Field(output, "status", "unsupported");
                output.WriteLine($"  this build reads {KomaVersion.Supported} only (§5.0)");

                return UnsupportedVersion;

            default:
                if (result.DeclaredVersion is KomaVersion declared)
                    Field(output, "version", declared.ToString());

                Field(output, "status", "rejected");
                Report(output, result.Violations);

                return Rejected;
        }
    }

    /// <summary>
    /// Every layer of §15 on one file: what opening it says, and what reading
    /// its pages says.
    /// </summary>
    /// <remarks>
    /// The difference with <c>info</c> is layer 4. A page is only judged when
    /// something reads it, so a package can open and still carry a page whose
    /// bytes are not what the manifest declares.
    /// </remarks>
    private static int Check(string path, TextWriter output)
    {
        using FileStream file = File.OpenRead(path);
        PackageOpenResult result = PackageOpener.Open(file, leaveOpen: true);

        using KomaPackage? package = result.Package;

        if (package is null)
        {
            Field(output, "status", result.Outcome == PackageOpenOutcome.UnsupportedVersion ? "unsupported" : "rejected");
            Report(output, result.Violations);

            return result.Outcome == PackageOpenOutcome.UnsupportedVersion ? UnsupportedVersion : Rejected;
        }

        ContainerViolation[] faults = [.. result.Violations, .. PageResourceChecks.CheckAll(package)];
        bool refused = faults.Any(v => v.Severity == ViolationSeverity.Error);

        Field(output, "status", refused ? "faulty" : "conforming");
        Field(output, "pages", Count(package.Manifest.Spine.Count));
        Report(output, faults);

        return refused ? Rejected : Opened;
    }

    /// <summary>Converts a CBZ into a package beside it, or where it is told.</summary>
    private static int Convert(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length is 0 or > 2)
        {
            error.WriteLine("koma convert: give an archive, and a file to write it to.");

            return Usage;
        }

        string cbz = args[0];
        string koma = args.Length == 2 ? args[1] : Path.ChangeExtension(cbz, ".koma");

        if (!File.Exists(cbz))
        {
            error.WriteLine($"koma convert: {cbz} is not there.");

            return Usage;
        }

        if (File.Exists(koma))
        {
            // Never over an existing publication: it may be one that was
            // edited since, or one another tool made.
            error.WriteLine($"koma convert: {koma} exists already.");

            return Usage;
        }

        CbzConversion conversion;

        try
        {
            conversion = CbzConverter.Convert(cbz, koma, new ConversionOptions(Checksums: true));
        }
        catch (Exception refused) when (refused is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"koma convert: {refused.Message}");

            return Rejected;
        }

        output.WriteLine(Path.GetFileName(koma));
        Field(output, "pages", Count(conversion.PageCount));
        Field(output, "direction", conversion.Direction == ReadingDirection.RightToLeft ? "rtl" : "ltr");

        // What it assumed, in the words the reference converter uses.
        foreach (string note in conversion.Notes)
            output.WriteLine($"  note       {note}");

        return Opened;
    }

    private static int Unknown(string command, TextWriter output, TextWriter error)
    {
        error.WriteLine($"koma: unknown command '{command}'.");
        Help(output);

        return Usage;
    }

    private static void Report(TextWriter output, IEnumerable<ContainerViolation> violations)
    {
        foreach (ContainerViolation violation in violations)
        {
            string severity = violation.Severity == ViolationSeverity.Warning ? "warning" : "error";

            output.WriteLine($"  {severity,-11}{violation.Code}");

            if (violation.EntryName is not null)
                output.WriteLine($"    entry   {violation.EntryName}");

            output.WriteLine($"    {violation.Message}");
        }
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Field(TextWriter output, string name, string value) => output.WriteLine($"  {name,-11}{value}");

    private static void Help(TextWriter output)
    {
        output.WriteLine("""
            koma — read, check and convert KOMA publications

            usage:
              koma info <file.koma> [<file.koma> ...]     what a publication says about itself
              koma check <file.koma> [<file.koma> ...]    every layer of §15, pages included
              koma convert <file.cbz> [<file.koma>]       a CBZ as a publication

            exit status:
              0  opened, or converted, warnings and all
              1  faulty; the violations are printed with their §15.1 codes
              2  the version is not one this build reads (§5.0)
              3  bad usage, or a file that is not there
            """);
    }
}
