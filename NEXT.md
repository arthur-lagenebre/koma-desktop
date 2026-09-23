# What is left to do

Not a plan with dates. A list of what is known to be missing, why it matters,
and where to start, so that picking one up does not begin with rediscovering
it. Items leave this file when they are done, or when they are decided against
and recorded in the README under Settled.

## Next

### Pin the image the workflow linter runs on, upstream

The `workflows` job of `koma-desktop` failed with no workflow changed, on an
image announced as migrating, and was fixed by pinning `runs-on: ubuntu-24.04`.
The `koma` repository has the same job, unpinned, and will fail the same way at
its next run. The same one-line change, with the same comment.

### Tests for the interface

Every defect of the last weeks showed itself at runtime and none of them at
build time or in the tests: the language switch that recursed until the stack
gave out, the spread drawn at nothing when a publication was reopened, a call
left with two arguments where the method had gained a third. `Koma.Core`,
`Koma.Library` and `Koma.Imaging` are covered; `Koma.Desktop` is not covered at
all.

Avalonia runs headless for tests. A handful of smoke tests would catch the
class of defect that keeps getting through: the shelf draws for a library of a
few entries, choosing one opens it, switching language redraws without
recursing, the pages window fills its form from a package. Start with the test
project and one test; the value is in the first one, which proves the harness
works.

## Worth doing

### Keep some publications off the shelf

Adult or otherwise explicit publications sit on the same shelf as everything
else, and a shelf is looked at by whoever is in front of the screen. A way to
keep some of them out of sight — a folder marked private, a publication marked
private, revealed by a passphrase for the session — would make the library
usable in a room with other people in it.

What such a thing can honestly promise has to be said plainly in the interface,
or it promises what it cannot keep:

- it hides publications from the shelf, and nothing more;
- the files stay where they are, readable by anything that opens files, and the
  covers stay in the thumbnail folder unless they are removed with them;
- a passphrase that unlocks a view is not a passphrase that encrypts anything.

Encrypting a library is a different feature, with key management, a decision
about what a lost passphrase costs, and §2.1 to respect — a KOMA package is a
ZIP with an unencrypted `mimetype` entry, so encryption belongs outside the
package rather than inside it. Worth separating the two from the start: hiding
is a shelf feature and can be built now; encryption is a project of its own.

### Show everything wrong with a publication that will not open

A refused publication shows one line in its card and the rest in a tooltip. The
opener reports every violation it finds, and a reader who wants to repair a
file wants them all, with the entry each concerns. The import report already
does this for conversions; the same window would do for an opening.

### The accessibility of the application itself

The application edits the accessibility metadata of publications and has never
been checked for its own: keyboard navigation across the shelf and the forms,
labels a screen reader can announce, contrast, focus visible where it is. Not
knowing whether it is usable without a mouse is the part to fix first, since it
is also the cheapest to test — unplug the mouse for ten minutes.

### `Koma.Cli` beyond `koma info`

The command-line tool stopped at `koma info` while the core learned schema
validation, page checks and writing. It could validate a package the way
`check_corpus.py` does, convert a CBZ, and report a publication's faults — a
second way into the same checks, scriptable over a whole library, and useful
for anyone who wants to test a package without a window.

## Deferred, and why

- **Signing the executables.** Windows calls them unknown publishers. SignPath
  Foundation signs open-source projects for free, and asks for a code-signing
  policy on the project page, multi-factor authentication for everyone, and an
  approver for each signing. The application has to be made by a person, not by
  CI.
- **An installer, and the `.koma` file association.** A double-click that opens
  the application needs an installer, which needs signing to not be worse than
  the plain executable it replaces. It waits on the item above.
- **Translating what `Koma.Core` reports.** Its violations and conversion notes
  are the vocabulary of the specification, read against a bug report or the
  reference converter's output. The interface is translated; these are not, on
  purpose.
- **A TIFF decoder.** Skia reads no TIFF where Pillow does, so a CBZ the
  reference converter handles can be one this one refuses, naming the page. A
  decoder means a dependency for a format a CBZ rarely holds; worth reopening
  the day one turns up often.
