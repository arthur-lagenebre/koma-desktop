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

### Walk the accessibility passes again after a change

Both passes of [`docs/accessibility-pass.md`](docs/accessibility-pass.md) have
been walked: the Narrator announces the shelf, the reading and the forms
usefully, and the contrast and the focus hold up in both themes. The document
stays, since every window that changes is a reason to walk them again.

What neither pass covered has since been done: Ctrl with the up and down
arrows walks the pages of a spread one at a time and says each one, and a
report is a list of faults rather than one paragraph. Both were written
without a screen reader to hand, so both are worth listening to at the next
pass.

## Deferred, and why

- **Signing the executables.** Applied to the SignPath Foundation programme in
  September 2026 and turned down: the programme asks for public visibility —
  stars, forks, contributors, outside mentions, sustained activity — and a
  project a week old has none of it. They said plainly it was no judgment on
  the work, and invited a fresh application once the project is better known.
  So: nothing is signed, the release says why, and this is worth trying again
  when there is something to show. [`docs/code-signing.md`](docs/code-signing.md)
  stays as it is, and what would follow an approval is written down there.

  Until then a binary is what CI built from public source, with `SHA256SUMS`
  beside it, and Windows says unknown publisher — which the download section
  says before a reader meets the dialog. A paid certificate would remove the
  warning tomorrow; it has not seemed worth it for a comics reader handed out
  on GitHub.
- **An installer, and the `.koma` file association.** A double-click that opens
  the application needs an installer, which needs signing to not be worse than
  the plain executable it replaces — an unsigned installer asks for more trust
  than an unsigned executable, not less. It waits on the item above.
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
