# koma-desktop

A cross-platform desktop library manager, reader and editor for
[KOMA](https://github.com/arthur-lagenebre/koma) publications.

Written in C# with Avalonia. Windows is the primary target; Linux is supported.

## Status

`Koma.Core` goes from a file to a pagination. It refuses the entry count before
building the archive (§13.1), checks the mimetype entry field by field (§2.1),
resolves the version against the portal of §5.0 before judging anything, reads
the container, manifest, metadata — its titles included, since a library lists
publications by name — and navigation, validates each core document against its
RELAX NG schema, enforces the ZIP profile of §3, and reports every code §15.1
defines that an opener can see. It writes core documents back in the canonical
form of §14.1, which `CanonicalXmlTests` checks against every document of the
corpus. `PackageWriter` puts them in a file the way §2.1 and §14.2 ask: the
mimetype entry first and stored, the rest in one byte-wise order under one
fixed timestamp, so that the same publication written twice gives the same
bytes. `PublicationEditor` edits the metadata of a publication in place: it
changes what it is asked to and nothing else, stamps the modified date of
§7.2.1, reads the result back before writing, and replaces the file only once
the new one is complete, copying the pages it is not changing straight from the
old file to the new one. It edits a page the same way — its roles, its span,
its side of the spread, its alternative text, the chapter it opens in the table
of contents, the numbers printed on it — and moves the landmarks of §9.3 with
the roles they come from, so that a manifest and its navigation never say two
different things. Navigation labels carry the language §4.4 gives them, and
`NavigationLabel.Choose` picks the one to show from the reader's languages;
regions (§9.4) are not read, which §16 allows. `OpenVocabularies` judges the
tokens of all twenty-five open vocabularies of §4.5 in whichever document holds
them. An unknown token is an error of the publication that §16 lets a reader
read past, so the opener opens the publication with its fallback and carries
the error; every other error still refuses it. The pairing algorithm of §10.4
agrees with the reference implementation on all fifteen upstream fixtures,
which are written by hand from the prose and never regenerated from an
implementation.

Page resources are checked in a pass of their own rather than at open time:
each check reads a whole image, so running them on open would decompress the
publication before the first page could be shown, and make scanning a library
cost as much as reading it.

`PageLoader` is that pass as a reading system runs it, one page at a time as
the reader reaches it. It applies §16: a page with no bytes, bytes that do not
decode, or a size beyond the pixel limits is withheld and keeps its place in
the spine; a page with any other fault of the resource layer is decoded and
shown, and its fault travels with it for the interface to report.

Of the 67 packages in the upstream corpus, 56 are refused with the code the
corpus gives. The remaining eleven are a defect no opener can see — an entry
that under-declares its size is only caught when something reads it — and the
ten packages that have nothing to refuse. `ConformanceCorpusTests` asserts those
counts, so a case that moves is reported rather than quietly reclassified.

`CbzConverter` turns a CBZ into a KOMA package with the decisions of
`tools/cbz_to_koma.py`: which entries are pages and in which order, how each
page is made fit for a package, how ComicInfo projects onto the metadata, which
page is the cover, which landmarks the roles give. What it cannot find out it
assumes out loud, in the reference converter's words. Where every page passes
through untouched the package is the converter's entry for entry, identifier
included; where a page is re-encoded the two agree on every decision and on no
byte of that page, since Skia and Pillow write different files.
`CbzConverterTests` holds it to the converter's output on the three example
archives.

`Koma.Library` keeps what the reader needs to list publications without
opening them again: one JSON file and a folder of cover thumbnails, under the
user's application data. It is a cache, rebuilt by a scan from the folders it
watches, apart from the reading positions, which are kept as the first item of
the last spread shown rather than as a spread number: pagination changes with
the shape of the window, and an item does not. A publication is opened again
only when its path, size or modification time has changed.

`Koma.Cli` is the same reader without a window. `koma info` says what a
publication says about itself; `koma check` adds layer 4, reading the pages a
package can open without, and answers 0 when a publication conforms and 1 when
it does not, so a shell can walk a library; `koma convert` writes a CBZ as a
publication, never over one that is there, and prints what it assumed in the
words the reference converter uses.

`Koma.Desktop` opens on the shelf: the publications of the watched folders,
with their covers, scanned in the background at startup and whenever the
watched folders change; Folders adds and removes them, and marks one private;
and a folder removed takes its publications off the shelf without touching a
file. Covers are decoded on a worker, one at a time and at the size they are
shown, so the shelf is drawn at once and fills from the top. The shelf searches
titles, series and file names, case and accents aside, and orders by title, by
series, each in the order of its volume numbers, or by what was read last; the
order it is left in is the order it opens on. Choosing one opens it where it
was last left, fitted and zoomed as it was left too, since a dense manga and a
large-format album are not read the same way; a Resume button takes up the
publication read last. Escape goes back to the shelf. Import CBZ takes files or a whole folder, subfolders included, and asks once
whether the original ComicInfo travels with the packages, and how the
publications are read and what they may do to a reader, which no CBZ says; it
writes the packages beside their archives or into a folder of the reader's,
keeping whatever tree a folder of archives sat in, never over an existing file,
checksums included, and shows what each conversion assumed or refused as it
happens, before the shelf is rescanned. Edit metadata, from a card's menu or
from the publication on screen, changes the title, language, reading direction,
series, and what the publication says about reading it — how it is taken in,
what it may do to a reader, a sentence for someone deciding whether they can
read it — writing only what changed. Pages lists the pages of the publication
and changes what the package says about one — its role, its span, its side of
the spread, the chapter it opens, the number printed on it, what a reader who
cannot see it is told — and a right-click edits the page under the pointer. It
also opens a publication from a file picker or the command line, paginates it
for the window (§10.1), and turns spreads with the arrow keys along the reading
direction, Page Up and Page Down, Home and End. A spread fits the window or its
width, in its own proportions; Ctrl with the wheel or with plus, minus and zero
zooms it, Space reads down a spread taller than the window before turning it,
the wheel turns the page when there is nothing to scroll, a click turns towards
the half it lands in and the side buttons of a mouse browse back and forth, and
F11 reads full screen, which Escape leaves. A Contents panel lists the table of
contents and the landmarks of `nav.xml` in the reader's language, and a choice
takes the reader to the spread that holds it; the counter shows the page-list
labels on screen. Pages are laid out from their declared sizes (§16) and
decoded on a worker, one at a time, as the reader reaches them. The spreads
either side follow, so that a turn usually finds its pages ready, and a page
queued for a spread the reader has left is dropped before it is decoded.
Warnings from opening, the faults of the pages on screen and a withheld page
are reported in a status line. The interface is in English or in French, as the
reader chooses, and the library remembers which; what Koma.Core reports stays
in English, being the vocabulary of the specification. A publication or a
folder kept private stays off the shelf until Show private is pressed, which is
never remembered: it hides what the room sees over a shoulder, and touches
neither the files nor their covers on disk. A card's menu checks a publication
and reports every fault the opener finds, with the entry each one is about; a
publication that will not open answers a click with that report, being the only
useful thing to do with it.

The format itself is at pre-release draft `0.9`. Per §5.0 of the specification,
a reader supporting one `0.x` version **must reject every other `0.x`**, and
files produced against `0.9` must not be archived. This application targets
`0.9` and only `0.9`.

## Conformance goals

The specification defines four conformance classes in §16. This project aims at
two of them:

| Class | Scope here |
| --- | --- |
| KOMA Reading System | §16, including the rendering model of §10 |
| KOMA Authoring Tool | §16, including the canonical serialization of §14.1 |

Validation (§15) is implemented only as far as the reader and editor need it.
This is **not** a conforming KOMA Validator: that requires all four layers of
§15 and the reporting rules of §5.4.

## Layout

```
src/Koma.Core             format model, reader, writer, rendering, limits — no UI
src/Koma.Cli              command line: info, check, convert
src/Koma.Desktop          the Avalonia reader
src/Koma.Imaging          page loading and decoding over SkiaSharp — no UI
src/Koma.Library          the library: its index, its covers, its scan — no UI
tests/Koma.Cli.Tests      the command line, over corpus packages and archives
tests/Koma.Core.Tests     xUnit, driven by the upstream conformance corpus
tests/Koma.Imaging.Tests  SkiaSharp and the decoder, over the corpus page images
tests/Koma.Desktop.Tests  the interface, drawn headless
tests/Koma.Library.Tests  the library, over a folder of corpus packages
tests/Koma.TestSupport    finds the corpus for the other test projects
external/koma             git submodule: the specification, schemas and corpus
```

`Koma.Core` and `Koma.Imaging` must never reference a UI package. That
separation is what keeps the conformance work, and the decoding it guards,
testable without a running window. `Koma.Core` references no imaging library
either: what a page declares about itself is read from its header by hand, and
decoding belongs to the layer above.

## Building

Requires the .NET SDK (see `Directory.Build.props` for the target framework).

```
git clone --recurse-submodules <this repo>
cd koma-desktop

./check.ps1                       # build, then the tests, stopping at the first
                                  # failure; -Configuration Release for what CI runs

dotnet build                      # or the two by hand
dotnet test                       # every suite, the interface one included
```

If you cloned without `--recurse-submodules`:

```
git submodule update --init --recursive
```

## Releases

A tag starting with `v` publishes the reader:

```
git tag v0.1.0
git push origin v0.1.0
```

`release.yml` tests the tagged commit on Windows and Linux, publishes each as
one self-contained executable with its native libraries inside, and makes a
GitHub release of the two files with a `SHA256SUMS` beside them. The file is
the application: nothing to install, nothing to unpack. A publish that leaves
a second file fails the build. On Linux, a browser download loses the
executable bit, which `chmod +x` gives back. Run by hand from the Actions tab, the workflow
stops before the release and leaves the builds as artifacts, to try one
before tagging it.

The executables are not signed. Windows SmartScreen warns about them the
first time they run, and says so until a certificate signs them.

The library lives in the user's application data, `%APPDATA%\KOMA` on Windows
and `~/.config/KOMA` on Linux. A debug build keeps its own, in
`KOMA (development)`, so that a released copy starts empty rather than with
the folders and positions of a development session.

## Testing against the corpus

`external/koma/corpus/expected.json` states, for each of the 67 packages,
whether a conforming implementation must report it valid, warning or error.
It is normative by example. The test suite walks it directly rather than
defining its own fixtures.

The pairing cases in `external/koma/corpus/spread-cases.json` are written by
hand from the prose of §10 and are never regenerated from an implementation.
The tests read that file directly, as the reference implementation does, so
the two are graded against the same text.

The page images of the corpus serve as decoding fixtures too. They were
written by Pillow, so SkiaSharp is never judged on its own output.

## Debts

None of these block anything.

- **Check order is load-bearing in two places.** `CheckResourcesInSpine` and
  `CheckNavigationTargets` stay silent when the spine is already at fault, so
  that the reader does not name a cause and a symptom with equal weight. Both
  run after the checks they defer to.

## Conventions

One logical expression per line. Arrow bodies, single-call statements and
switch arms are not folded, whatever their length. Constructs that are
multi-line by nature — object and collection initialisers, switch expressions
with several arms — keep their lines.

No trailing commas. `var` only where the type is apparent on the right-hand
side. Braces on multi-line bodies only. Comments say why, not what.

Project files carry only what differs from `Directory.Build.props`. The target
framework, nullability and implicit usings are set there and nowhere else.

## Settled

**ZIP writing.** §2.1 fixes bytes 0–61 of the file, and it was not established
that `System.IO.Compression` could satisfy that. It can: `MimetypeEntryTests`
asserts the local header field by field, on Windows and on Linux, so no
third-party ZIP writer is needed. The tests stay because a future runtime could
change what `CompressionLevel.NoCompression` emits.

**WebP and EXIF.** §16 makes WebP mandatory and §8.2 requires EXIF orientation
to be ignored. SkiaSharp 3.119.4, the version Avalonia.Skia 12.1 loads, decodes
static WebP. On orientation its two decoding roads disagree: `SKCodec` and
`SKBitmap.Decode` report the EXIF origin and render pixels as stored, while
`SKImage.FromEncodedData` applies it and swaps width and height. Pages are
therefore decoded through `SKCodec` only, and Avalonia is never handed encoded
bytes. `SkiaSharpDecodingTests` pins all three, along with a quirk of the
binding: `SKCodec.FrameCount` is 0 for any still image, where native Skia says
1, so animation is judged by `PageImageReader` and never by a frame count.

**Colour.** §8.3 asks a reading system to apply an embedded profile and to
honour the PNG `sRGB`, `gAMA` and `cHRM` chunks. `SKCodec` does both, in all
three formats, as soon as it is given sRGB as the destination space; without
a destination it leaves the pixels in their stored space. On a profile that
does not parse, Skia falls back to sRGB, which was checked against Skia itself:
no corpus page carries a broken profile yet. `PageDecoder` therefore names sRGB, and
`SkiaSharpDecodingTests` pins both behaviours against `valid-icc-profiles` and
`valid-png-gamma`, whose notes give the colour to expect.

**Core document paths.** §1 fixes them, and the attributes of §6 and §8 that
name them must carry exactly those values. `CorePaths` holds the four; the
opener checks the attributes against it and reads from it, and never follows
an attribute. A manifest whose `@navigation` disagrees with the presence of
`koma/nav.xml`, in either direction, is refused with
`navigation-declaration-mismatch`.

**RELAX NG.** .NET validates XSD and DTD, not RELAX NG, and XSD 1.0 cannot
say what the extension mechanism says: any namespace but the four of KOMA.
`RelaxNgSchema` validates by derivatives, after James Clark's algorithm, from
the XML form of the schemas the specification repository publishes, which the
assembly carries. It implements what those schemas use — patterns, name
classes with exceptions, eight XML Schema datatypes whose `\s` is XML Schema's
four characters and not every Unicode space — and refuses anything else when a
schema loads. The algorithm was checked against libxml2 before it was written,
on the reference instances, the 36 rejections, the corpus and three thousand
random mutations, with no disagreement; `RelaxNgSchemaTests` holds the C# to
the same files. The opener validates every core document in strict mode only:
a later minor version may use what the 0.9 schemas do not know. The readers
still run on a document the schema refuses, so that layer 3 reports what it
finds there as well, and a document gets one schema violation, the schema's,
which says where it stopped matching.

**Unicode version.** §3 folds and normalizes entry names with the data of
Unicode 16.0.0. `CaseFoldingTable` is generated from exactly that release.
Normalization comes from the runtime, which takes it from ICU or from Windows
and so from whatever version the host carries; Unicode's stability policies
keep both fixed for every character once assigned, so every host agrees with
16.0.0 on every name made of characters it assigns, which is all §3 asks.

**TIFF pages.** Skia reads no TIFF where Pillow does, so a CBZ the reference
converter handles can be one this one refuses. It refuses rather than leaving
the page out: a publication missing a page is worse than a conversion that did
not happen, and §16 has no way to report a page that never existed. The
message names the format and says that `tools/cbz_to_koma.py` converts it. A
decoder would mean a dependency — ImageSharp under a split licence, or
Magick.NET and its native code — for a format a CBZ rarely holds; it is worth
reopening the day one turns up often.

**Checks that repeat the schema.** The readers refuse by hand much of what the
schemas refuse: closed vocabularies, lexical forms, the sections §7 and §8
require. That repetition is deliberate. Forward-compatible processing (§5.0)
skips the schema, since a document of a later minor version may hold what the
0.9 schemas do not know, and the readers are all that is left there. Each
reader is public API, and nothing obliges its caller to validate first. And a
message that names the attribute and its section is worth more to whoever has
to fix the file than a path into the document. `SchemaOverlapTests` keeps the
repetition honest: it holds the schema to what the readers catch, and names
what only the readers catch — an id declared twice, an href outside `pages/`,
a version disagreeing with the container, a second main title — which no
RELAX NG schema can express.

## Licence

Apache License 2.0. See [LICENSE](LICENSE).

The KOMA specification itself lives in a separate repository under its own
terms; this licence covers only the code in this repository.
