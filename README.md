# koma-desktop

A cross-platform desktop library manager, reader and editor for
[KOMA](https://github.com/arthur-lagenebre/koma) publications.

Written in C# with Avalonia. Windows is the primary target; Linux is supported.

## Status

`Koma.Core` goes from a file to a pagination. It refuses the entry count before
building the archive (§13.1), checks the mimetype entry field by field (§2.1),
resolves the version against the portal of §5.0 before judging anything, reads
the container, manifest, metadata and navigation, and enforces the ZIP profile
of §3. Navigation labels carry the language §4.4 gives them, and
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

Of the 42 packages in the upstream corpus, 33 are refused with the code the
corpus gives. The remaining nine are a defect no opener can see — an entry that
under-declares its size is only caught when something reads it — and the eight
packages that have nothing to refuse. `ConformanceCorpusTests` asserts those
counts, so a case that moves is reported rather than quietly reclassified.

`Koma.Desktop` is a first reader: it opens a publication from a file picker or
the command line, paginates it for the window (§10.1), and turns spreads with
the arrow keys along the reading direction, Page Up and Page Down, Home and
End. A Contents panel lists the table of contents and the landmarks of
`nav.xml` in the reader's language, and a choice takes the reader to the spread
that holds it; the counter shows the page-list labels on screen. Pages are laid
out from their declared sizes (§16) and decoded on a worker, one at a time, as
the reader reaches them. The spreads either side follow, so that a turn usually
finds its pages ready, and a page queued for a spread the reader has left is
dropped before it is decoded. Warnings from opening, the faults of the pages on
screen and a withheld page are reported in a status line.

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
src/Koma.Cli              command-line front end over Koma.Core
src/Koma.Desktop          the Avalonia reader
src/Koma.Imaging          page loading and decoding over SkiaSharp — no UI
tests/Koma.Core.Tests     xUnit, driven by the upstream conformance corpus
tests/Koma.Imaging.Tests  SkiaSharp and the decoder, over the corpus page images
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
dotnet build
dotnet test
```

If you cloned without `--recurse-submodules`:

```
git submodule update --init --recursive
```

## Testing against the corpus

`external/koma/corpus/expected.json` states, for each of the 42 packages,
whether a conforming implementation must report it valid, warning or error.
It is normative by example. The test suite walks it directly rather than
defining its own fixtures.

The pairing cases in `external/koma/corpus/spread-cases.json` are written by
hand from the prose of §10 and are never regenerated from an implementation.
The tests read that file directly, as the reference implementation does, so
the two are graded against the same text.

The page images of the corpus serve as decoding fixtures too. They were
written by Pillow, so SkiaSharp is never judged on its own output.

## Open technical questions

1. **RELAX NG.** .NET validates XSD and DTD, not RELAX NG. Options are Trang
   conversion to XSD (lossy), the unmaintained `Commons.Xml.Relaxng`, or
   hand-written structural checks. §17 notes the schemas are only layer 2 of
   four in any case.

## Debts

None of these block anything.

- **§3 names no Unicode version.** `CaseFoldingTable` is generated from
  Unicode 16.0; the reference validator uses whatever the Python running it
  carries. They agree on the machine that generated the table and may not
  agree elsewhere. Either the specification names a version, or the table
  moves to the `koma` repository.
- **The corpus does not exercise every code of §15.1**, which criterion 3 of
  §5.0.1 will eventually require.
- **Two §2.1 faults have no code of their own**: a data descriptor and extra
  fields on the mimetype entry. Both are reported as `mimetype-content` with
  the reason in the message.
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

## Licence

Apache License 2.0. See [LICENSE](LICENSE).

The KOMA specification itself lives in a separate repository under its own
terms; this licence covers only the code in this repository.
