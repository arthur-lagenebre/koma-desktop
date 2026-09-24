using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Koma.Core.Rendering;

namespace Koma.Core.Importing;

/// <summary>
/// What a conversion is told rather than finds out.
/// </summary>
/// <param name="Direction">Overrides what ComicInfo says, or does not say, about the reading direction.</param>
/// <param name="Language">Overrides <c>LanguageISO</c>.</param>
/// <param name="FallbackTitle">The title when ComicInfo has neither a title nor a series.</param>
/// <param name="Modified">The <c>modified</c> date, when it is not to be taken from the archive.</param>
/// <param name="Checksums">Whether every page carries its SHA-256 (§8.6).</param>
/// <param name="InferSpreads">Whether a landscape page ComicInfo does not mark is taken for a double page.</param>
/// <param name="KeepComicInfo">Whether the original <c>ComicInfo.xml</c> travels with the package, unchanged.</param>
/// <param name="Navigation">Whether to write <c>nav.xml</c>, with the landmarks the page roles give.</param>
/// <param name="NumberFromFileName">
/// Whether a volume number is taken from the digits an archive's name starts
/// with, for a collection that numbers its files and not its metadata.
/// </param>
/// <param name="Number">The volume number to use where ComicInfo gives none.</param>
/// <param name="AccessModes">
/// How the publications are taken in (§7.13). A CBZ says nothing about
/// reading its pages, so this comes from whoever converts it or not at all.
/// </param>
/// <param name="AccessibilityHazards">What the publications may do to a reader (§7.13).</param>
/// <param name="PageList">
/// Whether <c>nav.xml</c> numbers the pages. Off unless asked: §9.2 means the
/// printed number, and a CBZ only knows the order of its scans.
/// </param>
public sealed record ConversionOptions(
    ReadingDirection? Direction = null,
    string? Language = null,
    string FallbackTitle = "Untitled",
    string? Modified = null,
    bool Checksums = false,
    bool InferSpreads = false,
    bool KeepComicInfo = true,
    bool Navigation = true,
    bool PageList = false,
    bool NumberFromFileName = false,
    string? Number = null,
    IReadOnlyList<string>? AccessModes = null,
    IReadOnlyList<string>? AccessibilityHazards = null);

/// <summary>
/// Projects a ComicInfo onto the metadata of §7, as <c>tools/cbz_to_koma.py</c>
/// projects it, to the byte once written canonically.
/// </summary>
/// <remarks>
/// <para>
/// The converter never invents information. What KOMA requires and ComicInfo
/// cannot supply is derived from the archive, taken from the options, or
/// written as an assumption in the notes, in the reference converter's words,
/// less its references to command-line options this tool does not have.
/// </para>
/// <para>
/// The elements and attributes come in the converter's order, which is the
/// order §7 gives them; <see cref="Writing.CanonicalXml"/> then lays them out.
/// </para>
/// </remarks>
public static partial class ComicInfoProjection
{
    private const string Namespace = "urn:koma:metadata";

    /// <summary>
    /// The UUID namespace of converted publications: version 5 on the URL
    /// namespace, as the reference converter derives it.
    /// </summary>
    private static readonly Guid ConvertedNamespace = NameBasedUuid(new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8"), "https://koma.invalid/ns/cbz");

    private static readonly (string Field, string Role)[] Credits =
    [
        ("Writer", "writer"),
        ("Penciller", "penciller"),
        ("Inker", "inker"),
        ("Colorist", "colorist"),
        ("Letterer", "letterer"),
        ("CoverArtist", "cover-artist"),
        ("Editor", "editor"),
        ("Translator", "translator")
    ];

    private static readonly HashSet<string> Mapped = new(StringComparer.Ordinal)
    {
        "Title", "Series", "Number", "Count", "Summary", "Notes", "Year", "Month",
        "Day", "Publisher", "Imprint", "Genre", "LanguageISO", "Manga",
        "BlackAndWhite", "AgeRating", "Web", "Pages",
        "Writer", "Penciller", "Inker", "Colorist", "Letterer", "CoverArtist", "Editor", "Translator"
    };

    // What KOMA derives from the package itself: carrying it over would make
    // a second source of truth rather than add information.
    private static readonly HashSet<string> Redundant = new(StringComparer.Ordinal) { "PageCount" };

    /// <summary>
    /// The metadata document and the reading direction it settles on.
    /// </summary>
    /// <param name="pageDigests">
    /// The SHA-256 of every page as written, in spine order, lowercase hex:
    /// the identifier is derived from them, so that converting the same
    /// archive twice gives the same publication (§7.2.1).
    /// </param>
    public static (XDocument Metadata, ReadingDirection Direction) Metadata(ComicInfo comicInfo, ConversionOptions options, IReadOnlyList<string> pageDigests, List<string> notes)
    {
        ArgumentNullException.ThrowIfNull(comicInfo);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pageDigests);
        ArgumentNullException.ThrowIfNull(notes);

        NoteUnmapped(comicInfo, notes);

        string title = Title(comicInfo, options, notes);
        string language = options.Language ?? comicInfo["LanguageISO"] ?? Undetermined(notes);
        ReadingDirection direction = Direction(comicInfo, options, notes);
        string? series = comicInfo["Series"];

        // The declaration is written out: a document built in memory has no
        // xmlns attribute of its own, and the canonical writer puts
        // declarations where the document carries them.
        var root = new XElement(X("Metadata"), new XAttribute("xmlns", Namespace), new XAttribute("version", "0.9"), new XAttribute(XNamespace.Xml + "lang", language));

        root.Add(new XElement(X("Identifiers"), new XElement(X("Identifier"), new XAttribute("scheme", "uuid"), new XAttribute("primary", "true"), $"urn:uuid:{Identifier(pageDigests)}")));
        root.Add(new XElement(X("Titles"), new XElement(X("Title"), new XAttribute("type", "main"), title)));
        root.Add(new XElement(X("Languages"), new XElement(X("Language"), new XAttribute("role", "content"), language)));

        if (series is not null)
            root.Add(new XElement(X("Collections"), Collection(comicInfo, series, Number(comicInfo, options, notes))));

        Add(root, "Contributors", Contributors(comicInfo, notes));
        Add(root, "Descriptions", Descriptions(comicInfo));
        Add(root, "Publication", Publication(comicInfo, options));
        Add(root, "Subjects", SplitCredits(comicInfo["Genre"]).Select(genre => new XElement(X("Subject"), new XAttribute("type", "genre"), genre)));

        root.Add(new XElement(X("Reading"), new XAttribute("direction", direction == ReadingDirection.RightToLeft ? "rtl" : "ltr"), new XAttribute("spread", "auto")));

        if (Content(comicInfo, notes) is { } content)
            root.Add(content);

        if (Accessibility(options, notes) is { } accessibility)
            root.Add(accessibility);

        if (comicInfo["AgeRating"] is { } rating && rating != "Unknown")
            root.Add(new XElement(X("Ratings"), new XElement(X("Rating"), new XAttribute("scheme", "comicinfo-agerating"), new XAttribute("value", rating))));

        Add(root, "Links", (comicInfo["Web"] ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(url => new XElement(X("Link"), new XAttribute("rel", "other"), new XAttribute("href", url))));

        // Provenance records only what the conversion knows. The digitization
        // method is absent on purpose: a CBZ may hold a scan or a born-digital
        // export, and nothing in the archive says which.
        root.Add(new XElement(X("Provenance"), new XElement(X("Note"), "Converted from a CBZ archive.")));

        return (new XDocument(root), direction);
    }

    /// <summary>
    /// The newest date among the archive's entries, as an ISO date: what the
    /// reference converter takes for <c>modified</c> when it is not told one.
    /// </summary>
    /// <remarks>
    /// From the archive and not from the clock, so that converting the same
    /// CBZ twice yields the same release identity (§7.2.1). A ZIP time has no
    /// zone, so its fields are taken as they are.
    /// </remarks>
    public static string? ModifiedDate(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        return archive.Entries.Count == 0 ? null : archive.Entries.Max(e => e.LastWriteTime.DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The identifier of a converted publication: a version 5 UUID on the
    /// SHA-256 of its page digests, in the reference converter's namespace.
    /// </summary>
    /// <remarks>
    /// Two tools that write the same pages give the same URN, which holds
    /// only while no page is re-encoded: Skia and Pillow write different bytes.
    /// </remarks>
    public static Guid Identifier(IReadOnlyList<string> pageDigests)
    {
        ArgumentNullException.ThrowIfNull(pageDigests);

        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(pageDigests))));

        return NameBasedUuid(ConvertedNamespace, digest);
    }

    /// <summary>A version 5 UUID (RFC 9562): SHA-1 of the namespace and the name.</summary>
    /// <remarks>
    /// SHA-1 is what version 5 is, not a choice made here: any other hash
    /// gives another UUID, and the point is to give the reference converter's.
    /// Nothing is protected by it; it names a publication.
    /// </remarks>
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "RFC 9562 defines version 5 UUIDs over SHA-1; the hash names a publication and protects nothing.")]
    internal static Guid NameBasedUuid(Guid space, string name)
    {
        byte[] input = [.. space.ToByteArray(bigEndian: true), .. Encoding.UTF8.GetBytes(name)];
        byte[] hash = SHA1.HashData(input);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    /// <summary>
    /// The volume number, from ComicInfo or from the name of the archive.
    /// </summary>
    /// <remarks>
    /// A collection that numbers its files and not its metadata is common
    /// enough — 1 - Ante demonium.cbz — and taking the number from the name
    /// is a guess, so it is said out loud and asked for rather than assumed.
    /// </remarks>
    private static string? Number(ComicInfo comicInfo, ConversionOptions options, List<string> notes)
    {
        if (comicInfo["Number"] is { } stated)
            return stated;

        if (options.Number is not { } given)
            return null;

        string note = $"ComicInfo gives no volume number; {Repr(given)} taken from the name of the archive";

        if (!notes.Contains(note, StringComparer.Ordinal))
            notes.Add(note);

        return given;
    }

    private static string Title(ComicInfo comicInfo, ConversionOptions options, List<string> notes)
    {
        if (comicInfo["Title"] is { } title)
            return title;

        string? series = comicInfo["Series"];

        if (series is not null && Number(comicInfo, options, notes) is { } number)
        {
            string built = $"{series} {number}";
            notes.Add($"no Title in ComicInfo; built {Repr(built)} from Series and Number");
            return built;
        }

        if (series is not null)
        {
            notes.Add($"no Title in ComicInfo; using the series name {Repr(series)}");
            return series;
        }

        notes.Add($"no title in ComicInfo, using {Repr(options.FallbackTitle)}");
        return options.FallbackTitle;
    }

    private static string Undetermined(List<string> notes)
    {
        notes.Add("no language in ComicInfo, using the undetermined tag 'und'");
        return "und";
    }

    private static ReadingDirection Direction(ComicInfo comicInfo, ConversionOptions options, List<string> notes)
    {
        string manga = comicInfo["Manga"] ?? "Unknown";

        if (options.Direction is { } direction)
            return direction;

        if (manga == "YesAndRightToLeft")
            return ReadingDirection.RightToLeft;

        notes.Add($"ComicInfo Manga={Repr(manga)} does not state a reading direction; assuming ltr.");
        return ReadingDirection.LeftToRight;
    }

    /// <summary>
    /// What the publication says about reading it (§7.13), from what the
    /// conversion was told.
    /// </summary>
    /// <remarks>
    /// Nothing in a CBZ says how its pages are taken in, so this is asked of
    /// whoever converts rather than guessed. Said out loud in the notes,
    /// since it is the one part of the metadata the archive did not carry.
    /// </remarks>
    private static XElement? Accessibility(ConversionOptions options, List<string> notes)
    {
        string[] modes = [.. (options.AccessModes ?? []).Select(m => m.Trim()).Where(m => m.Length > 0)];
        string[] hazards = [.. (options.AccessibilityHazards ?? []).Select(h => h.Trim()).Where(h => h.Length > 0)];

        // §7.13 wants at least one access mode in the section, so hazards
        // alone have nowhere to sit.
        if (modes.Length == 0)
            return null;

        notes.Add($"accessibility declared by the importer: {string.Join(", ", modes.Concat(hazards))}");

        return new XElement(
            X("Accessibility"),
            modes.Select(m => new XElement(X("AccessMode"), m)),
            hazards.Select(h => new XElement(X("AccessibilityHazard"), h)));
    }

    private static XElement Collection(ComicInfo comicInfo, string series, string? position)
    {
        var collection = new XElement(X("Collection"), new XAttribute("type", "series"));

        if (position is not null)
            collection.Add(new XAttribute("position", position));

        if (comicInfo["Count"] is { } total)
            collection.Add(new XAttribute("total", total));

        collection.Add(new XElement(X("Name"), series));

        return collection;
    }

    /// <summary>
    /// One contributor per name, with every role it is credited with, in the
    /// order ComicInfo first names them.
    /// </summary>
    private static List<XElement> Contributors(ComicInfo comicInfo, List<string> notes)
    {
        var roles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach ((string field, string role) in Credits)
        {
            foreach (string person in SplitCredits(comicInfo[field]))
            {
                if (!roles.TryGetValue(person, out List<string>? held))
                {
                    roles[person] = held = [];
                    order.Add(person);
                }

                if (!held.Contains(role))
                    held.Add(role);
            }
        }

        if (order.Count > 0)
            notes.Add("ComicInfo does not distinguish people from organizations; every credit is written as type=\"person\": " + string.Join(", ", order));

        return [.. order.Select(person => new XElement(X("Contributor"), new XAttribute("type", "person"), new XAttribute("roles", string.Join(';', roles[person])), new XElement(X("Name"), person)))];
    }

    private static List<XElement> Descriptions(ComicInfo comicInfo)
    {
        var descriptions = new List<XElement>();

        if (comicInfo["Summary"] is { } summary)
            descriptions.Add(new XElement(X("Description"), new XAttribute("type", "summary"), summary));

        if (comicInfo["Notes"] is { } note)
            descriptions.Add(new XElement(X("Description"), new XAttribute("type", "note"), note));

        return descriptions;
    }

    private static List<XElement> Publication(ComicInfo comicInfo, ConversionOptions options)
    {
        var publication = new List<XElement>();

        if (comicInfo["Publisher"] is { } publisher)
            publication.Add(new XElement(X("Publisher"), publisher));

        if (comicInfo["Imprint"] is { } imprint)
            publication.Add(new XElement(X("Imprint"), imprint));

        if (PublicationDate(comicInfo) is { } date)
            publication.Add(new XElement(X("Date"), new XAttribute("event", "publication"), date));

        if (options.Modified is { } modified)
            publication.Add(new XElement(X("Date"), new XAttribute("event", "modified"), modified));

        return publication;
    }

    private static string? PublicationDate(ComicInfo comicInfo)
    {
        if (!int.TryParse(comicInfo["Year"], CultureInfo.InvariantCulture, out int year))
            return null;

        bool hasMonth = int.TryParse(comicInfo["Month"], CultureInfo.InvariantCulture, out int month);
        bool hasDay = int.TryParse(comicInfo["Day"], CultureInfo.InvariantCulture, out int day);

        if (hasMonth && hasDay)
            return string.Create(CultureInfo.InvariantCulture, $"{year:0000}-{month:00}-{day:00}");

        return hasMonth ? string.Create(CultureInfo.InvariantCulture, $"{year:0000}-{month:00}") : string.Create(CultureInfo.InvariantCulture, $"{year:0000}");
    }

    private static XElement? Content(ComicInfo comicInfo, List<string> notes)
    {
        string? blackAndWhite = comicInfo["BlackAndWhite"];

        if (blackAndWhite == "Yes")
            return new XElement(X("Content"), new XAttribute("color-mode", "monochrome"));

        if (blackAndWhite != "No")
            return null;

        notes.Add("ComicInfo BlackAndWhite=No rules out monochrome but does not distinguish color from mixed; writing color-mode=\"color\"");
        return new XElement(X("Content"), new XAttribute("color-mode", "color"));
    }

    private static void NoteUnmapped(ComicInfo comicInfo, List<string> notes)
    {
        string[] unmapped = [.. comicInfo.Fields.Keys.Where(f => !Mapped.Contains(f) && !Redundant.Contains(f)).Order(StringComparer.Ordinal)];

        if (unmapped.Length > 0)
            notes.Add("ComicInfo fields with no KOMA equivalent, kept only in the ComicInfo passthrough: " + string.Join(", ", unmapped));

        string[] redundant = [.. comicInfo.Fields.Keys.Where(Redundant.Contains).Order(StringComparer.Ordinal)];

        if (redundant.Length > 0)
            notes.Add("ComicInfo fields recomputed from the package rather than trusted: " + string.Join(", ", redundant) + ". The passthrough copy still carries the original values, which is the section 15 warning 'ComicInfo projection inconsistent with KOMA'");
    }

    private static IEnumerable<string> SplitCredits(string? value) => value is null ? [] : CreditSeparator().Split(value).Select(part => part.Trim()).Where(part => part.Length > 0);

    [GeneratedRegex("[,;]")]
    private static partial Regex CreditSeparator();

    private static void Add(XElement root, string container, IEnumerable<XElement> children)
    {
        XElement[] items = [.. children];

        if (items.Length > 0)
            root.Add(new XElement(X(container), items));
    }

    /// <summary>
    /// A value quoted as the reference converter's notes quote it: in single
    /// quotes, or in double quotes when it holds a single quote and no double.
    /// </summary>
    private static string Repr(string value)
    {
        string escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal);

        if (escaped.Contains('\'', StringComparison.Ordinal) && !escaped.Contains('"', StringComparison.Ordinal))
            return $"\"{escaped}\"";

        return $"'{escaped.Replace("'", "\\'", StringComparison.Ordinal)}'";
    }

    private static XName X(string name) => XName.Get(name, Namespace);
}
