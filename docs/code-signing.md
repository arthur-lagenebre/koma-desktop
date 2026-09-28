# Code signing policy

This page exists because the SignPath Foundation programme asks every project
it signs for to publish one, and because whoever downloads a binary deserves
to know who built it and who vouches for it.

## What is signed

The Windows binaries of a release — `koma-desktop-<version>-win-x64.exe` and
`koma-<version>-win-x64.exe` — published on the
[releases page](https://github.com/arthur-lagenebre/koma-desktop/releases).

The Linux binaries are not signed: the platform has no equivalent of
Authenticode, and `SHA256SUMS` beside them says what the build produced.

## Who is the publisher

**SignPath Foundation.** The certificate is issued to the Foundation and not
to this project or to its maintainer, so a signature says that the Foundation
vouches for a binary built from this repository — not that a named person
stands behind it. That is the trade the programme offers, and it is worth
saying plainly rather than letting a dialog imply otherwise.

Free code signing is provided by [SignPath.io](https://signpath.io), with a
certificate by the [SignPath Foundation](https://signpath.org).

## Who holds which role

Following SignPath's role model, and with one maintainer, the roles fall to
the same person:

- **Authors** — trusted to change source and build scripts without further
  review: [@arthur-lagenebre](https://github.com/arthur-lagenebre).
- **Reviewers** — none: there is no second maintainer to review, which this
  page says rather than pretending otherwise.
- **Approvers** — authorise each signing request: @arthur-lagenebre.

Every account with access to this repository has two-factor authentication
enabled.

## How a binary comes to be signed

1. A tag is pushed, and GitHub Actions builds the binaries from the public
   source of this repository, on a runner nobody has touched.
2. The unsigned binaries are submitted to SignPath as a signing request, whose
   origin — repository, branch, commit, workflow — SignPath verifies against
   what it was told to trust.
3. An Approver approves the request by hand. Nothing is signed without that.
4. The signed binaries are attached to the release.

The private key is generated and held in SignPath's hardware security module.
It is never on a runner, on a maintainer's machine, or in this repository.

## What is not covered

The source of this project is entirely open, including the conformance corpus
it is tested against, which comes from
[arthur-lagenebre/koma](https://github.com/arthur-lagenebre/koma) as a
submodule and is maintained by the same person. The dependencies — Avalonia,
SkiaSharp — are open source and are shipped as they come, unsigned by this
project.

## Privacy

The application makes no network request of any kind, sends nothing anywhere,
and collects nothing. It writes its library — the watched folders, the reading
positions, the covers it made — under `%APPDATA%\KOMA` on Windows and
`~/.config/KOMA` on Linux, and reads the files it is pointed at. There is no
account, no telemetry and no update check.

## Reporting something suspicious

A binary claiming to be KOMA that is unsigned, signed by someone else, or
whose checksum does not match `SHA256SUMS` on the release page should be
reported by opening an issue on this repository, or privately through GitHub's
security advisories if it looks like an attack rather than a mistake.
