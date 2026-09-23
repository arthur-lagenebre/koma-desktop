using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Koma.Core.Importing;
using Koma.Core.Rendering;
using Koma.Core.Writing;

namespace Koma.Imaging;

/// <summary>
/// A CBZ converted: every entry of the package, and what the conversion had
/// to assume or give up.
/// </summary>
/// <param name="Entries">Every entry but <c>mimetype</c>, ready for <see cref="PackageWriter"/>.</param>
public sealed record CbzConversion(ReadOnlyDictionary<string, byte[]> Entries, ReadingDirection Direction, int PageCount, ReadOnlyCollection<string> Notes);

/// <summary>
/// Converts a CBZ into a KOMA package, as <c>tools/cbz_to_koma.py</c> does.
/// </summary>
/// <remarks>
/// <para>
/// The converter never invents information. What KOMA requires and the CBZ
/// cannot supply is derived from the archive, taken from the options, or
/// assumed out loud in the notes, in the reference converter's words less its
/// command-line options.
/// </para>
/// <para>
/// Where every page passes through untouched, the package is the reference
/// converter's entry for entry. Where a page has to be re-encoded, the two
/// agree on every decision and on no byte of that page, nor on the identifier
/// derived from it.
/// </para>
/// </remarks>
public static class CbzConverter
{
    private const string Container = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Container xmlns="urn:koma:container" version="0.9">
          <RootFiles>
            <RootFile full-path="koma/manifest.xml" media-type="application/vnd.koma.manifest+xml"/>
          </RootFiles>
        </Container>

        """;

    private const string ManifestNamespace = "urn:koma:manifest";
    private const string NavigationNamespace = "urn:koma:navigation";

    private static readonly Dictionary<string, string> RoleOfPageType = new(StringComparer.Ordinal)
    {
        ["FrontCover"] = "front-cover",
        ["InnerCover"] = "inner-cover",
        ["Roundup"] = "recap",
        ["Story"] = "story",
        ["Advertisement"] = "advertisement",
        ["Editorial"] = "editorial",
        ["Letters"] = "letters",
        ["Preview"] = "preview",
        ["BackCover"] = "back-cover",
        ["Other"] = "other",
        ["Deleted"] = "other"
    };

    // The roles that make a landmark of the same name (§9.3); the first page
    // of the story makes body-start.
    private static readonly string[] LandmarkRoles = ["front-cover", "inner-cover", "title-page", "back-cover"];

    /// <summary>Converts a CBZ file into a KOMA file.</summary>
    /// <exception cref="InvalidDataException">
    /// The archive holds no image, or a page nothing here can decode.
    /// </exception>
    public static CbzConversion Convert(string cbz, string koma, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(koma);

        CbzConversion conversion;

        using (ZipArchive archive = ZipFile.OpenRead(cbz))
            conversion = Convert(archive, options);

        using FileStream output = File.Create(koma);
        PackageWriter.Write(output, conversion.Entries);

        return conversion;
    }

    /// <inheritdoc cref="Convert(string, string, ConversionOptions)"/>
    public static CbzConversion Convert(ZipArchive cbz, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(cbz);
        ArgumentNullException.ThrowIfNull(options);

        var notes = new List<string>();

        if (options.Modified is null && ComicInfoProjection.ModifiedDate(cbz) is { } modified)
        {
            notes.Add($"modified date taken from the archive timestamps ({modified})");
            options = options with { Modified = modified };
        }

        CbzContents contents = CbzReader.Read(cbz);

        if (contents.Pages.Count == 0)
            throw new InvalidDataException("no image found in the archive");

        ComicInfo comicInfo = ComicInfo.Parse(contents.ComicInfo, notes);
        Dictionary<int, IReadOnlyDictionary<string, string>> described = Described(comicInfo, contents.Pages.Count, notes);
        List<Page> pages = Normalize(cbz, contents, described, options, notes);

        AssignCover(pages, notes);

        (XDocument metadata, ReadingDirection direction) = ComicInfoProjection.Metadata(comicInfo, options, [.. pages.Select(p => p.Digest)], notes);
        XDocument? navigation = options.Navigation ? Navigation(pages, options.PageList, notes) : null;

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["META-INF/container.xml"] = Encoding.UTF8.GetBytes(Container),
            ["koma/metadata.xml"] = CanonicalXml.Write(metadata),
            ["koma/manifest.xml"] = CanonicalXml.Write(Manifest(pages, options.Checksums, navigation is not null))
        };

        if (navigation is not null)
            entries["koma/nav.xml"] = CanonicalXml.Write(navigation);

        foreach (Page page in pages)
            entries[page.Href] = page.Data;

        // The last page's number, padded as its name is: 005, or 0165.
        string last = CbzReader.PageId(pages.Count - 1, pages.Count)[1..];
        notes.Add($"pages renumbered to pages/001..{last} in archive order; the source names are not preserved");

        // Kept unchanged, never rewritten: §1 says ComicInfo is never
        // normative for KOMA, and a reader that still wants it gets the file
        // it came with.
        if (options.KeepComicInfo && contents.ComicInfo is { } original)
        {
            entries["ComicInfo.xml"] = original;

            if (comicInfo.Pages.Count > 0)
                notes.Add($"ComicInfo Page/@Image counts from 0 while page files count from 1: Image=\"0\" is {pages[0].Href[..^Path.GetExtension(pages[0].Href).Length]}, Image=\"N\" is page N+1");
        }

        return new CbzConversion(entries.AsReadOnly(), direction, pages.Count, notes.AsReadOnly());
    }

    /// <summary>
    /// ComicInfo's page entries by image index, which counts from zero.
    /// </summary>
    private static Dictionary<int, IReadOnlyDictionary<string, string>> Described(ComicInfo comicInfo, int images, List<string> notes)
    {
        var described = new Dictionary<int, IReadOnlyDictionary<string, string>>();

        foreach (IReadOnlyDictionary<string, string> page in comicInfo.Pages)
        {
            // Whitespace and a sign are allowed, as Python's int() allows
            // them, so that the two tools match the same pages.
            if (int.TryParse(page.GetValueOrDefault("Image", "-1"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                described[index] = page;
        }

        if (comicInfo.Pages.Count > 0 && comicInfo.Pages.Count != images)
            notes.Add($"ComicInfo declares {comicInfo.Pages.Count} pages but the archive holds {images}; page metadata is matched by index and may be off");

        return described;
    }

    private static List<Page> Normalize(ZipArchive cbz, CbzContents contents, Dictionary<int, IReadOnlyDictionary<string, string>> described, ConversionOptions options, List<string> notes)
    {
        var pages = new List<Page>(contents.Pages.Count);

        for (int index = 0; index < contents.Pages.Count; index++)
        {
            string source = contents.Pages[index].Entry;

            byte[] data = Read(cbz, source);

            // Refused rather than lost: a publication missing a page is worse
            // than none, and the reason is one a person can act on.
            NormalizedPage normalized = PageNormalizer.Normalize(data, source) ?? throw new InvalidDataException($"{source}: {Format(data)} is a format this converter cannot decode; convert the page to PNG or JPEG first. The reference converter, tools/cbz_to_koma.py, does convert it.");

            notes.AddRange(normalized.Notes);

            IReadOnlyDictionary<string, string> meta = described.GetValueOrDefault(index) ?? new Dictionary<string, string>();
            string? role = RoleOfPageType.GetValueOrDefault(meta.GetValueOrDefault("Type", string.Empty));
            int span = string.Equals(meta.GetValueOrDefault("DoublePage"), "true", StringComparison.OrdinalIgnoreCase) ? 2 : 1;

            if (span == 1 && options.InferSpreads && normalized.Width > normalized.Height * 1.2)
            {
                span = 2;
                notes.Add($"{source}: landscape page marked page-span=2");
            }

            pages.Add(new Page
            {
                Id = CbzReader.PageId(index, contents.Pages.Count),
                Href = CbzReader.PageName(index, contents.Pages.Count, normalized.MediaType),
                Normalized = normalized,
                Role = role,
                Span = span,
                Digest = System.Convert.ToHexStringLower(SHA256.HashData(normalized.Data)),
                Source = source
            });
        }

        return pages;
    }

    /// <summary>
    /// §8.4 wants one front cover: the first ComicInfo names, else the first
    /// page. Every page ComicInfo leaves untyped is story.
    /// </summary>
    private static void AssignCover(List<Page> pages, List<string> notes)
    {
        List<Page> covers = [.. pages.Where(p => p.Role == "front-cover")];

        if (covers.Count == 0)
        {
            pages[0].Role = "front-cover";
            notes.Add($"no FrontCover in ComicInfo; {pages[0].Source} taken as the cover");
        }
        else if (covers.Count > 1)
        {
            foreach (Page extra in covers.Skip(1))
                extra.Role = "inner-cover";

            notes.Add($"{covers.Count} pages marked FrontCover; kept the first, demoted the others to inner-cover");

            foreach (Page page in pages.Skip(1).Where(p => Path.GetFileName(p.Source).Contains("cover", StringComparison.OrdinalIgnoreCase)))
                notes.Add($"{page.Source} looks like a cover but sorts after {pages[0].Source}; the archive order was kept");
        }
        else if (covers[0] != pages[0])
        {
            notes.Add("the ComicInfo front cover is not the first page; the spine keeps the archive order");
        }

        List<Page> untyped = [.. pages.Where(p => p.Role is null)];

        foreach (Page page in untyped)
            page.Role = "story";

        if (untyped.Count > 0)
            notes.Add($"{untyped.Count} of {pages.Count} pages carry no ComicInfo type and default to 'story'");
    }

    private static XDocument Manifest(List<Page> pages, bool checksums, bool declaresNavigation)
    {
        var root = new XElement(M("Manifest"), new XAttribute("xmlns", ManifestNamespace), new XAttribute("version", "0.9"), new XAttribute("metadata", "koma/metadata.xml"));

        if (declaresNavigation)
            root.Add(new XAttribute("navigation", "koma/nav.xml"));

        var resources = new XElement(M("Resources"));

        foreach (Page page in pages)
        {
            var item = new XElement(M("Item"),
                new XAttribute("id", page.Id),
                new XAttribute("href", page.Href),
                new XAttribute("media-type", page.Normalized.MediaType),
                new XAttribute("width", page.Normalized.Width),
                new XAttribute("height", page.Normalized.Height),
                new XAttribute("roles", page.Role!));

            if (page.Span == 2)
                item.Add(new XAttribute("page-span", "2"));

            if (checksums)
                item.Add(new XElement(M("Checksum"), new XAttribute("algorithm", "sha-256"), page.Digest));

            resources.Add(item);
        }

        root.Add(resources, new XElement(M("Spine"), pages.Select(p => new XElement(M("ItemRef"), new XAttribute("item", p.Id)))));

        return new XDocument(root);
    }

    /// <summary>
    /// Landmarks from the page roles, and a page list only when asked for.
    /// </summary>
    private static XDocument Navigation(List<Page> pages, bool pageList, List<string> notes)
    {
        var root = new XElement(N("Navigation"), new XAttribute("xmlns", NavigationNamespace), new XAttribute("version", "0.9"));

        if (pageList)
        {
            notes.Add("page list: labels number the scan order, which is not the printed pagination");

            var list = new XElement(N("PageList"));
            int logical = 0;

            foreach (Page page in pages)
            {
                if (page.Span == 2)
                {
                    list.Add(Target(page, logical + 1, "left"), Target(page, logical + 2, "right"));
                    logical += 2;
                }
                else
                {
                    list.Add(Target(page, ++logical, null));
                }
            }

            root.Add(list);
        }

        var landmarks = new XElement(N("Landmarks"));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Page page in pages.Where(p => LandmarkRoles.Contains(p.Role) && seen.Add(p.Role!)))
            landmarks.Add(Landmark(page.Role!, page));

        if (pages.FirstOrDefault(p => p.Role == "story") is { } story)
            landmarks.Add(Landmark("body-start", story));

        root.Add(landmarks);

        return new XDocument(root);
    }

    private static XElement Target(Page page, int label, string? side)
    {
        var target = new XElement(N("PageTarget"), new XAttribute("item", page.Id), new XAttribute("label", label.ToString(CultureInfo.InvariantCulture)));

        if (side is not null)
            target.Add(new XAttribute("spread-position", side));

        return target;
    }

    private static XElement Landmark(string type, Page page) => new(N("Landmark"), new XAttribute("type", type), new XAttribute("item", page.Id));

    /// <summary>
    /// What a file that could not be decoded says it is, from its first bytes.
    /// </summary>
    /// <remarks>
    /// TIFF is the one a CBZ turns up in practice: Pillow reads it and Skia
    /// does not, so this is the one page the reference converter keeps and
    /// this one refuses. Naming it is the difference between a reader who
    /// knows what to convert and one who does not.
    /// </remarks>
    private static string Format(byte[] data) => data switch
    {
        [0x49, 0x49, 0x2A, 0x00, ..] or [0x4D, 0x4D, 0x00, 0x2A, ..] => "TIFF",
        [0x49, 0x49, 0x2B, 0x00, ..] or [0x4D, 0x4D, 0x00, 0x2B, ..] => "BigTIFF",
        [0x38, 0x42, 0x50, 0x53, ..] => "Photoshop",
        [0x00, 0x00, 0x01, 0x00, ..] => "ICO",
        _ => "what looks like no image"
    };

    private static byte[] Read(ZipArchive archive, string entry)
    {
        using Stream stream = archive.GetEntry(entry)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }

    private static XName M(string name) => XName.Get(name, ManifestNamespace);

    private static XName N(string name) => XName.Get(name, NavigationNamespace);

    private sealed class Page
    {
        public required string Id { get; init; }

        public required string Href { get; init; }

        public required NormalizedPage Normalized { get; init; }

        public string? Role { get; set; }

        public required int Span { get; init; }

        public required string Digest { get; init; }

        public required string Source { get; init; }

        public byte[] Data => Normalized.Data;
    }
}
