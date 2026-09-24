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

### Try the application with a screen reader running

The names are written and nobody has heard them. That is the test that counts:
a name can be present, announced, and still say nothing useful — "Series" read
out after a combo box has already said "Rivage" is noise, and a card that
announces four sentences in a row is a card nobody will sit through.

Under Windows, the Narrator starts with Ctrl+Windows+Enter. Half an hour is
enough for the pass that matters: reach the shelf, walk it with the arrows,
open a publication, turn a page, open the edit window and change a field —
mouse unplugged, eyes on something else. What comes back from that is worth
more than another round of names.

### The rest of the accessibility

What is not done at all: contrast, which has never been measured; the focus,
which is wherever Fluent puts it and has never been looked at; and the reading
view, where a spread is a picture with nothing to announce — the alternative
text of §8.7 is written into packages by this application and read back by
nothing in it.

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
