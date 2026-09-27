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

`Koma.Desktop.Tests` covers the shelf and its series, reading with its fit and
zoom, the pages, edit, import, folders and report windows, and the two long
pieces of work over real files — an edit written into three publications, an
archive converted into one. What is not covered: the conversion of a whole
folder, which is the same work with more of it, and anything that needs a
picker, which belongs to the platform rather than to this application.

### The last corners of §7

The edit window carries §7 but for three things: the ratings of §7.14, which
are kept as a conversion wrote them and only the warnings beside them are
edited; the identifiers and the release identity of §7.2, which an editor has
no business changing by hand; and what `Publication` holds after the place —
edition, dates, physical format — which nothing this application writes fills
in. None of it is urgent, and the first is arguably right as it stands.

### Walk the accessibility passes again after a change

Both passes of [`docs/accessibility-pass.md`](docs/accessibility-pass.md) have
been walked: the Narrator announces the shelf, the reading and the forms
usefully, and the contrast and the focus hold up in both themes. The document
stays, since every window that changes is a reason to walk them again.

What neither pass covered, and what is left: the reading view is one picture,
so a reader who cannot see it hears the spread but cannot move within it, page
by page rather than spread by spread; and a report is read as one block of
text rather than as a list of faults. Neither is urgent, and both would be
judged better by someone who reads this way every day than by us.

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
