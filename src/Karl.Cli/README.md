# Karl.Cli

Karl includes a standalone CLI tool for:

* Sending test emails
* Rendering templates
* Local development workflows
* CI/CD pipelines
* Administrative scripts

## Installing Karl.Cli

```bash
dotnet tool install --global Karl.Cli
```

Or locally:

```bash
dotnet new tool-manifest
dotnet tool install Karl.Cli
```

## Basic Usage

### Send an Email

```bash
karl send \
  --from noreply@example.com \
  --smtp-host smtp.example.com \
  --to user@example.com \
  --subject "Hello" \
  --body "# Hello World"
```

### CLI Configuration Sources

`Karl.Cli` reads configuration from:

* `--json <path>` when provided explicitly
* Local config files in current directory (first match wins): `.karl`, `karl`, `karl.json`
* User config directory:
  * Windows: `%USERPROFILE%/.karl`
  * Linux/macOS: `$XDG_CONFIG_HOME/karl` or `~/.config/karl`
* Environment variables prefixed with `KARL_`

Use `__` in environment variable names for nested keys, for example:

```bash
KARL_Karl__Smtp__Host=smtp.example.com
KARL_Karl__Smtp__Port=587
KARL_Karl__Smtp__SecurityMode=StartTlsRequired
```

`send` requires an SMTP host to be specified *somewhere* — via `--smtp-host`, `Karl:Smtp:Host` in a
config file, or `KARL_Karl__Smtp__Host` — and fails fast with a clear error if none of them supply
one, rather than silently attempting to connect to a default host.

## Send Using a Template

```bash
karl send \
  --from noreply@example.com \
  --smtp-host smtp.example.com \
  --to user@example.com \
  --subject "Welcome {{name}}" \
  --markdown ./welcome.md \
  --model ./model.json
```

## SMTP Send Example

```bash
karl send \
  --from noreply@example.com \
  --to user@example.com \
  --subject "SMTP test" \
  --body "Sent via Karl CLI" \
  --smtp-host smtp.example.com \
  --smtp-port 587 \
  --username username \
  --password password \
  --tls StartTlsRequired
```

## Mass Email Using a CSV File

`send`, `file`, and `preview` all accept `--csv` to send one personalized email
per row of a CSV file instead of a single message. Every CSV value is a string
that can be substituted into `--subject`/`--body`/`--markdown` templates — this
is token replacement, not typed data binding.

```bash
karl send \
  --from noreply@example.com \
  --smtp-host smtp.example.com \
  --csv contacts.csv \
  --to-column Email \
  --name-column Name \
  --subject "Welcome {{FirstName}}" \
  --markdown ./welcome.md
```

```csv
Email,FirstName,Name
alice@example.com,Alice,Alice Smith
bob@example.com,Bob,Bob Jones
```

* `--to-column` (required with `--csv`) names the CSV column holding the
  recipient's email address.
* `--name-column` (optional) names the CSV column holding the recipient's
  display name. If omitted, or blank for a row, the email address is used with
  no display name.
* `--to` and `--model` cannot be combined with `--csv` — the recipient and the
  per-row template values both come from the CSV.
* A row with a blank `--to-column` value is skipped, not sent. Failed sends are
  logged and counted, and the batch continues to the next row. A summary line
  reports the outcome: `Sent 48 of 50 emails from contacts.csv (1 skipped, 1 failed).`

**Token names are case-sensitive and must match the CSV header (or `--model`
JSON property) exactly** — a column named `FirstName` only satisfies
`{{FirstName}}` in a template, not `{{firstname}}` or `{{first_name}}`.

## Attachments

`send`, `file`, and `preview` all accept a repeatable `--attach`/`-a` option to attach one or more
local files:

```bash
karl send \
  --from noreply@example.com \
  --smtp-host smtp.example.com \
  --to user@example.com \
  --subject "Your invoice" \
  --markdown ./invoice.md \
  --attach ./invoice.pdf \
  -a ./terms.pdf
```

* The display name and content type are inferred from each path (`invoice.pdf` becomes
  `application/pdf`); unrecognized extensions fall back to `application/octet-stream`.
* A missing `--attach` path fails the whole command before anything is sent or rendered, the
  same as a missing `--markdown`/`--model`/`--csv` path.
* `--attach` composes with `--csv`: the same set of attached files is sent with every row's
  personalized message.
* `file` and `preview` only list attachment names and content types in their output — they don't
  copy the attached bytes anywhere. Use `send` against a test SMTP server to verify the actual
  file contents.

## File Output Mode

```bash
karl file \
  --from noreply@example.com \
  --to user@example.com \
  --subject "File output test" \
  --body "This message is written to disk." \
  --output ./mail-output
```

## StdOut Mode

```bash
karl preview \
  --from noreply@example.com \
  --to user@example.com \
  --subject "Preview test" \
  --body "This message is printed to stdout."
```
