# koma-desktop

A cross-platform desktop library manager, reader and editor for
[KOMA](https://github.com/arthur-lagenebre/koma) publications.

Written in C# with Avalonia. Windows is the primary target; Linux is supported.

## Status

`Koma.Core` goes from a file to a pagination. It refuses the entry count before
building the archive (§13.1), checks the mimetype entry field by field (§2.1),
resolves the version against the portal of §5.0 before judging anything, reads
the container, manifest and metadata, and enforces the ZIP profile of §3. The
pairing algorithm of §10.4 agrees with the reference implementation on all
fifteen upstream fixtures, which are written by hand from the prose and never
regenerated from an implementation.

Page resources are checked in a pass of their own rather than at open time:
each check reads a whole image, so running them on open would decompress the
publication before the first page could be shown, and make scanning a library
cost as much as reading it.

Of the 31 packages in the upstream corpus, 25 are refused with the code the
corpus gives. The remaining six are a defect no opener can see — an entry that
under-declares its size is only caught when something reads it — and the five
packages that have nothing to refuse. `ConformanceCorpusTests` asserts those
counts, so a case that moves is reported rather than quietly reclassified.

There is no user interface yet.

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
src/Koma.Core        format model, reader, writer, rendering, limits — no UI
src/Koma.Cli         command-line front end over Koma.Core
tests/Koma.Core.Tests  xUnit, driven by the upstream conformance corpus
external/koma        git submodule: the specification, schemas and corpus
```

`Koma.Core` must never reference a UI package. That separation is what keeps
the conformance work testable without a running window.

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

`external/koma/corpus/expected.json` states, for each of the 28 packages,
whether a conforming implementation must report it valid, warning or error.
It is normative by example. The test suite walks it directly rather than
defining its own fixtures.

The pairing cases in `external/koma/tools/spread_cases.py` are written by hand
from the prose of §10 and are never regenerated from an implementation. They
are ported to xUnit here for the same reason.

## Open technical questions

These are unresolved:

1. **RELAX NG.** .NET validates XSD and DTD, not RELAX NG. Options are Trang
   conversion to XSD (lossy), the unmaintained `Commons.Xml.Relaxng`, or
   hand-written structural checks. §17 notes the schemas are only layer 2 of
   four in any case.
2. **WebP and EXIF.** §16 makes WebP support mandatory and requires EXIF
   orientation to be ignored. Verify SkiaSharp's behaviour on both before
   building the render pipeline around it.

## Debts

None of these block anything.

- **§3 names no Unicode version.** `CaseFoldingTable` is generated from
  Unicode 16.0; the reference validator uses whatever the Python running it
  carries. They agree on the machine that generated the table and may not
  agree elsewhere. Either the specification names a version, or the table
  moves to the `koma` repository.
- **The corpus does not exercise every code of §15.1**, which criterion 3 of
  §5.0.1 will eventually require.
- **Two warnings the corpus declares are not produced here**:
  `no-navigation-document` and `private-use-token`. The packages carrying them
  open, which the tests accept, but the corpus expects a warning.
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

## Settled

**ZIP writing.** §2.1 fixes bytes 0–61 of the file, and it was not established
that `System.IO.Compression` could satisfy that. It can: `MimetypeEntryTests`
asserts the local header field by field, on Windows and on Linux, so no
third-party ZIP writer is needed. The tests stay because a future runtime could
change what `CompressionLevel.NoCompression` emits.

## Licence

Apache License 2.0. See [LICENSE](LICENSE).

The KOMA specification itself lives in a separate repository under its own
terms; this licence covers only the code in this repository.
