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

    /// <summary>
    /// Converts a CBZ into a package beside it, or where it is told, with the
    /// choices the reader would have made in the window.
    /// </summary>
    /// <remarks>
    /// The same options as the import window, over the same converter: what
    /// turns a CBZ into a publication is one implementation, and the two
    /// front ends offer the same of it.
    /// </remarks>
    private static int Convert(string[] args, TextWriter output, TextWriter error)
    {
        var options = new ConversionOptions(Checksums: true);
        var paths = new List<string>();
        var modes = new List<string>();
        var hazards = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--no-comicinfo":
                    options = options with { KeepComicInfo = false };
                    break;
                case "--number-from-file-name":
                    options = options with { NumberFromFileName = true };
                    break;
                case "--access-mode" when i + 1 < args.Length:
                    modes.Add(args[++i]);
                    break;
                case "--hazard" when i + 1 < args.Length:
                    hazards.Add(args[++i]);
                    break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        error.WriteLine($"koma convert: unknown option '{args[i]}'.");

                        return Usage;
                    }

                    paths.Add(args[i]);
                    break;
            }
        }

        if (paths.Count is 0 or > 2)
        {
            error.WriteLine("koma convert: give an archive, and a file to write it to.");

            return Usage;
        }

        options = options with { AccessModes = modes, AccessibilityHazards = hazards };

        string cbz = paths[0];

        // A folder converts everything under it, keeping the tree: a library
        // is converted a library at a time, and one archive at a time was
        // only ever the smallest case of that.
        if (Directory.Exists(cbz))
            return ConvertFolder(cbz, paths.Count == 2 ? paths[1] : cbz, options, output, error);

        string koma = paths.Count == 2 ? paths[1] : Path.ChangeExtension(cbz, ".koma");

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
            conversion = CbzConverter.Convert(cbz, koma, options);
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

    /// <summary>
    /// Converts every archive under a folder, and says at the end which ones
    /// want a second look.
    /// </summary>
    /// <remarks>
    /// A hundred archives each carrying the same eight notes is eight hundred
    /// lines saying nothing. What a reader wants after a long conversion is
    /// the list of publications that are not like the others, so the notes
    /// are counted: the ones that fall on nearly everything are summed up in
    /// a line, and the ones that fall on a few name those few.
    /// </remarks>
    private static int ConvertFolder(string source, string destination, ConversionOptions options, TextWriter output, TextWriter error)
    {
        string[] archives = [.. Directory.EnumerateFiles(source, "*.cbz", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

        if (archives.Length == 0)
        {
            error.WriteLine($"koma convert: no archive under {source}.");

            return Usage;
        }

        var said = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        int converted = 0;
        int refused = 0;
        int skipped = 0;

        foreach (string cbz in archives)
        {
            string koma = Path.Combine(destination, Path.ChangeExtension(Path.GetRelativePath(source, cbz), ".koma"));
            string name = Path.GetRelativePath(source, koma);

            if (File.Exists(koma))
            {
                // Never over an existing publication, and a run that was
                // interrupted is finished by running it again.
                skipped++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(koma)!);

            try
            {
                CbzConversion conversion = CbzConverter.Convert(cbz, koma, options);

                converted++;

                foreach (string note in conversion.Notes)
                {
                    if (!said.TryGetValue(note, out List<string>? which))
                        said[note] = which = [];

                    which.Add(name);
                }
            }
            catch (Exception refusal) when (refusal is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                refused++;
                error.WriteLine($"{name}: {refusal.Message}");
            }
        }

        output.WriteLine($"{Count(converted)} converted, {Count(refused)} refused, {Count(skipped)} already there.");

        if (converted > 0)
            Notes(said, converted, output);

        return refused == 0 ? Opened : Rejected;
    }

    /// <summary>
    /// The notes of a whole conversion: what fell on everything, then what
    /// fell on a few, with their names.
    /// </summary>
    private static void Notes(Dictionary<string, List<string>> said, int converted, TextWriter output)
    {
        // A tenth of the run, or five publications: below either, a note is
        // about particular publications and they are worth naming.
        int few = Math.Max(5, converted / 10);

        output.WriteLine();

        foreach ((string note, List<string> which) in said.OrderByDescending(n => n.Value.Count).ThenBy(n => n.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"  {Count(which.Count)}  {note}");

            if (which.Count > few)
                continue;

            foreach (string name in which)
                output.WriteLine($"       {name}");
        }
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
              koma convert [options] <file.cbz> [<file.koma>]

            options of koma convert, as the import window offers them:
              --no-comicinfo             leave the original ComicInfo out of the package
              --number-from-file-name    take the volume number from the digits the name starts with
              --access-mode <token>      how the publication is read (§7.13); repeatable
              --hazard <token>           what it may do to a reader (§7.13); repeatable

            exit status:
              0  opened, or converted, warnings and all
              1  faulty; the violations are printed with their §15.1 codes
              2  the version is not one this build reads (§5.0)
              3  bad usage, or a file that is not there
            """);
    }
}
