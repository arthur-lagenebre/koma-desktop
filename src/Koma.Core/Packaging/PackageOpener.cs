using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Xml.Linq;
using Koma.Core.Versioning;

namespace Koma.Core.Packaging;

/// <summary>
/// How an attempt to open a package ended.
/// </summary>
public enum PackageOpenOutcome
{
    /// <summary>The package is open and may be read.</summary>
    Opened,
    /// <summary>
    /// The package declares a version this build does not support. §5.0
    /// requires this to be reported as an unsupported version and not as an
    /// invalid publication, so it is an outcome of its own rather than a
    /// violation in the list.
    /// </summary>
    UnsupportedVersion,
    /// <summary>The package is not readable. See the violations.</summary>
    Rejected
}

/// <summary>
/// The result of opening a package.
/// </summary>
public sealed record PackageOpenResult
{
    public required PackageOpenOutcome Outcome { get; init; }

    /// <summary>Set when <see cref="Outcome"/> is <see cref="PackageOpenOutcome.Opened"/>.</summary>
    public KomaPackage? Package { get; init; }

    /// <summary>
    /// The version the package declares, when one could be read. Present for
    /// <see cref="PackageOpenOutcome.UnsupportedVersion"/>, which is the case
    /// where a caller most needs it to tell the user what they have.
    /// </summary>
    public KomaVersion? DeclaredVersion { get; init; }

    public required ReadOnlyCollection<ContainerViolation> Violations { get; init; }
}

/// <summary>
/// An open KOMA package.
/// </summary>
public sealed class KomaPackage : IDisposable
{
    private readonly ZipArchive archive;

    internal KomaPackage(ZipArchive archive, KomaVersion version, ProcessingMode mode, string rootManifestPath, ResourceLimits limits)
    {
        this.archive = archive;
        Version = version;
        Mode = mode;
        RootManifestPath = rootManifestPath;
        Limits = limits;
    }

    /// <summary>Number of entries in the archive.</summary>
    public int EntryCount => archive.Entries.Count;

    /// <summary>The version declared by <c>container.xml</c>.</summary>
    public KomaVersion Version { get; }

    /// <summary>The processing mode selected by §5.3.</summary>
    public ProcessingMode Mode { get; }

    /// <summary>Path of the root manifest, from the single <c>RootFile</c> of §6.</summary>
    public string RootManifestPath { get; }

    /// <summary>The profile the package was opened under.</summary>
    public ResourceLimits Limits { get; }

    /// <summary>
    /// Loads a core XML document by its package-relative path.
    /// </summary>
    public XDocument? TryLoadXml(string path, out ContainerViolation? violation) => KomaXml.TryLoad(archive, path, out violation, Limits);

    /// <summary>
    /// Opens a resource for reading, bounded by the size its own central
    /// directory declares.
    /// </summary>
    public Stream? TryOpenResource(string path)
    {
        ZipArchiveEntry? entry = archive.GetEntry(path);

        if (entry is null)
            return null;

        return new BoundedReadStream(entry.Open(), entry.Length, path);
    }

    public void Dispose() => archive.Dispose();
}

/// <summary>
/// Opens a package, in the order the specification requires.
/// </summary>
/// <remarks>
/// <para>
/// The order is the substance here, not an implementation detail. The entry
/// count is refused before the archive is built (§13.1); the mimetype entry is
/// checked next, once the central directory can tell an absent entry from a
/// misplaced one (§2.1); and the version portal runs before any judgement of
/// validity (§5.0): a package from another era of the format is not a broken
/// package, and a reader that validates first reports the wrong thing about it.
/// </para>
/// </remarks>
public static class PackageOpener
{
    private const string ContainerPath = "META-INF/container.xml";
    private const string ContainerNamespace = "urn:koma:container";
    private const string ManifestMediaType = "application/vnd.koma.manifest+xml";

    public static PackageOpenResult Open(Stream stream, ResourceLimits? limits = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        ResourceLimits profile = limits ?? ResourceLimits.Default;

        // 1. Before the archive exists.
        ContainerViolation? gate = ArchiveGate.CheckBeforeOpening(stream, profile);

        if (gate is not null)
            return Rejected(gate);

        var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        bool keep = false;

        try
        {
            ContainerViolation? mimetype = MimetypeEntryCheck.Check(stream, archive);

            if (mimetype is not null)
                return Rejected(mimetype);

            // 2. container.xml, and the version, before anything else is judged.
            XDocument? container = KomaXml.TryLoad(archive, ContainerPath, out ContainerViolation? xml, profile);

            if (xml is not null)
                return Rejected(xml);

            if (container is null)
                return Rejected(new ContainerViolation(ContainerViolationCode.MissingRequiredXml, ContainerPath, "The package has no container (§6)."));

            XElement? root = container.Root;

            if (root is null || root.Name != XName.Get("Container", ContainerNamespace))
                return Rejected(new ContainerViolation(ContainerViolationCode.SchemaInvalidContainer, ContainerPath, $"The root element is not Container in {ContainerNamespace} (§6)."));

            string? declared = root.Attribute("version")?.Value;

            if (!KomaVersion.TryParse(declared, out KomaVersion version))
                return Rejected(new ContainerViolation(ContainerViolationCode.SchemaInvalidContainer, ContainerPath, $"'{declared}' is not a version of the form major.minor (§5.1)."));

            ProcessingMode mode = VersionPortal.SelectMode(version);

            if (mode == ProcessingMode.UnsupportedMajor)
            {
                return new PackageOpenResult
                {
                    Outcome = PackageOpenOutcome.UnsupportedVersion,
                    DeclaredVersion = version,
                    Violations = Empty
                };
            }

            // 3. Now that the package is one this build may read, judge it.
            var violations = new List<ContainerViolation>();

            InspectionResult inspection = ArchiveInspector.Inspect(archive, stream.Length, profile);
            violations.AddRange(inspection.Violations);

            string? rootPath = ReadRootFile(root, ContainerPath, violations);

            if (rootPath is not null && archive.GetEntry(rootPath) is null)
                violations.Add(new ContainerViolation(ContainerViolationCode.MissingRequiredXml, rootPath, "The container points at a manifest the package does not contain (§6)."));

            if (violations.Count > 0 || rootPath is null)
            {
                return new PackageOpenResult
                {
                    Outcome = PackageOpenOutcome.Rejected,
                    DeclaredVersion = version,
                    Violations = violations.AsReadOnly()
                };
            }

            keep = true;

            return new PackageOpenResult
            {
                Outcome = PackageOpenOutcome.Opened,
                Package = new KomaPackage(archive, version, mode, rootPath, profile),
                DeclaredVersion = version,
                Violations = Empty
            };
        }
        finally
        {
            if (!keep)
                archive.Dispose();
        }
    }

    /// <summary>
    /// Reads the single <c>RootFile</c> §6 requires.
    /// </summary>
    private static string? ReadRootFile(XElement container, string entryName, List<ContainerViolation> violations)
    {
        XElement[] roots = [.. container.Elements(XName.Get("RootFiles", ContainerNamespace))
                                        .Elements(XName.Get("RootFile", ContainerNamespace))];

        if (roots.Length != 1)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.SchemaInvalidContainer, entryName, $"KOMA 0.9 requires exactly one RootFile; found {roots.Length} (§6)."));

            return null;
        }

        XElement rootFile = roots[0];
        string? path = rootFile.Attribute("full-path")?.Value;
        string? mediaType = rootFile.Attribute("media-type")?.Value;

        if (mediaType != ManifestMediaType)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.SchemaInvalidContainer, entryName, $"RootFile/@media-type is '{mediaType}', not the literal of §2."));

            return null;
        }

        // A Path is a package-relative entry name, so the rules of §3 apply to
        // it before it is used to reach into the archive.
        if (!KomaEntryName.TryValidate(path, out EntryNameProblem problem))
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.SchemaInvalidContainer, entryName, $"RootFile/@full-path is not a Path: {problem} (§4.3)."));

            return null;
        }

        return path;
    }

    private static readonly ReadOnlyCollection<ContainerViolation> Empty = new List<ContainerViolation>().AsReadOnly();

    private static PackageOpenResult Rejected(ContainerViolation violation) => new()
    {
        Outcome = PackageOpenOutcome.Rejected,
        Violations = new List<ContainerViolation> { violation }.AsReadOnly()
    };
}
