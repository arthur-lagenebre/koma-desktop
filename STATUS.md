# Where this stands

Written as the reader reached the point where a package can be opened,
checked and paginated. Read it before picking the next piece up.

## Done

**Checked against the upstream corpus.** `ConformanceCorpusTests` runs
`PackageOpener` over all 31 packages and sorts them into three buckets: 16
detected with the spelling §15.1 and the corpus give, 0 detected under the
wrong name, 15 outside what the opener reaches. The bucket counts are
asserted, so a corpus case that moves is reported rather than quietly filed as
out of scope.

The last bucket is the one that matters: those 15 packages are defective, and
they must open. Refusing them would be a false positive, not early diligence.

**Opening, in the order the specification requires.** The entry count is
refused before the archive is built, from the end-of-central-directory record
(§13.1). The mimetype entry is checked next, once the central directory can
tell an absent entry from a misplaced one (§2.1). The version portal runs
before any judgement of validity (§5.0), so a package from another era of the
format comes back as `UnsupportedVersion` with no violations, even when it
carries faults this build would otherwise report.

That order is load-bearing. Reversing any two steps produces a reader that
refuses the right files for the wrong reasons.

**Pairing (§10.4).** A transcription of the normative pseudocode, not a
rewriting of it. It agrees with the reference implementation on all fifteen
upstream fixtures, which are written by hand from the prose and had never been
seen by this code. `SpreadPaginator.Pseudocode` carries the algorithm verbatim
and a test compares it with the specification in the submodule.

**The manifest (§8).** `ManifestReader` reads `manifest.xml` and checks it
against itself: identifiers, paths below `pages/`, the closed media-type
vocabulary, dimensions with the lexical strictness of §4.3, page spans, spine
targets, and the four front-cover rules of §8.4. `Manifest.ToSpineEntries` is
the join between the two halves of the reader — §10.3 draws an entry's
effective position from both the `ItemRef` and the `Item`, so they are brought
together there rather than in the paginator.

**Severity.** §8.8 allows a resource outside the spine and only asks that it
be noticed, so a reader without the distinction refuses publications the
specification calls readable. Only errors stop a package from opening.

**`koma info`.** Prints version, mode, the manifest's paths and counts, and
the violations with their codes and severities. Exit status carries the §5.0
distinction out to the shell: 1 rejected, 2 version not read by this build.

## Next

1. **The remaining layer-3 checks.** Four corpus codes need a core document
   other than the manifest: `navigation-target-outside-spine` (`nav.xml`),
   `accessibility-hazard-conflict` (`metadata.xml`),
   `unnamespaced-element-in-extensions`, and `tokenlist-duplicate`, which
   turned out to repeat a token somewhere other than `roles`.
2. **Layer 4**, which needs the image bytes: media type against signature,
   declared dimensions, animation, checksums, residual EXIF orientation.
3. **The Avalonia interface**, and with it the two questions still open in the
   README: RELAX NG validation, and WebP with EXIF orientation.

## Debts

None of these block anything.

- **§3 names no Unicode version.** `CaseFoldingTable` is generated from
  Unicode 16.0; `check_corpus.py` uses whatever the Python running it carries.
  They agree on the machine that generated the table and may not agree on a CI
  runner. Either the specification names a version, or the table moves to the
  `koma` repository.
- **The corpus does not exercise the 23 codes of §15.1.** Criterion 3 of
  §5.0.1 asks that it cover every error listed, so this will have to be paid.
- **`declared-size-mismatch` is invisible at open time.** The lying entry is
  never read, so the package opens; `BoundedReadStream` catches it at the point
  of use. Catching it earlier means decompressing everything, which is the
  wrong trade. Worth remembering when the interface has to show an error that
  arrives mid-read.
- **Two §2.1 faults have no code of their own**: a data descriptor and extra
  fields on the mimetype entry. Both are reported as `mimetype-content` with
  the reason in the message.
- **`CheckResourcesInSpine` depends on running after `CheckFrontCover`.** It
  stays silent when the spine is already at fault, so that the reader does not
  name a cause and a symptom with equal weight. The order is real, not
  incidental.

## Conventions

One logical expression per line. Arrow bodies, single-call statements and
switch arms are not folded, whatever their length. Constructs that are
multi-line by nature — object and collection initialisers, switch expressions
with several arms — keep their lines.

No trailing commas. `var` only where the type is apparent on the right-hand
side. Braces on multi-line bodies only. Comments say why, not what.
