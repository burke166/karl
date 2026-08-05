# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Each `Karl.*`
package (and `Karl.Cli`) is versioned independently in its own `.csproj`;
this file tracks releases of the repository as a whole, and each entry below
notes which package it affects.

## [1.3.0] - 2026-08-04

### Added

- `Karl.Abstractions`: `IAttachmentSource`, `EmailAttachment` (with
  `EmailAttachment.FromFile`/`FromStream` factories), and a new
  `EmailMessage.Attachments` collection. `FromFile`-backed attachments reopen
  a fresh file stream on every send (safe to reuse across a `--csv` batch);
  `FromStream`-backed attachments read the caller's stream once and never
  close it. `IAttachmentSource.OwnsStream` tells a transport whether it
  opened the stream itself (and must dispose it) or received a caller-owned
  stream (and must not). See `docs/design/02_send_attachments.md`.
- `Karl`: `MailBuilder.AttachFile`/`AttachStream`, mirroring the existing
  fluent `To`/`Cc`/`Bcc`/body methods.
- `Karl.Transport.Smtp`: attachments are added to the outgoing MIME message
  via MailKit's `BodyBuilder.Attachments`; file-backed attachment streams are
  disposed after the send completes, caller-supplied streams are left open.
- `Karl.Transport.File` / `Karl.Transport.StdOut`: an `Attachments:` line
  listing each attachment's file name and content type is added to the
  rendered output when a message has attachments (metadata only — neither
  transport reads or writes the actual attachment bytes).
- `Karl.Cli`: `--attach`/`-a` option on `send`, `file`, and `preview`,
  repeatable to attach multiple local files. A missing `--attach` path fails
  the whole command before anything renders or sends, the same as a missing
  `--markdown`/`--model`/`--csv` path. Composes with `--csv`: the same
  attachments are applied to every row.

## [1.2.0] - 2026-08-02

### Added

- `Karl.Cli`: `--csv`, `--to-column`, and `--name-column` options on `send`,
  `file`, and `preview`, enabling one personalized email per CSV row (mass
  email / mail-merge). Rows are read via `ComputerCodeBlue.Csv`'s
  `CsvFile.ReadDynamic` and rendered through the existing Scriban
  subject/body pipeline the same way `--model` JSON already worked - no
  renderer changes needed. `--csv` is mutually exclusive with `--to` and
  `--model`. Blank-recipient rows are skipped; per-row send failures are
  logged and counted rather than aborting the batch; a summary line reports
  sent/skipped/failed counts. See `docs/design/01_csv_mass_email.md`.
- `Karl.Cli`: batch sends thread a `CancellationToken` through the row loop
  and into `IEmailService.SendAsync`, so Ctrl+C can interrupt a long-running
  CSV batch.
- `.github/workflows/publish-nuget.yml`: publishes all 9 packable projects
  (the `Karl.Cli` dotnet tool, the `Karl` convenience package, and the
  individual `Karl.*` libraries) to NuGet.org on `v*.*.*` tag push.
- `scripts/publish-local.ps1`: packs and pushes the same 9 packages to a
  local or UNC-path NuGet feed, for testing releases before tagging.

### Changed

- `Karl.Cli`: bumped `ComputerCodeBlue.Csv` from 1.2.1 to 1.3.0 to pick up
  `CsvFile.ReadDynamic`.

### Fixed

- `Karl.Cli`: `--to` and `--tls` both declared the short alias `-t`, which
  could throw or silently misbehave in `send`'s option parsing. `--tls` now
  uses `-tls` instead; `-t` remains `--to`'s alias.

### Documentation

- Added `docs/design/01_csv_mass_email.md`, now marked Implemented.
- `src/Karl.Cli/README.md`: new "Mass Email Using a CSV File" section.
