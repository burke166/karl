# Design: CSV-Driven Mass Email for Karl.Cli

Status: Proposal
Owner: Karl.Cli
Related packages: `Karl.Cli`, `Karl.Template.Scriban`, `ComputerCodeBlue.Csv` >= 1.3.0 (external, same author)

> **Revision note:** The original version of this proposal recommended using
> CsvHelper directly in `Karl.Cli` and *not* routing through
> `ComputerCodeBlue.Csv`, because at the time that package only exposed
> `Read<T>`/`Write<T>` — both requiring a fixed record type, which doesn't fit a
> mass-mail CSV's arbitrary columns (§3 explains why). `ComputerCodeBlue.Csv`
> 1.3.0 adds `CsvFile.ReadDynamic`, which returns exactly the loosely-typed
> `Dictionary<string, string>` per row this feature needs. This revision
> replaces the hand-rolled CsvHelper reader with that API. `Karl.Cli` has been
> bumped from `ComputerCodeBlue.Csv` 1.2.1 to 1.3.0
> (`src/Karl.Cli/Karl.Cli.csproj`) to pick it up.

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

`KarlCliCommandFactory.HandleEmailAsync` (`src/Karl.Cli/KarlCliCommandFactory.cs:220-226,245-246`)
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
outcome as the JSON path, no new rendering code needed. This holds regardless of
whether the row is built by hand or comes back from `ComputerCodeBlue.Csv` (§3):
what matters is the *runtime* type of the object handed to `Import`, and
`ReadDynamic`'s rows are concrete `Dictionary<string, string>` instances under an
`IDictionary<string, string>` return type, so the `is IDictionary` check still
passes.

## 3. Practicality of `ComputerCodeBlue.Csv` for this feature

`Karl.Cli.csproj` already references `ComputerCodeBlue.Csv` — now bumped to 1.3.0
as part of this design. Worth being explicit about how the fit changed, since the
first draft of this proposal reached the opposite conclusion.

At 1.2.1, `ComputerCodeBlue.Csv` was a thin, generic wrapper around CsvHelper with
four methods, all requiring a concrete `T`:

```csharp
IEnumerable<T> Read<T>(string filePath, CsvOptions? options = null);
IAsyncEnumerable<T> ReadAsync<T>(string filePath, CsvOptions? options = null, CancellationToken ct = default);
void Write<T>(string filePath, IEnumerable<T> items, CsvOptions? options = null);
Task WriteAsync<T>(string filePath, IEnumerable<T> items, CsvOptions? options = null, CancellationToken ct = default);
```

That's the wrong shape for this feature: a mass-mail CSV has **arbitrary,
user-defined columns** (whatever tokens the campaign needs), so there's no fixed
record type to bind to, and CsvHelper's own workarounds (`Read<Dictionary<...>>`,
`Read<dynamic>`) don't hold up — see the 1.2.1-era analysis this section used to
contain, preserved in git history at `b9d266c` if needed.

**1.3.0 closes that gap directly.** `CsvFile.ReadDynamic` / `ReadDynamicAsync`
(`src/ComputerCodeBlue.Csv/CsvFile.cs`, backed by `CsvDynamicReader.cs`) read a
row per `Dictionary<string, string>`, keyed by the file's actual header names,
with every value as the raw field string — no type inference, no fixed `T`:

```csharp
public static IEnumerable<IDictionary<string, string>> ReadDynamic(string filePath, CsvOptions? options = null);
public static IAsyncEnumerable<IDictionary<string, string>> ReadDynamicAsync(string filePath, CsvOptions? options = null, CancellationToken ct = default);
```

This is exactly the shape §2 needs: a `Dictionary<string, string>` per row (the
concrete type constructed internally by `CsvDynamicReader.ReadRow`), returned
through an `IDictionary<string, string>`-typed enumerable. Two things that made
1.2.1 impractical are gone:

- No fixed `T` — `ReadDynamic` was purpose-built for "arbitrary columns, string
  values," not adapted from a typed-record API.
- `CsvOptionsAdapter` (culture, delimiter detection, trim, missing-field/bad-data
  behavior) is still `internal`, but that no longer matters: `CsvOptions` itself
  is public and `ReadDynamic` accepts it directly, so `Karl.Cli` gets CsvHelper's
  configuration surface without touching `CsvOptionsAdapter` or CsvHelper types
  at all.

**`CsvOptions.Default` already matches what this feature needs**, with no
customization required: `DetectDelimiter = true`, `Trim = Trim`,
`IgnoreBlankLines = true`, `MissingField = Ignore` (don't throw on ragged rows),
`BadData = Ignore` (don't throw on unescaped quotes). Passing `null`/omitting
`options` to `ReadDynamic` is sufficient.

**Recommendation (supersedes the original one): route this feature through
`ComputerCodeBlue.Csv.CsvFile.ReadDynamic`.** No CsvHelper reference is added to
`Karl.Cli` (§6) — CSV parsing edge cases (ragged rows, quoted fields with commas/
newlines, delimiter detection) are the library's concern and are covered by its
own test suite, not re-tested here (§10).

One limitation to know about: `ReadDynamic` returns *data rows only* — there's no
separate "just the headers" API. For a CSV with a header row but zero data rows,
there's nothing to inspect `--to-column` against. See §5's validation rules for
how this design handles that case.

## 4. CSV row reading

No new type is needed in `Karl.Cli` — `CsvFile.ReadDynamic` from
`ComputerCodeBlue.Csv` is called directly where the CSV batch branch needs rows,
inside `KarlCliCommandFactory.HandleEmailAsync`:

```csharp
using ComputerCodeBlue.Csv;

IReadOnlyList<IDictionary<string, string>> rows;
try
{
    rows = CsvFile.ReadDynamic(csvPathValue).ToList();
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    writeLine($"Could not read CSV file '{csvPathValue}': {ex.Message}");
    return 1;
}
```

Notes:

- `.ToList()` materializes the sequence into an indexable, countable
  `IReadOnlyList` for the `[12/50]`-style progress output in §5 — `ReadDynamic`
  already builds its result eagerly internally (`CsvDynamicReader.ReadRecords`
  returns a `List<IDictionary<string, string>>`), so this doesn't add a second
  full read of the file, just a shallow copy of an already-in-memory list.
- No async/streaming for v1, consistent with the original proposal's reasoning:
  mass-mail CSVs for this kind of tool are realistically in the tens-to-low-
  thousands of rows, and reading the whole file into memory up front lets us fail
  fast (§5) before sending anything. `ReadDynamicAsync` exists in the library if
  that changes later.
- Dictionary keys are the header text verbatim (`ReadDynamic`'s row keys come
  straight from `csv.HeaderRecord`), matching the JSON model's case-sensitive-
  exact-match behavior from §2 — so `{{FirstName}}` requires a column literally
  named `FirstName`, same rule users already learned from `--model`.
- Values are never type-inferred (a zip code like `"00501"` stays `"00501"`, not
  `501`) — this is `ReadDynamic`'s documented behavior, not something `Karl.Cli`
  has to arrange.
- `File.OpenRead` inside `CsvFile.ReadDynamic` throws `FileNotFoundException`
  (an `IOException` subtype) for a missing path and `UnauthorizedAccessException`
  for a permissions problem; both are caught above and turned into a clean exit-1
  message instead of a raw stack trace.

## 5. CLI surface changes (`KarlCliCommandFactory.cs`)

New options, added to `AddCommonOptions` (so `send`, `file`, and `preview` all gain
mass-mail support for free — see §7 for why this is a flag on existing commands
rather than a new subcommand):

| Option | Alias | Required | Description |
|---|---|---|---|
| `--csv` | — | no | Path to CSV file. Presence of this flag switches the command into batch mode. |
| `--to-column` | — | required *if* `--csv` given | Name of the CSV column holding the recipient email address. |
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
- `--to-column` value not found in the CSV header row → error, exit 1, before any sends. Checked against `rows[0].Keys` after reading — since `ReadDynamic` returns data rows only (§3), a CSV with a header row but zero data rows has nothing to check the column name against; in that case the batch proceeds with zero rows to send and reports `Sent 0 of 0 emails from contacts.csv`, rather than guessing whether the header would have matched.
- CSV file missing / unreadable → error, exit 1 (unlike `--model`'s current silent-fallback-to-`{}` behavior — this is an explicit improvement, not a regression, since a missing recipient list can't degrade to "send nothing to nobody" quietly).

### Per-row behavior (after validation passes)

- A row whose `--to-column` value is blank/whitespace is **skipped**, a warning is
  written (`writeLine`), and processing continues — one bad row shouldn't sink a
  400-row campaign.
- For each remaining row: the row (`IDictionary<string, string>`, as returned by
  `ReadDynamic`) is passed straight through as the `object? model` argument to
  `ITemplateRenderer.RenderAsync(template, model)` — no renderer changes needed
  (§2/§4) — build an `EmailMessage` (recipient from `--to-column`/`--name-column`,
  everything else same as today), call
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
available in System.CommandLine 2.0.10) and threads that token through the row loop
and into `emailService.SendAsync(message, cancellationToken)` (the interface
already accepts one). This directly serves CLAUDE.md's "use cancellation tokens for
long-running work" — it wasn't worth doing for a single send, it is for a batch.

### Refactor note

`HandleEmailAsync` currently inlines "load model → render subject/body → build
`EmailMessage`" once. To avoid duplicating that between single-send and CSV-batch
modes, this design extracts it into a small local function (e.g.
`RenderMessageAsync(object? templateModel, string toAddress, string? toName)`)
used by both branches. No behavior change to the existing single-send path.

## 6. Package changes

`src/Karl.Cli/Karl.Cli.csproj` already declares:

```xml
<PackageReference Include="ComputerCodeBlue.Csv" Version="1.3.0" />
```

(bumped from 1.2.1 as part of this design; verified against
`https://api.nuget.org/v3-flatcontainer/computercodeblue.csv/index.json`, and
`dotnet restore` succeeds against it.)

No direct `CsvHelper` package reference is added to `Karl.Cli` — unlike the
original version of this proposal, `Karl.Cli` never calls a CsvHelper type
directly; `ComputerCodeBlue.Csv.CsvFile.ReadDynamic` and the public
`ComputerCodeBlue.Csv.CsvOptions` are the only new surface used (§3/§4). CsvHelper
stays an implementation detail one layer down, pulled in transitively same as
today.

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
- **A headers-only / peek API on `ComputerCodeBlue.Csv`.** §5's `--to-column`
  validation gap (can't check a header-only, zero-row CSV) could be closed by such
  an API, but it's a change to a separate repo, not demonstrated as needed yet,
  and shouldn't block this feature — flagged as an open question in §9 instead.

## 9. Open questions for you

1. Should `--to-column` default to `"Email"` for convenience, or stay required with
   no default (current proposal, safer against silently-wrong guesses)? **No. We
   may wish to change this behavior later, but let's wait until there's a 
   demonstrated need.**
2. Is the mutual-exclusivity of `--csv`/`--model` acceptable for v1, or is
   merged shared+per-row tokens (§8) actually needed now rather than later?
   **This is acceptable for v1. We should plan to implement this later.**
3. Is the §5 behavior for a header-only, zero-row CSV (skip the `--to-column`
   check, report `Sent 0 of 0`) acceptable, or would you rather
   `ComputerCodeBlue.Csv` grow a way to read just the header row so this case can
   be validated too (§8)? **We should accept this now and report `Sent 0 of 0`
   and defer an adjustment to the next version of `ComputerCodeBlue.Csv`.
4. Failure policy: continue-and-report (current proposal) vs. stop-on-first-failure
   for `send`? Continue-and-report seems right for a mass campaign, but worth
   confirming since it means a bad SMTP config might burn through several rows
   before you notice. **Continue and report is the desired behavior. The current
   proposal is correct.**

## 10. Testing plan

CSV-parsing edge cases (ragged rows, quoted fields with commas/newlines, blank
lines, delimiter detection, value-stays-a-string behavior) are exercised by
`ComputerCodeBlue.Csv`'s own test suite (`tests/ComputerCodeBlue.Csv.Tests/`)
against `ReadDynamic` and don't need to be re-tested in `Karl.Cli` — this is the
main testing-surface simplification from routing through the library instead of
a hand-rolled reader (§3).

`test/Karl.Test/KarlCliCommandTests.cs` (additions):
- `Preview_Csv_SendsOneEmailPerRow_WithTokensSubstituted` — 2-row CSV, assert two renders happened with distinct substituted values.
- `Preview_Csv_MissingToColumnHeader_ReturnsExitCode1WithClearError`.
- `Preview_Csv_HeaderOnlyZeroRows_ReturnsExitCode0WithZeroSentSummary` — covers the §5/§9 edge case explicitly so its behavior is locked in, not just documented.
- `Preview_Csv_SkipsRowsWithBlankRecipient_AndReportsSkippedCount`.
- `Preview_CsvAndModel_ReturnsExitCode1` / `Preview_CsvAndTo_ReturnsExitCode1` (mutual exclusivity).
- `File_Csv_CreatesOneFileForEachRow`.
- `Send_Csv_ContinuesAfterOneRowFails_AndReturnsExitCode1` (needs the test SMTP fake to throw on a specific recipient — check whether the existing fake in `SmtpTransportTests.cs`/`KarlCliCommandTests.cs` (`CaptureSink`) supports per-call failure injection; if it only records `LastMessage` today, it'll need a small extension to record *all* sent messages, not just the last one, to assert batch counts).
- `Preview_Csv_MissingFile_ReturnsExitCode1WithClearError` — exercises the `IOException`/`UnauthorizedAccessException` catch in §4 rather than letting `CsvFile.ReadDynamic`'s exception surface raw.

## 11. Documentation updates

- `src/Karl.Cli/README.md`: new "Mass Email Using a CSV File" section with the
  example from §1, plus a note on the case-sensitive exact-match token rule
  (shared with `--model`, currently undocumented — worth fixing for both while
  touching this file).

## 12. Incidental issue observed (not part of this feature, flagging only)

While reading `KarlCliCommandFactory.cs`, the `--to` option (`-t`) and `--tls`
option (`-t`) both declared the same short alias
(`src/Karl.Cli/KarlCliCommandFactory.cs:49-53` and `:136-140`). Both were added to
the `send` command via `AddCommonOptions` + the explicit `send.Options.Add(tls)`.

**Fixed before implementing this design's build plan** (not part of the CSV
feature itself): `--to` keeps `-t`; `--tls` now takes `-tls` as its alias instead,
giving `-tls`/`--tls` both as accepted spellings with no collision. One
side-effect worth knowing: System.CommandLine's default help renderer collapses
`-tls`/`--tls` into a single `-tls <tls>` line in `send --help` (it treats a
single-dash and double-dash alias with identical text as redundant — unlike, say,
`-h`/`--host`/`--smtp-host`, whose three forms all have distinct text and all
render). Both forms still parse correctly on the command line; only the
generated help text shows one of them. `CommandHelp_ShowsExpectedOptions` in
`test/Karl.Test/KarlCliCommandTests.cs` was updated to assert `-tls` (what's
actually rendered) instead of `--tls`.
