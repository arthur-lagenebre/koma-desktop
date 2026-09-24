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

### More tests for the interface

`Koma.Desktop.Tests` covers the shelf, reading, the pages window, the edit
window and the report window. What is not covered yet: the import window's
choices, the folders window, and fitting and zooming while reading.

### Thumbnails in the pages window

The pages window lists identifiers: a reader edits `p014` without seeing what
it is. A grid of page thumbnails, decoded as the covers are, would put the
roles, the spans and the chapters under the eye — the same model, seen rather
than named.

### Taking a page out, and putting pages in another order

Nothing removes a page from a publication or moves one. Both touch the
manifest, the spine, the landmarks of §9.3, the page targets of §9.2 and the
resources at once, and must leave exactly one front cover (§8.4) and a package
that reads back. It is the one item here that belongs to `Koma.Core` rather
than to the interface, and the one that can damage a file, so it is worth its
own increment and its own tests.

### The accessibility of the application itself

The application edits the accessibility metadata of publications and has never
been checked for its own: keyboard navigation across the shelf and the forms,
labels a screen reader can announce, contrast, focus visible where it is. Not
knowing whether it is usable without a mouse is the part to fix first, since it
is also the cheapest to test — unplug the mouse for ten minutes.

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
- **Encrypting a library.** Keeping publications off the shelf hides them from
  the shelf and nothing else, which is what was asked for. Encryption is not
  planned: it would mean key management, a decision about what a lost
  passphrase costs, and encryption outside the package, since §2.1 wants an
  unencrypted `mimetype` entry inside it.
- **A TIFF decoder.** Skia reads no TIFF where Pillow does, so a CBZ the
  reference converter handles can be one this one refuses, naming the page. A
  decoder means a dependency for a format a CBZ rarely holds; worth reopening
  the day one turns up often.
