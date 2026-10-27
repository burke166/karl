# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Each `Karl.*`
package (and `Karl.Cli`) is versioned independently in its own `.csproj`;
this file tracks releases of the repository as a whole, and each entry below
notes which package it affects.

## [2.0.1] - 2026-10-27

### Security

- `Karl.Transport.Smtp`: updated MailKit 4.17.0 → 4.18.2. MailKit's MimeKit dependency now
  resolves `System.Security.Cryptography.Pkcs` 10.0.13, fixing two .NET denial-of-service
  advisories in the previously resolved 8.0.1 (net8.0) / 10.0.0 (net10.0):
  [CVE-2026-84008](https://github.com/advisories/GHSA-846f-f764-76mx) (High) and
  [CVE-2026-70162](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) (Moderate).
  `Karl`, `Karl.Extensions.Microsoft`, and `Karl.Cli` pulled the vulnerable package in through
  `Karl.Transport.Smtp`, so all four bump to 2.0.1 and now require `Karl.Transport.Smtp` 2.0.1.

### Changed

- Tests: migrated from the deprecated `xunit` 2.9.3 to `xunit.v3.mtp-off` 4.0.2 (with
  `xunit.runner.visualstudio` 4.0.1). The `mtp-off` variant keeps the existing VSTest-based
  `dotnet test` workflow unchanged on the .NET 10 SDK. No published package is affected.

## [2.0.0] - 2026-09-17

### Changed

- **Breaking:** `Karl.Transport.Smtp`: `SmtpTransportOptions.SecurityMode` is now the
  `SmtpSecurityMode` enum (`StartTlsRequired` (default), `None`, `ImplicitTls`,
  `StartTlsWhenAvailable`) instead of a plain `string`. Code that assigns a string literal
  (`options.SecurityMode = "StartTlsRequired"`) no longer compiles — use the enum member
  (`options.SecurityMode = SmtpSecurityMode.StartTlsRequired`) instead.
  `IConfiguration`/`karl.json`/`KARL_`-prefixed environment variable sources are unaffected: the
  configuration binder already parses a string value into an enum by member name, so
  `"SecurityMode": "StartTlsRequired"` in a JSON config file keeps working unchanged.
- **Breaking:** an unrecognized `SecurityMode` value now throws `ArgumentOutOfRangeException` from
  `SmtpTransport.SendAsync` instead of silently falling back to `StartTlsRequired`. With the old
  `string`-typed property this fallback covered plausible typos; with the enum, the only way to
  reach it is an explicit invalid cast (e.g. `(SmtpSecurityMode)99`), so failing loudly is more
  useful than a silent, surprising fallback.
- `Karl.Cli`: `--tls` (`Karl:Smtp:SecurityMode` / `KARL_Karl__Smtp__SecurityMode`) is now validated
  against `SmtpSecurityMode`'s member names up front — an invalid value is now a clear parse error
  listing the valid options, rather than silently sending over `StartTlsRequired`. Accepted string
  values on the command line/config/environment are unchanged (`None`, `ImplicitTls`,
  `StartTlsRequired`, `StartTlsWhenAvailable`).

### Documentation

- `README.md`: updated the SMTP examples and `SecurityMode` description for the new
  `SmtpSecurityMode` enum and its throw-on-invalid-value behavior.

## [1.4.0] - 2026-09-17

### Added

- `Karl.Abstractions`: `EmailMessage.ReplyTo`, a `List<EmailAddress>` alongside `To`/`Cc`/`Bcc`.
- `Karl`: `MailBuilder.ReplyTo(address, name)`, mirroring the existing `To`/`Cc`/`Bcc` methods —
  repeatable for messages that need more than one reply-to address.
- `Karl.Transport.Smtp`: `EmailMessage.ReplyTo` addresses are now mapped onto the outgoing
  `MimeMessage.ReplyTo` list directly. This is now the primary way to set Reply-To; the
  `EmailMessage.Headers["Reply-To"]` escape hatch (1.3.3) still works and populates the same
  underlying MimeKit list, but combining both on one message will produce duplicate/extra
  addresses rather than one overriding the other — use one or the other per message, not both.
- `Karl.Transport.File` / `Karl.Transport.StdOut`: a `Reply-To:` line is added to the rendered
  output when a message has one or more reply-to addresses, following the same pattern as the
  existing `Cc:`/`Bcc:` lines.

### Documentation

- `README.md`: documents `MailBuilder`'s full method set, that `To`/`Cc`/`Bcc`/`ReplyTo` are each
  repeatable for multiple recipients, that `TextBody`/`HtmlBody` are independent (Karl never
  derives one from the other), that `SendAsync` returns a plain `Task` and throws on failure rather
  than returning `bool` or a result record, the exact `SecurityMode` string values and its silent
  fallback to `StartTlsRequired` for an unrecognized value, and that omitting `Username`/`Password`
  sends over an unauthenticated SMTP connection.

## [1.3.3] - 2026-08-28

### Fixed

- `Karl.Transport.Smtp`: `EmailMessage.Headers` (e.g. `Reply-To`) was
  never applied to the outgoing MIME message — `SmtpTransport` only ever
  mapped `From`/`To`/`Cc`/`Bcc`/`Subject`/`Body`/`Attachments`, so any
  custom header a caller set had silently no effect on the actual sent
  email. Each header is now added to the underlying `MimeMessage`, so a
  well-known header like `Reply-To` correctly populates MimeKit's
  corresponding structured property.

## [1.3.2] - 2026-08-10

### Fixed

- `Karl.Cli`: SMTP/File settings from an auto-discovered `karl.json`, an explicit `--json <path>`,
  or `KARL_`-prefixed environment variables were silently discarded — `send`/`file` always
  registered a second, unconditional configuration pass on top of the one bound from the config
  file, so `Karl:Smtp:Port`, `Username`, `Password`, `SecurityMode`, `Karl:File:DirectoryPath`, and
  `FileNamePrefix` could never actually take effect. Each setting now only falls back to its
  built-in CLI default when neither an explicit flag nor a config value was supplied, restoring the
  intended CLI flag > config file > built-in default precedence. See
  `docs/design/03_config_file_parameters_not_used.md`.
- `Karl.Cli`: `--smtp-host` was hard-required on the command line, so `send` could never source the
  SMTP host from a config file or `KARL_Karl__Smtp__Host` alone, unlike every other SMTP setting.
  It's now optional like `--username`/`--password`; `send` fails fast with a clear error if no
  source (flag, config file, or environment variable) supplies a host, rather than silently
  defaulting to `localhost` or failing later with a confusing connection error.

### Documentation

- `src/Karl.Cli/README.md`: new "Configuration Precedence" table documenting how each SMTP/File
  setting resolves across CLI flag, config key, and built-in default; a note on `file`'s default
  `email` file name prefix (no CLI flag exists for it — config-only); updated the `--smtp-host`
  requirement description to match the fix above.

## [1.3.1] - 2026-08-05

### Documentation

- `README.md`: the `Karl.Cli` section now shows install instructions directly
  (`dotnet tool install --global Karl.Cli`, and the local tool-manifest path
  via `dotnet new tool-manifest` + `dotnet tool install Karl.Cli`) instead of
  only linking to `src/Karl.Cli/README.md` — that relative link is dead once
  this README is embedded standalone on a NuGet.org package page. No code
  changes; republished so the fix is visible on the already-published 1.3.0
  package pages.

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
