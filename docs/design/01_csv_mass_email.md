# Design: CSV-Driven Mass Email for Karl.Cli

Status: Proposal
Owner: Karl.Cli
Related packages: `Karl.Cli`, `Karl.Template.Scriban`, `ComputerCodeBlue.Csv` (external, same author)

## 1. Goal

Let `karl send` / `karl file` / `karl preview` take a CSV file where each row is one
recipient, and send one personalized email per row using the existing Scriban
subject/body templates. Every CSV value is a string — this is token replacement,
not typed data binding.

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

## 2. Existing JSON model feature: how it actually works, and its practicality

Before designing the CSV path it's worth understanding *why* `--model` works today,
because the CSV design deliberately rides the same mechanism rather than inventing
a new one.

`KarlCliCommandFactory.HandleEmailAsync` (`src/Karl.Cli/KarlCliCommandFactory.cs:219-225`)
does:

```csharp
var model = JsonSerializer.Deserialize<object>(modelJson);
...
var renderedSubject = await renderer.RenderAsync(subjectValue ?? string.Empty, model);
```

`JsonSerializer.Deserialize<object>(...)` doesn't produce a POCO — with `object` as
the target type, System.Text.Json always returns a boxed `System.Text.Json.JsonElement`.
That gets passed straight into `ScribanTemplateRenderer.RenderAsync`, which calls
Scriban's `Template.RenderAsync(model, member => member.Name)`.

That overload does `scriptObject.Import(model, ...)`
(`Scriban.Runtime.ScriptObjectExtensions.Import`), and — this is the load-bearing
detail — Scriban has a built-in, public special case for exactly this type:

```csharp
// Scriban.Runtime.ScriptObjectExtensions.Import(object, ...)
if (obj is IScriptObject scriptObj) { ... }
if (obj is IDictionary dictionary) { script.ImportDictionary(dictionary); return; }
#if !SCRIBAN_NO_SYSTEM_TEXT_JSON
if (obj is JsonElement json) { script.Import(json); return; }
#endif
script.Import(obj, ScriptMemberImportFlags.All, filter, renamer); // reflection fallback
```

`Import(JsonElement)` recursively walks the element and copies it into a
`ScriptObject`/`ScriptArray` tree (`JsonElementExtensions.ToScriban`), preserving
JSON property names exactly as written (no casing changes, no renamer applied).

**Verdict: the JSON model feature is legitimate, not a hack** — it uses a
documented, public Scriban capability, not an implementation accident. But it has
real, currently-unhandled edges worth knowing before extending the same code path:

- **Root must be a JSON object.** If `model.json` is a top-level array or scalar,
  `Import(JsonElement)` throws `ArgumentOutOfRangeException` (it only special-cases
  `JsonValueKind.Object` when the target script container is a `ScriptObject`,
  which it always is here). There's no CLI-level validation or friendly message —
  the raw exception will surface.
- **Malformed JSON crashes the process.** `JsonSerializer.Deserialize` throws
  `JsonException` with no try/catch around it in `HandleEmailAsync`. The user gets
  a stack trace, not "your model.json is invalid at line 4."
- **A missing `--model` path fails silently.** `fileExists(modelPathValue)` gates
  the read; if false, `modelJson` quietly stays `"{}"`. A typo'd path produces an
  email with unresolved `{{tokens}}` and no warning.
- **Property names are case-sensitive and must match the template exactly** —
  `{"name":"Chris"}` only satisfies `{{name}}`, not `{{Name}}`. This is by design
  (`CopyToScriptObject` uses `property.Name` verbatim) and is fine, but it's
  undocumented in `README.md`.
- It is single-recipient by construction: one `--to`, one render, one send. There
  is no batching, which is exactly the gap this design fills.

The practical takeaway for this design: **the same `object? model` parameter on
`ITemplateRenderer.RenderAsync` can carry a per-row CSV dictionary with zero
interface changes**, because Scriban's `Import` already special-cases
`IDictionary` (checked *before* the `JsonElement` case, at the top of the same
method). A `Dictionary<string, string>` implements the non-generic
`System.Collections.IDictionary`, so it hits `ImportDictionary`, which copies
`entry.Key.ToString() -> entry.Value` into the `ScriptObject` — same shape of
outcome as the JSON path, no new rendering code needed.

## 3. Practicality of `ComputerCodeBlue.Csv` for this feature

`Karl.Cli.csproj` already references `ComputerCodeBlue.Csv` (has since the initial
commit), but nothing in the codebase uses it yet — it appears to have been added in
anticipation of this exact feature. Worth being explicit about why it doesn't
directly fit, so the choice below isn't a surprise:

`ComputerCodeBlue.Csv` (also authored by you) is a thin, generic wrapper around
CsvHelper with exactly four methods:

```csharp
IEnumerable<T> Read<T>(string filePath, CsvOptions? options = null);
IAsyncEnumerable<T> ReadAsync<T>(string filePath, CsvOptions? options = null, CancellationToken ct = default);
void Write<T>(string filePath, IEnumerable<T> items, CsvOptions? options = null);
Task WriteAsync<T>(string filePath, IEnumerable<T> items, CsvOptions? options = null, CancellationToken ct = default);
```

All four require a concrete `T` — CsvHelper binds columns to `T`'s public
properties via reflection. That's the wrong shape for this feature: a mass-mail
CSV has **arbitrary, user-defined columns** (whatever tokens the campaign needs),
so there's no fixed record type to bind to. The two ways around that don't hold up:

- `CsvFile.Read<Dictionary<string,string>>(...)` — CsvHelper's default class-map
  auto-mapping does not bind to `Dictionary<TKey,TValue>`; it expects settable
  properties, not an indexer. This throws at runtime.
- `CsvFile.Read<dynamic>(...)` — `dynamic` erases to `object` at the call site, and
  CsvHelper's dynamic-record support requires it to construct an `ExpandoObject`.
  Even if that worked through the wrapper, `ExpandoObject` implements
  `IDictionary<string, object>` but **not** the non-generic `System.Collections.IDictionary`
  that Scriban's `Import` checks for — so it would silently fall through to
  Scriban's reflection-based import path, find no real CLR properties on the
  `ExpandoObject`, and import nothing. Verified against Scriban 7.1.0's
  `ScriptObjectExtensions.Import` source. This would look like it works (no
  exception) but every token would render empty.

There's also a mechanical blocker: the wrapper's `CsvOptionsAdapter` (culture,
delimiter detection, trim, missing-field/bad-data behavior →
`CsvHelper.Configuration.CsvConfiguration`) is `internal`, so even the "just use
`CsvOptions` for its sane defaults" path isn't available outside that assembly.

**Recommendation:** don't route this feature through `ComputerCodeBlue.Csv` as it
stands. Use CsvHelper directly in `Karl.Cli` (see §4) — it's a handful of lines and
matches your instruction to use CsvHelper for this. Separately, if you want
`ComputerCodeBlue.Csv` to eventually support "arbitrary rows as string maps," that's
a generically useful addition to *that* package (e.g. a
`ReadDictionaries(string path, CsvOptions?)` returning
`IEnumerable<IReadOnlyDictionary<string,string>>`), but it's a change to a separate
repo and shouldn't block or be bundled into this feature. Flagged as an open
question in §9, not decided here.

## 4. CSV row reading

New internal type in `Karl.Cli`, e.g. `src/Karl.Cli/CsvModelSource.cs`:

```csharp
internal static class CsvModelSource
{
    internal static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadRows(string filePath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectDelimiter = true,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
            MissingFieldFound = null,   // don't throw on ragged rows
            BadDataFound = null,        // don't throw on unescaped quotes etc.
        };

        using var streamReader = new StreamReader(filePath);
        using var csv = new CsvReader(streamReader, config);

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        var rows = new List<IReadOnlyDictionary<string, string>>();
        while (csv.Read())
        {
            var row = new Dictionary<string, string>(headers.Length, StringComparer.Ordinal);
            foreach (var header in headers)
            {
                row[header] = csv.GetField(header) ?? string.Empty;
            }
            rows.Add(row);
        }

        return rows;
    }
}
```

Notes:

- Every field is read with `GetField(string)` (string overload) — CsvHelper never
  attempts numeric/date type inference this way, which matches "assume everything
  is a string" (e.g. a zip code column like `"00501"` stays `"00501"`, not `501`).
  This is the reason to avoid `GetRecords<dynamic>()`: besides the `IDictionary`
  problem in §3, CsvHelper's dynamic path does its own type inference per field,
  which is exactly what we don't want.
- `Dictionary<string, string>` keys are the header text verbatim (`StringComparer.Ordinal`),
  matching the JSON model's case-sensitive-exact-match behavior from §2 — so
  `{{FirstName}}` requires a column literally named `FirstName`, same rule users
  already learned from `--model`.
- No async/streaming for v1 — mass-mail CSVs for this kind of tool are realistically
  in the tens-to-low-thousands of rows; reading the whole file into memory up front
  also lets us fail fast (see §6) before sending anything.

## 5. CLI surface changes (`KarlCliCommandFactory.cs`)

New options, added to `AddCommonOptions` (so `send`, `file`, and `preview` all gain
mass-mail support for free — see §7 for why this is a flag on existing commands
rather than a new subcommand):

| Option | Alias | Required | Description |
|---|---|---|---|
| `--csv` | — | no | Path to CSV file. Presence of this flag switches the command into batch mode. |
| `--to-column` | — | required *iff* `--csv` given | Name of the CSV column holding the recipient email address. |
| `--name-column` | — | no | Name of the CSV column holding the recipient's display name. If omitted or the column is blank for a row, the email address is used with no display name. |

`--to-column` has **no default**. A mass-mail CSV's email column could be named
`Email`, `Email Address`, `Contact`, etc. — guessing a default (e.g. `"Email"`)
would silently do the wrong thing on a CSV that doesn't happen to use that name.
Failing fast with "`--to-column` is required when `--csv` is provided" is more in
line with "explicit over magic" than a convenience default that's wrong half the
time. (Flagged in §9 in case you'd rather default it.)

### Validation rules (fail fast, before sending anything)

- `--csv` + `--to` together → error, exit 1: *"--to is not used in CSV batch mode; the recipient comes from --to-column in each row."*
- `--csv` + `--model` together → error, exit 1: *"--csv and --model cannot be combined yet; see docs for planned per-row + shared token merging."* (v1 keeps these mutually exclusive — see §8 for why merging is deferred.)
- `--csv` without `--to-column` → error, exit 1.
- `--to-column` value not found in the CSV header row → error, exit 1, before any sends.
- CSV file missing / unreadable → error, exit 1 (unlike `--model`'s current silent-fallback-to-`{}` behavior — this is an explicit improvement, not a regression, since a missing recipient list can't degrade to "send nothing to nobody" quietly).

### Per-row behavior (after validation passes)

- A row whose `--to-column` value is blank/whitespace is **skipped**, a warning is
  written (`writeLine`), and processing continues — one bad row shouldn't sink a
  400-row campaign.
- For each remaining row: build `Dictionary<string,string> model` from the row,
  render subject + body via the existing `ITemplateRenderer.RenderAsync(template, model)` —
  no renderer changes needed (§2/§4) — build an `EmailMessage` (recipient from
  `--to-column`/`--name-column`, everything else same as today), call
  `emailService.SendAsync(message, cancellationToken)`.
- If a send throws (SMTP failure, etc.), catch it, log it (`writeLine`), count it
  as a failure, and **keep going** to the next row rather than aborting the whole
  batch — consistent with CLAUDE.md's "APIs fail... code should degrade
  gracefully." Exit code is `1` if any row failed, `0` if all attempted sends
  succeeded, `1` if any rows were skipped for missing recipients — surfaced via a
  summary line either way:
  `Sent 48 of 50 emails from contacts.csv (1 skipped, 1 failed).`
- `--verbose` additionally prints one line per row: `[12/50] Sending to alice@example.com...`.

### Cancellation

`HandleEmailAsync` currently uses `Command.SetAction(Func<ParseResult, Task<int>>)`,
which gives System.CommandLine no cancellation token to wire up to Ctrl+C. A
single-send command finishes fast enough that this doesn't matter today, but a
300-row SMTP batch might run for minutes. This design switches to the
`SetAction(Func<ParseResult, CancellationToken, Task<int>>)` overload (already
available in System.CommandLine 2.0.7) and threads that token through the row loop
and into `emailService.SendAsync(message, cancellationToken)` (the interface
already accepts one). This directly serves CLAUDE.md's "use cancellation tokens for
long-running work" — it wasn't worth doing for a single send, it is for a batch.

### Refactor note

`HandleEmailAsync` currently inlines "load model → render subject/body → build
`EmailMessage`" once. To avoid duplicating that between single-send and CSV-batch
modes, this design extracts it into a small local function (e.g.
`RenderMessageAsync(IReadOnlyDictionary<string,string>? templateModel, string toAddress, string? toName)`)
used by both branches. No behavior change to the existing single-send path.

## 6. Package changes

Add an explicit reference in `src/Karl.Cli/Karl.Cli.csproj`:

```xml
<PackageReference Include="CsvHelper" Version="33.1.0" />
```

`CsvHelper` is already pulled in transitively today via `ComputerCodeBlue.Csv`
(which depends on `CsvHelper 33.1.0`), so this adds no new dependency to the
resolved graph — it just makes the dependency explicit instead of relying on a
transitive reference from an otherwise-unused package. If `ComputerCodeBlue.Csv`
is ever dropped from `Karl.Cli`, the CSV feature shouldn't silently stop compiling.

## 7. Why a flag on existing commands, not a new subcommand

Considered `karl mass-send --csv ...` as a separate command. Rejected: `send`,
`file`, and `preview` already share one rendering/sending pipeline
(`HandleEmailAsync`) differing only in transport. `--csv` composes with all three
for free:

- `karl preview --csv contacts.csv ...` → dry-run every row's rendered output to stdout.
- `karl file --csv contacts.csv ...` → write one `.txt` per row to disk for review.
- `karl send --csv contacts.csv ...` → actually send.

A separate subcommand would have to re-implement (or import) all three transport
wire-ups to get the same dry-run capability, which is exactly the kind of
duplication CLAUDE.md's "extend existing patterns before creating new ones" warns
against.

## 8. Non-goals for v1 (explicitly deferred)

- **Merging `--csv` with `--model`** (shared/global tokens across all rows, CSV
  overriding per-row). Mechanically possible — import the JSON model into the
  `ScriptObject` first, then `Import` the row dictionary over it — but it adds a
  second configuration surface before there's a demonstrated need. Revisit if a
  real campaign needs "same footer link for everyone + per-row name."
- **Connection reuse for SMTP.** `SmtpTransport.SendAsync` (`src/Karl.Transport.Smtp/SmtpTransport.cs`)
  connects, authenticates, sends, and disconnects **on every call**. A 500-row CSV
  through `karl send` means 500 full SMTP handshakes, which is slow and more likely
  to trip a provider's rate limiting than a single persistent connection would be.
  Fixing this properly means a batch-aware transport API
  (e.g. `IEmailTransport.SendManyAsync` or a connection-scoped sender), which is a
  cross-cutting change to `Karl.Abstractions`/`Karl.Transport.Smtp`/`Karl.Core` —
  exactly the kind of change CLAUDE.md says to propose separately rather than fold
  into this feature. v1 accepts the per-row reconnect cost; it's correct, just not
  fast. Worth measuring against a real provider before optimizing (CLAUDE.md:
  "Measure first. Optimize second.").
- **CC/BCC/attachments from CSV columns.** Not requested; would extend the row
  schema (`CcColumn`, `BccColumn`, etc.) with no current use case.
- **Extending `ComputerCodeBlue.Csv`** with a dictionary/dynamic-row API — belongs
  in that package's own repo if wanted (§3), not bundled here.

## 9. Open questions for you

1. Should `--to-column` default to `"Email"` for convenience, or stay required with
   no default (current proposal, safer against silently-wrong guesses)?
2. Is the mutual-exclusivity of `--csv`/`--model` acceptable for v1, or is
   merged shared+per-row tokens (§8) actually needed now rather than later?
3. Do you want `ComputerCodeBlue.Csv` to grow a dictionary-row API so this logic
   can eventually move there instead of living in `Karl.Cli` — or is CsvHelper
   direct in `Karl.Cli` fine long-term?
4. Failure policy: continue-and-report (current proposal) vs. stop-on-first-failure
   for `send`? Continue-and-report seems right for a mass campaign, but worth
   confirming since it means a bad SMTP config might burn through several rows
   before you notice.

## 10. Testing plan

`test/Karl.Test/CsvModelSourceTests.cs` (new):
- Parses headers and rows into `Dictionary<string,string>` correctly.
- Values that look numeric/zero-padded stay strings verbatim (e.g. `"00501"`).
- Ragged rows (missing trailing column) don't throw, given `MissingFieldFound = null`.
- Blank lines are ignored.
- Quoted fields containing commas/newlines parse correctly (standard CsvHelper behavior — a smoke test, not re-testing CsvHelper itself).

`test/Karl.Test/KarlCliCommandTests.cs` (additions):
- `Preview_Csv_SendsOneEmailPerRow_WithTokensSubstituted` — 2-row CSV, assert two renders happened with distinct substituted values.
- `Preview_Csv_MissingToColumnHeader_ReturnsExitCode1WithClearError`.
- `Preview_Csv_SkipsRowsWithBlankRecipient_AndReportsSkippedCount`.
- `Preview_CsvAndModel_ReturnsExitCode1` / `Preview_CsvAndTo_ReturnsExitCode1` (mutual exclusivity).
- `File_Csv_CreatesOneFileForEachRow`.
- `Send_Csv_ContinuesAfterOneRowFails_AndReturnsExitCode1` (needs the test SMTP fake to throw on a specific recipient — check whether the existing fake in `SmtpTransportTests.cs`/`KarlCliCommandTests.cs` (`CaptureSink`) supports per-call failure injection; if it only records `LastMessage` today, it'll need a small extension to record *all* sent messages, not just the last one, to assert batch counts).

## 11. Documentation updates

- `src/Karl.Cli/README.md`: new "Mass Email Using a CSV File" section with the
  example from §1, plus a note on the case-sensitive exact-match token rule
  (shared with `--model`, currently undocumented — worth fixing for both while
  touching this file).

## 12. Incidental issue observed (not part of this feature, flagging only)

While reading `KarlCliCommandFactory.cs`, the `--to` option (`-t`) and `--tls`
option (`-t`) both declare the same short alias (`src/Karl.Cli/KarlCliCommandFactory.cs:48-52`
and `:135-139`). Both are added to the `send` command via `AddCommonOptions` +
the explicit `send.Options.Add(tls)`. Not touched by this design (out of scope),
but worth a separate small fix since it could throw or silently misbehave in
`send`'s option parsing.
