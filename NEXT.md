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

### One test runner rather than two

Avalonia's headless integration is built on xunit v3, which runs itself rather
than through VSTest, while the other three suites are on v2, which does not.
`dotnet test` runs one or the other, so the interface suite is run on its own,
in its own CI step. Moving the three older suites to v3 would bring everything
back under one command; it is mechanical, and worth doing when someone has an
afternoon rather than in the middle of something else.

### More tests for the interface

`Koma.Desktop.Tests` covers the shelf, the pages window, the edit window and
the report window. What is not covered yet: reading itself — opening a
publication, turning its pages, fitting and zooming — which lives in the main
window, and the main window reads the library of whoever runs the tests.
Giving it its store rather than fetching one would make it testable, and is
the next step for this.

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
