using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Xml.Linq;
using Koma.Core.Model;
using Koma.Core.Rendering;
using Koma.Core.Schemas;
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

    internal KomaPackage(ZipArchive archive, KomaVersion version, ProcessingMode mode, Manifest manifest, PublicationMetadata metadata, PublicationNavigation? navigation, ResourceLimits limits)
    {
        this.archive = archive;
        Version = version;
        Mode = mode;
        Manifest = manifest;
        Metadata = metadata;
        Navigation = navigation;
        Limits = limits;
    }

    /// <summary>Number of entries in the archive.</summary>
    public int EntryCount => archive.Entries.Count;

    /// <summary>The version declared by <c>container.xml</c>.</summary>
    public KomaVersion Version { get; }

    /// <summary>The processing mode selected by §5.3.</summary>
    public ProcessingMode Mode { get; }

    /// <summary>The manifest: the declared resources and the reading order (§8).</summary>
    public Manifest Manifest { get; }

    /// <summary>What the metadata says about how the publication is read (§7).</summary>
    public PublicationMetadata Metadata { get; }

    /// <summary>What <c>nav.xml</c> offers the reader (§9), or <see langword="null"/> without one.</summary>
    public PublicationNavigation? Navigation { get; }

    /// <summary>The profile the package was opened under.</summary>
    public ResourceLimits Limits { get; }

    /// <summary>
    /// Loads a core XML document by its package-relative path.
    /// </summary>
    public XDocument? TryLoadXml(string path, out ContainerViolation? violation) => KomaXml.TryLoad(archive, path, out violation, Limits);

    /// <summary>
    /// The publication laid out as it is to be displayed (§10).
    /// </summary>
    /// <param name="viewportFitsTwo">
    /// Whether the display can show two pages side by side. §10.1 leaves that
    /// judgement to the reading system, so it is asked for here rather than
    /// guessed: the spine, the reading direction and the spread policy come
    /// from the publication, and this one fact does not.
    /// </param>
    public IReadOnlyList<Spread> Paginate(bool viewportFitsTwo = true) => SpreadPaginator.Paginate(Manifest.ToSpineEntries(), Metadata.Direction, Metadata.Spread, viewportFitsTwo);

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
/// <para>
/// Only errors stop a package from opening, and not every error: §16 lets an
/// unknown token be read past with its fallback. §8.8 allows a resource
/// outside the spine and asks that it be noticed, so a reader that refused it
/// would refuse a publication the specification calls readable; the warnings,
/// and the errors read past, travel on the result instead.
/// </para>
/// </remarks>
public static class PackageOpener
{
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
            XDocument? container = KomaXml.TryLoad(archive, CorePaths.Container, out ContainerViolation? xml, profile);

            if (xml is not null)
                return Rejected(xml);

            if (container is null)
                return Rejected(new ContainerViolation(ContainerViolationCode.MissingRequiredXml, CorePaths.Container, "The package has no container (§6)."));

            XElement? root = container.Root;

            if (root is null || root.Name != XName.Get("Container", ContainerNamespace))
                return Rejected(new ContainerViolation(ContainerViolationCode.SchemaInvalidContainer, CorePaths.Container, $"The root element is not Container in {ContainerNamespace} (§6)."));

            string? declared = root.Attribute("version")?.Value;

            if (!KomaVersion.TryParse(declared, out KomaVersion version))
                return Rejected(new ContainerViolation(ContainerViolationCode.SchemaInvalidContainer, CorePaths.Container, $"'{declared}' is not a version of the form major.minor (§5.1)."));

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

            ContainerViolation? containerSchema = CheckSchema(container, CorePaths.Container, mode, violations);
            bool rootFileValid = CheckRootFile(root, violations);
            KeepOne(containerSchema, violations);

            Manifest? manifest = rootFileValid ? ReadManifest(archive, version, mode, profile, violations) : null;

            (PublicationMetadata? metadata, PublicationNavigation? navigation) = manifest is null ? (null, null) : ReadCompanionDocuments(archive, manifest, version, mode, profile, violations);

            if (manifest is null || metadata is null || violations.Any(PreventsReading))
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
                Package = new KomaPackage(archive, version, mode, manifest, metadata, navigation, profile),
                DeclaredVersion = version,
                Violations = violations.AsReadOnly()
            };
        }
        finally
        {
            if (!keep)
                archive.Dispose();
        }
    }

    /// <summary>
    /// Loads and reads the root manifest.
    /// </summary>
    private static Manifest? ReadManifest(ZipArchive archive, KomaVersion version, ProcessingMode mode, ResourceLimits profile, List<ContainerViolation> violations)
    {
        const string path = CorePaths.Manifest;
        XDocument? document = KomaXml.TryLoad(archive, path, out ContainerViolation? xml, profile);

        if (xml is not null)
        {
            violations.Add(xml);
            return null;
        }

        if (document is null)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.MissingRequiredXml, path, "The package has no manifest (§1)."));
            return null;
        }

        ContainerViolation? schema = CheckSchema(document, path, mode, violations);
        CoreDocumentChecks.CheckExtensions(document, path, violations);

        Manifest? manifest = ManifestReader.Read(document, path, version, violations);

        if (manifest is not null)
            OpenVocabularies.Check(document, path, mode, violations);

        KeepOne(schema, violations);

        return manifest;
    }

    /// <summary>
    /// Reads the documents beside the manifest: metadata, which §1 requires,
    /// and navigation, which the manifest declares exactly when it is present.
    /// </summary>
    /// <remarks>
    /// Metadata comes first because navigation needs it: §4.4 makes the
    /// content language of the metadata the language of any label that
    /// declares none of its own.
    /// </remarks>
    private static (PublicationMetadata? Metadata, PublicationNavigation? Navigation) ReadCompanionDocuments(ZipArchive archive, Manifest manifest, KomaVersion version, ProcessingMode mode, ResourceLimits profile, List<ContainerViolation> violations)
    {
        PublicationMetadata? metadata = ReadMetadata(archive, version, mode, profile, violations);
        bool present = archive.GetEntry(CorePaths.Navigation) is not null;

        // §8: the declaration and the package must agree. Whichever of the two
        // is wrong, a reader that picked one would be guessing, so neither is
        // believed and the navigation is not read.
        if (manifest.DeclaresNavigation != present)
        {
            string message = present ? "The package contains nav.xml and the manifest does not declare it (§8)." : "The manifest declares nav.xml and the package does not contain it (§8).";
            violations.Add(new ContainerViolation(ContainerViolationCode.NavigationDeclarationMismatch, CorePaths.Manifest, message));

            return (metadata, null);
        }

        if (!present)
        {
            // §1 makes nav.xml optional and §15 notes its absence: a
            // publication without one is readable but has no table of
            // contents, no page list and no landmarks.
            violations.Add(new ContainerViolation(ContainerViolationCode.NoNavigationDocument, CorePaths.Manifest, "The publication has no navigation document (§1).") { Severity = ViolationSeverity.Warning });

            return (metadata, null);
        }

        XDocument? document = KomaXml.TryLoad(archive, CorePaths.Navigation, out ContainerViolation? navigationXml, profile);

        if (navigationXml is not null)
        {
            violations.Add(navigationXml);
            return (metadata, null);
        }

        // TryLoad answers null for an absent entry, which the check above has
        // ruled out; the compiler cannot know that.
        if (document is null)
            return (metadata, null);

        ContainerViolation? schema = CheckSchema(document, CorePaths.Navigation, mode, violations);
        CoreDocumentChecks.CheckExtensions(document, CorePaths.Navigation, violations);
        CoreDocumentChecks.CheckNavigationTargets(document, manifest, CorePaths.Navigation, violations);

        PublicationNavigation? navigation = NavigationReader.Read(document, CorePaths.Navigation, version, metadata?.ContentLanguage, violations);

        if (navigation is not null)
        {
            CoreDocumentChecks.CheckPageTargets(navigation, manifest, CorePaths.Navigation, violations);
            OpenVocabularies.Check(document, CorePaths.Navigation, mode, violations);
        }

        KeepOne(schema, violations);

        return (metadata, navigation);
    }

    private static PublicationMetadata? ReadMetadata(ZipArchive archive, KomaVersion version, ProcessingMode mode, ResourceLimits profile, List<ContainerViolation> violations)
    {
        const string path = CorePaths.Metadata;
        XDocument? document = KomaXml.TryLoad(archive, path, out ContainerViolation? xml, profile);

        if (xml is not null)
        {
            violations.Add(xml);
            return null;
        }

        if (document is null)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.MissingRequiredXml, path, "The package has no metadata (§1)."));
            return null;
        }

        ContainerViolation? schema = CheckSchema(document, path, mode, violations);
        CoreDocumentChecks.CheckExtensions(document, path, violations);

        PublicationMetadata? metadata = MetadataReader.Read(document, path, version, violations);

        if (metadata is not null)
            OpenVocabularies.Check(document, path, mode, violations);

        KeepOne(schema, violations);

        return metadata;
    }

    /// <summary>
    /// Layer 2: the document against its schema (§15, §17).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only in strict mode. A package of a later minor version may use what
    /// the 0.9 schemas do not know, and forward-compatible processing
    /// (§5.0) reads it for what it can rather than refusing it for that.
    /// </para>
    /// <para>
    /// The readers still run on a document the schema refuses, so that the
    /// checks of layer 3 report what they find there too, as the reference
    /// validator reports every layer.
    /// </para>
    /// </remarks>
    private static ContainerViolation? CheckSchema(XDocument document, string path, ProcessingMode mode, List<ContainerViolation> violations)
    {
        if (mode != ProcessingMode.Strict)
            return null;

        ContainerViolation? schema = KomaSchemas.Check(document, path);

        if (schema is not null)
            violations.Add(schema);

        return schema;
    }

    /// <summary>
    /// One schema violation a document, the schema's: a reader that also
    /// found the document malformed says the same thing less precisely.
    /// </summary>
    private static void KeepOne(ContainerViolation? schema, List<ContainerViolation> violations)
    {
        if (schema is not null)
            violations.RemoveAll(v => !ReferenceEquals(v, schema) && v.Code == schema.Code && v.EntryName == schema.EntryName);
    }

    /// <summary>
    /// Checks the single <c>RootFile</c> §6 requires.
    /// </summary>
    /// <remarks>
    /// Checked, not followed. §1 fixes where the manifest lives, so the
    /// attribute has one legal value; a package naming another is at fault
    /// and says so, rather than being read from somewhere else.
    /// </remarks>
    private static bool CheckRootFile(XElement container, List<ContainerViolation> violations)
    {
        XElement[] roots = [.. container.Elements(XName.Get("RootFiles", ContainerNamespace))
                                        .Elements(XName.Get("RootFile", ContainerNamespace))];

        if (roots.Length != 1)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.SchemaInvalidContainer, CorePaths.Container, $"KOMA 0.9 requires exactly one RootFile; found {roots.Length} (§6)."));

            return false;
        }

        XElement rootFile = roots[0];
        string? path = rootFile.Attribute("full-path")?.Value;
        string? mediaType = rootFile.Attribute("media-type")?.Value;

        if (mediaType != ManifestMediaType)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.SchemaInvalidContainer, CorePaths.Container, $"RootFile/@media-type is '{mediaType}', not the literal of §2."));

            return false;
        }

        if (path != CorePaths.Manifest)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.SchemaInvalidContainer, CorePaths.Container, $"RootFile/@full-path is '{path}'; §6 requires {CorePaths.Manifest}."));

            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a defect stops the publication from being read.
    /// </summary>
    /// <remarks>
    /// Every error does, but one: §16 lets a reading system read past an
    /// unknown token, since §4.5.1 gives it a fallback or a way to be ignored.
    /// It stays an error of the publication, and is reported with the rest.
    /// </remarks>
    private static bool PreventsReading(ContainerViolation violation) => violation.Severity == ViolationSeverity.Error && violation.Code != ContainerViolationCode.UnknownToken;

    private static readonly ReadOnlyCollection<ContainerViolation> Empty = new List<ContainerViolation>().AsReadOnly();

    private static PackageOpenResult Rejected(ContainerViolation violation) => new()
    {
        Outcome = PackageOpenOutcome.Rejected,
        Violations = new List<ContainerViolation> { violation }.AsReadOnly()
    };
}
