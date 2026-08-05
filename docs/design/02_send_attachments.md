# Design: Sending Attachments via Karl and Karl.Cli

Status: Accepted
Owner: Karl / Karl.Cli
Target version: 1.3.0 (all packages, bumped together)
Related packages: `Karl.Abstractions`, `Karl`, `Karl.Transport.Smtp`, `Karl.Transport.File`,
`Karl.Transport.StdOut`, `Karl.Cli`

## 1. Goal

Let a caller attach one or more files to an `EmailMessage`, and let `Karl.Cli` do the same from
the command line via a repeatable `--attach`/`-a` option:

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

Programmatically, via `MailBuilder`:

```csharp
using Karl;

await emailService.SendAsync(
    new MailBuilder()
        .From("noreply@example.com")
        .To("user@example.com")
        .Subject("Your invoice")
        .HtmlBody("<p>Attached.</p>")
        .AttachFile("./invoice.pdf")
        .Build());
```

Or, when the caller's attachment doesn't live on the local file system (e.g. pulled from S3,
Azure Blob Storage, or a database `BLOB` column by application code Karl has no knowledge of):

```csharp
await using var contract = await storageClient.OpenReadAsync("contracts/2024-annual.pdf");

await emailService.SendAsync(
    new MailBuilder()
        .From("noreply@example.com")
        .To("user@example.com")
        .Subject("Signed contract")
        .HtmlBody("<p>Attached.</p>")
        .AttachStream(contract, "2024-annual.pdf", "application/pdf")
        .Build());
```

Per the requirements this design is scoped against: **only the local file system and
caller-supplied streams are in scope.** Fetching attachments from remote storage is explicitly
not something Karl (the assembly) does — that's the responsibility of "another service" the
consuming application already has, which then hands Karl an open `Stream`. `Karl.Cli` only ever
uses the local-file-system path, since it has no concept of a pluggable storage service.

## 2. Why this mirrors `ComputerCodeBlue.Csv`'s `CsvFile` / `CsvStream` split

The request calls out `ComputerCodeBlue.Csv`
(`~/Nextcloud/Projects/ComputerCodeBlue.Csv/src/ComputerCodeBlue.Csv/`) as the pattern to follow
for "files available as streams or filenames." That library doesn't have one API that tries to
accept "a file or a stream" — it has **two parallel static classes** with identical method
shapes:

- `CsvFile` (`CsvFile.cs:16`) — takes a `string filePath`. Opens its own `FileStream`, reads it,
  and closes it before returning. Callers never see or manage a stream.
- `CsvStream` (`CsvStream.cs:17`) — takes a `Stream` the caller already has. Never closes it
  (`leaveOpen: true` throughout); the doc comment on the class is explicit: *"the caller creates
  the stream, so the caller owns its lifetime."*

The two are not interchangeable in a way that matters here: `CsvFile` can be called repeatedly
against the same path (it reopens fresh every time), while `CsvStream` reads whatever position
the caller's stream happens to be at, once, and leaves it however the read left it.

This design uses the same split for attachments, for the same reason: the two content sources
have genuinely different ownership and reusability semantics, and collapsing them into one API
would either force Karl to close streams it doesn't own, or force every caller to route local
files through a `FileStream` they now have to manage by hand.

## 3. Model changes (`Karl.Abstractions`)

All new types below live in `Karl.Abstractions`, namespace `Karl.Models`, alongside
`EmailMessage`, `EmailAddress`, and `EmailBody` (`src/Karl.Abstractions/Models/`). No new package
reference is needed — everything used is `System.IO`/`System.Threading`, already available to
every target framework Karl supports (net8.0/net10.0). This keeps attachments out of
`Karl.Transport.*` and `Karl.Cli`, consistent with CLAUDE.md's dependency direction (Core has no
dependency on transports or hosting) — a transport shouldn't have to know how an attachment's
bytes were sourced, only that it can ask for a `Stream`.

### 3.1 `IAttachmentSource`

```csharp
namespace Karl.Models;

public interface IAttachmentSource
{
    Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
}
```

One method, deliberately. A transport that wants to send an attachment calls
`OpenReadAsync` once and reads the returned `Stream`; it does not know or care whether that
`Stream` came from a local file or was handed in by the caller.

### 3.2 `EmailAttachment`

```csharp
namespace Karl.Models;

public sealed class EmailAttachment
{
    public string FileName { get; }
    public string ContentType { get; }
    public IAttachmentSource Source { get; }

    private EmailAttachment(string fileName, string contentType, IAttachmentSource source)
    {
        FileName = fileName;
        ContentType = contentType;
        Source = source;
    }

    public static EmailAttachment FromFile(string filePath, string? fileName = null, string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var resolvedFileName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(filePath) : fileName;
        var resolvedContentType = contentType ?? AttachmentContentTypes.Resolve(resolvedFileName);

        return new EmailAttachment(resolvedFileName, resolvedContentType, new FileAttachmentSource(filePath));
    }

    public static EmailAttachment FromStream(Stream stream, string fileName, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var resolvedContentType = contentType ?? AttachmentContentTypes.Resolve(fileName);

        return new EmailAttachment(fileName, resolvedContentType, new StreamAttachmentSource(stream));
    }
}
```

`FromFile` requires only a path — `fileName` defaults to `Path.GetFileName(filePath)`, matching
what a recipient's mail client should reasonably display. `FromStream` requires an explicit
`fileName` because a `Stream` has no name to infer one from.

### 3.3 `FileAttachmentSource` (internal) — mirrors `CsvFile`

```csharp
namespace Karl.Models;

internal sealed class FileAttachmentSource : IAttachmentSource
{
    private readonly string _filePath;

    public FileAttachmentSource(string filePath) => _filePath = filePath;

    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(File.OpenRead(_filePath));
    }
}
```

`File.OpenRead` is synchronous — wrapped in `Task.FromResult` rather than reached for
`File.OpenReadAsync` machinery that doesn't materially exist for local file opens (opening a file
handle is fast; only reading its contents benefits from being async, and MimeKit/the transport
does that part). `ComputerCodeBlue.Csv.CsvFile.ReadDynamicAsync` does exactly the same thing —
synchronous `File.OpenRead` inside an `async` method — so there's direct precedent for not
over-engineering this.

Each call to `OpenReadAsync` opens a **new** `FileStream`. That's the property that makes
`FromFile`-sourced attachments safe to reuse across multiple sends — see §6 for why that matters
for `--csv` batch mode.

### 3.4 `StreamAttachmentSource` (internal) — mirrors `CsvStream`

```csharp
namespace Karl.Models;

internal sealed class StreamAttachmentSource : IAttachmentSource
{
    private readonly Stream _stream;
    private int _consumed;

    public StreamAttachmentSource(Stream stream) => _stream = stream;

    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _consumed, 1) != 0)
        {
            throw new InvalidOperationException(
                "This attachment's stream has already been read once and cannot be read again " +
                "(for example, by sending the same EmailMessage to more than one recipient, or " +
                "reusing an EmailAttachment across CSV batch rows). Use EmailAttachment.FromFile " +
                "for an attachment that needs to be sent more than once.");
        }

        return Task.FromResult(_stream);
    }
}
```

Two rules carried over directly from `CsvStream`'s documented contract, restated for attachments:

- **The stream is never closed by Karl.** `StreamAttachmentSource` never calls `Dispose`/
  `DisposeAsync` on `_stream`, and neither does any transport that reads from it (§5.1) — the
  caller created it, the caller owns its lifetime, exactly as `CsvStream`'s class-level doc
  comment states for CSV streams.
- **It's read-once.** `CsvStream.Read<T>` reads from the stream's current position once; there's
  no supported way to "re-read" a `CsvStream` call because streams aren't guaranteed seekable.
  Attachments have the same problem, but a silent second-read would be worse here: instead of
  raising the CSV equivalent (an empty or garbled record), a second recipient would silently get
  a zero-byte or truncated attachment. `StreamAttachmentSource` fails loudly instead
  (`InvalidOperationException`) the moment it's asked to open a second time, consistent with
  CLAUDE.md's "prefer explicit failures... do not silently swallow exceptions." `FromFile` is the
  documented escape hatch when the same content needs to go out more than once.

### 3.5 Content-type inference (`AttachmentContentTypes`, internal)

No MIME-type package is added — CLAUDE.md's dependency checklist ("is it necessary? can we
reasonably implement the functionality ourselves?") points at a small built-in table rather than
a package. The obvious built-in candidate,
`Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider`, is disqualified outright:
CLAUDE.md explicitly says "Avoid introducing ASP.NET dependencies into Core."

The following table is a brief example. In the built version, it should also include support for the following
extensions: `pptx`, `odt`, `ods`, `odp`, `doc`, `ppt`, and `xls`.

```csharp
namespace Karl.Models;

internal static class AttachmentContentTypes
{
    public const string Default = "application/octet-stream";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".zip"] = "application/zip",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    public static string Resolve(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && ByExtension.TryGetValue(extension, out var contentType)
            ? contentType
            : Default;
    }
}
```

Unknown/missing extensions fall back to `application/octet-stream` rather than throwing — an
attachment with an unrecognized extension is still a legitimate thing to send. Both
`EmailAttachment.FromFile`/`FromStream` accept an explicit `contentType` override for anything
this table gets wrong or doesn't cover.

### 3.6 `EmailMessage.Attachments`

```csharp
// src/Karl.Abstractions/Models/EmailMessage.cs
public sealed class EmailMessage
{
    public EmailAddress From { get; set; } = default!;
    public List<EmailAddress> To { get; } = new();
    public List<EmailAddress> Cc { get; } = new();
    public List<EmailAddress> Bcc { get; } = new();

    public string Subject { get; set; } = string.Empty;

    public EmailBody Body { get; set; } = new();

    public List<EmailAttachment> Attachments { get; } = new();   // new

    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>();
}
```

Same shape as `To`/`Cc`/`Bcc` — a mutable, get-only collection initialized empty, so existing
code that constructs `EmailMessage` (object initializers, `MailBuilder`) is unaffected. Nothing
about `IEmailService`/`EmailService` (`src/Karl.Core/EmailService.cs`) needs to change —
attachments ride along inside `EmailMessage` exactly the way `Cc`/`Bcc` already do.

## 4. `MailBuilder` changes (`Karl`)

```csharp
// src/Karl/MailBuilder.cs
public MailBuilder AttachFile(string filePath, string? fileName = null, string? contentType = null)
{
    _message.Attachments.Add(EmailAttachment.FromFile(filePath, fileName, contentType));
    return this;
}

public MailBuilder AttachStream(Stream stream, string fileName, string? contentType = null)
{
    _message.Attachments.Add(EmailAttachment.FromStream(stream, fileName, contentType));
    return this;
}
```

Same shape as the existing `To`/`Cc`/`Bcc`/`TextBody`/`HtmlBody` methods: thin, fluent, delegates
straight to the model. No new state in `MailBuilder` itself.

## 5. Transport changes

Attachments are optional per-message (an empty `Attachments` list is the common case and changes
nothing). Each transport decides what "sending an attachment" means for its own medium — no new
shared interface (e.g. an `IAttachmentCapableTransport` marker) is introduced, since all three
existing transports can support attachments directly and CLAUDE.md says not to add abstractions
without a demonstrated need.

### 5.1 `Karl.Transport.Smtp.SmtpTransport`

MimeKit's `BodyBuilder.Attachments` (already available — `Karl.Transport.Smtp.csproj` already
references `MailKit`, which brings in `MimeKit` transitively, and `SmtpTransport.cs` already uses
`MimeKit` types directly) does the actual MIME assembly:
`bodyBuilder.ToMessageBody()` (`SmtpTransport.cs:54`) already produces a
`multipart/alternative` body when both `TextBody`/`HtmlBody` are set; once `Attachments` is
non-empty it automatically wraps that in `multipart/mixed` instead. No manual multipart
construction is needed — populate `bodyBuilder.Attachments` before the existing
`bodyBuilder.ToMessageBody()` call.

**The load-bearing detail:** `MimePart`'s `Content` wraps the `Stream` you pass to
`Attachments.AddAsync` by reference — it does not eagerly copy the bytes. MimeKit only actually
reads from that stream when the message is serialized, which for `SmtpClient.SendAsync` happens
during the send call itself, not before it. That means every attachment's stream must stay open
and un-disposed until after `client.SendAsync(...)` (`SmtpTransport.cs:78`) returns — not just
until `Attachments.AddAsync` returns. Concretely:

```csharp
var openedSources = new List<Stream>();

try
{
    foreach (var attachment in message.Attachments)
    {
        var stream = await attachment.Source.OpenReadAsync(cancellationToken);
        openedSources.Add(stream);
        await bodyBuilder.Attachments.AddAsync(
            attachment.FileName,
            stream,
            MimeKit.ContentType.Parse(attachment.ContentType),
            cancellationToken);
    }

    mimeMessage.Body = bodyBuilder.ToMessageBody();

    // existing connect/authenticate/send/disconnect block (SmtpTransport.cs:65-86)
}
finally
{
    foreach (var stream in openedSources)
    {
        await stream.DisposeAsync();
    }
}
```

`FileAttachmentSource`-backed streams are `FileStream`s Karl itself opened via `OpenReadAsync`
(§3.3) — disposing them here is Karl's responsibility, same as `CsvFile`'s `using var stream =
File.OpenRead(...)` disposes its own stream once done. `StreamAttachmentSource`-backed streams are
caller-owned (§3.4) — `stream.DisposeAsync()` here would violate that contract, so **the finally
block above must only dispose streams it opened itself, not ones that came from
`StreamAttachmentSource`.** The cleanest way to express that without `IAttachmentSource` leaking
an "am I file-backed" flag is for `SmtpTransport` to not dispose anything it received via
`OpenReadAsync` at all, and instead have `FileAttachmentSource.OpenReadAsync` return a stream
that closes itself — but a `FileStream` doesn't do that on its own. `IAttachmentSource` should 
use an explicit lifetime hint (e.g. `OwnsStream` bool). This is close to the "avoid boolean flag
parameters" guidance this repo otherwise follows, however it is necessary to dispoose of streams
responsibly rather than have them left open unnecessarily.

### 5.2 `Karl.Transport.File.FileEmailTransport` / `Karl.Transport.StdOut.StdOutEmailTransport`

Both already build a header block before the body (`FileEmailTransport.cs:26-33`,
`StdOutEmailTransport.cs:17-23`, near-identical in each file). Add one more line, after the
existing `Subject:` line, only when attachments are present:

```csharp
if (message.Attachments.Count > 0)
{
    sb.AppendLine($"Attachments: {string.Join(", ", message.Attachments.Select(a => $"{a.FileName} ({a.ContentType})"))}");
}
```

Neither transport calls `attachment.Source.OpenReadAsync` — they list metadata only, they don't
open or copy the attachment's actual bytes. This is deliberately the minimal option and is sufficent
for v1. Users that need to test the actual bytes can always use an SMTP test to themselves. It is
difficult to imagine a consumer wanting to display the bytes of an attachment to stdout.

## 6. `Karl.Cli` changes

### 6.1 New option

| Option | Alias | Required | Description |
|---|---|---|---|
| `--attach` | `-a` | no | Path to a local file to attach. Repeatable to attach multiple files. |

```csharp
var attach = new Option<string[]>("--attach", ["-a"])
{
    Description = "Path to a local file to attach. Repeatable to attach multiple files.",
    Required = false,
    DefaultValueFactory = _ => Array.Empty<string>()
};
```

Added to `AddCommonOptions` (`KarlCliCommandFactory.cs:161-177`), so `send`, `file`, and `preview`
all gain attachment support for free, the same reasoning as `--csv`'s placement
(doc 01, §7): all three commands already share `HandleEmailAsync`, differing only in transport.
No new alias collisions — `-a` isn't used by any existing option
(`v`, `f`, `t`, `j`, `s`, `b`, `md`, `m`, `l`, `ld`, `c`, `h`/`host`, `P`/`port`, `u`, `p`, `o`,
`tls`).

### 6.2 Validation (fail fast, before rendering or sending anything)

Every `--attach` path is checked with the same `fileExists` abstraction already used for
`--markdown`/`--model` (`KarlCliCommandFactoryOptions.FileExists`,
`KarlCliCommandFactory.cs:19,30`) — this keeps the check unit-testable the same way
`Preview_Csv_MissingFile_ReturnsExitCode1WithClearError` (doc 01, §10) tests CSV path failures:

```csharp
foreach (var path in attachValues)
{
    if (!fileExists(path))
    {
        errors.AppendLine($"Attachment file not found: '{path}'.");
    }
}
```

Added into the existing `errors` `StringBuilder` block (`KarlCliCommandFactory.cs:219-257`) so it
composes with every other validation error already collected there, and reported together in one
pass rather than one error at a time. A missing attachment should fail before sending any messages
backed by a `csv` list. Karl should not continue to send messages with missing attachments. This 
is the desired behavior because attachments apply to the entire csv list, rather than a single 
errored row in a csv file where the remaining rows can be sent without an error.

### 6.3 Wiring into sent messages

`EmailAttachment.FromFile(path)` instances are built **once**, outside the per-row loop:

```csharp
var attachments = attachValues.Select(path => EmailAttachment.FromFile(path)).ToList();
```

and then added into every message `RenderMessageAsync` builds
(`KarlCliCommandFactory.cs:280-299`):

```csharp
async Task<EmailMessage> RenderMessageAsync(object? templateModel, string toAddress, string? toName)
{
    ...
    var message = new EmailMessage { /* ... existing fields ... */ };
    message.Attachments.AddRange(attachments);
    return message;
}
```

This is safe to do **once** and reuse across every row of a `--csv` batch (§6.4) — the whole
point of `FileAttachmentSource` reopening a fresh `FileStream` per `OpenReadAsync` call (§3.3) is
that the same `EmailAttachment` instance can be attached to as many different `EmailMessage`s as
needed without re-validating the path or re-reading the file up front.

### 6.4 Composes with `--csv` batch mode (doc 01)

`--attach` files apply to **every row** of a `--csv` batch — there's no per-row attachment
selection in this design (see §7, and doc 01 §8, which already named "attachments from CSV
columns" as an explicitly deferred non-goal when the CSV feature was built). A campaign that
needs the same PDF sent to 500 recipients:

```bash
karl send \
  --from noreply@example.com \
  --smtp-host smtp.example.com \
  --csv contacts.csv \
  --to-column Email \
  --subject "Welcome {{FirstName}}" \
  --markdown ./welcome.md \
  --attach ./welcome-packet.pdf
```

works correctly under this design specifically because `--attach` is always file-backed in
`Karl.Cli` (§6.3), never stream-backed — the read-once restriction in §3.4 never applies here.

## 7. Non-goals for v1 (explicitly deferred)

- **Remote/cloud attachment sources in `Karl.Cli`** (S3, Azure Blob, a URL, etc.). Per the
  requirements this design is scoped against, that's out of scope for the CLI entirely;
  `EmailAttachment.FromStream` (§3.2) exists so a consuming *application* can supply that content
  itself — Karl never fetches anything remote on its own.
- **Per-row attachments from `--csv` columns** (e.g. `--attach-column`). Doc 01 §8 already flagged
  this as deferred when the CSV feature shipped; nothing here changes that. `--attach` in this
  design is a fixed, shared set of files for the whole batch (§6.4).
- **Inline/embedded images** (`Content-ID`/`cid:` references for `<img>` in HTML bodies, via
  MimeKit's `BodyBuilder.LinkedResources`). Different feature from a regular attachment — worth
  its own design if/when there's a real need.
- **Explicit per-attachment CLI overrides** (custom display name or content-type via something
  like `--attach path:name:type`). v1 infers both from the path (§3.2, §3.5); the programmatic
  `EmailAttachment.FromFile`/`FromStream` overloads already accept overrides for callers who need
  them.
- **Attachment/message size limits enforced by Karl.** No proactive validation against SMTP
  server limits — a too-large message fails at the transport the same way any other SMTP error
  does today (CLAUDE.md: "assume APIs fail... code should degrade gracefully," not "prevent every
  possible failure").
- **Virus/malware scanning of attachment content.** Out of scope — the trust boundary for
  attachment content sits with whatever supplied the path or stream, not with Karl.

## 8. Open questions for you

1. §5.1 flags a real gap: should `IAttachmentSource` carry an explicit ownership signal so
   `SmtpTransport` (and any future attachment-aware transport) knows which streams it opened
   itself and must dispose (file-backed) versus which are caller-owned and must not be touched
   (stream-backed)? Or is leaving file handles to finalization acceptable for v1, with a
   follow-up if it proves to be a real problem in practice (e.g. a large `--csv` + `--attach`
   batch running low on file handles)? _`IAttachmentSource` should cary an ownership signal
   so streams that Karl should close are closed responsibly._ 

2. §5.2: is metadata-only listing (`Attachments: invoice.pdf (application/pdf)`) enough for the
   `file`/`preview` transports, or do you want `file` to also write the actual attachment bytes
   to disk next to the generated `.txt`, so a dev workflow can inspect the real content before
   wiring up SMTP? _This deliberately minimal option is sufficent for v1. Users that need to test
   the actual bytes can always use an SMTP test to themselves. It is difficult to imagine a 
   consumer wanting to display the bytes of an attachment to stdout._

3. Is the `AttachmentContentTypes` table in §3.5 worth seeding with more extensions up front, or
   is starting small and extending on demand (current proposal) fine, given it's an internal
   implementation detail that's always overridable via the explicit `contentType` parameter?
   _The materialized version should also include support for the following extensions: `pptx`,
   `odt`, `ods`, `odp`, `doc`, `ppt`, and `xls`._

4. Should a missing `--attach` file behave like a missing `--csv` file (fail the whole command,
   current proposal, §6.2) or, in `--csv` batch mode specifically, be treated as a softer warning
   since it affects every row uniformly rather than one row's data? Failing fast seems more
   consistent with "attachments are file-not-found errors, not data problems," but worth
   confirming since it's a different failure mode than the CSV per-row skip behavior in doc 01.
   _A missing attachment should fail before sending any messages backed by a `csv` list. Karl 
   should not continue to send messages with missing attachments. This is the desired behavior
   because attachments apply to the entire csv list, rather than a single errored row in a csv
   file._

## 9. Versioning

All packages currently sit at `1.2.0` in lockstep (bumped together in the `1.2.0` release,
`e5d91b7`, even packages with no functional change that release, e.g. `Karl.Extensions.Microsoft`).
This design should ship as **`1.3.0` across all packages**, not `1.2.1`:

- Every change here is additive: new `EmailAttachment`/`IAttachmentSource` types, a new
  get-only `EmailMessage.Attachments` property (empty by default, so existing object initializers
  and `MailBuilder` usage are unaffected), new `MailBuilder.AttachFile`/`AttachStream` methods, and
  a new `--attach`/`-a` CLI option. No existing public signature or behavior changes when
  attachments aren't used — that's a semver MINOR bump, not PATCH (new functionality, not just a
  fix) and not MAJOR (nothing breaks).
- Following the same repo-wide lockstep bump as `1.2.0` keeps one version number, one CHANGELOG
  entry, and one tag, rather than reintroducing the per-package independent versioning the
  CHANGELOG header describes but hasn't actually been practiced.
- Unlike `1.2.0` (where `Karl.Extensions.Microsoft` rode along with no functional change),
  `Karl.Transport.Smtp`, `Karl.Transport.File`, and `Karl.Transport.StdOut` all gain real new
  behavior here (MIME attachment assembly / metadata lines) and are genuinely part of the `1.3.0`
  feature set.
- If §8 question 1 is resolved by adding a member to `IAttachmentSource` (an ownership signal),
  that's still additive from a consumer's perspective — no external implementers of that interface
  exist yet — so it doesn't push this toward a MAJOR bump.

## 10. Testing plan

Following the existing fake-transport/in-memory conventions (CLAUDE.md: "favor fake transports
and in-memory implementations for unit tests"; `test/Karl.Test/TemporaryDirectory.cs` for
filesystem-backed cases):

`test/Karl.Test/` (new, e.g. `EmailAttachmentTests.cs`):
- `FromFile_DefaultsFileNameFromPath`.
- `FromFile_InfersContentTypeFromExtension` (theory over the `AttachmentContentTypes` table).
- `FromFile_UnknownExtension_FallsBackToOctetStream`.
- `FromFile_OpenReadAsync_CanBeCalledMultipleTimes_ReturnsFreshStreamEachTime` (using
  `TemporaryDirectory`).
- `FromStream_OpenReadAsync_SecondCall_ThrowsInvalidOperationException`.
- `FromStream_RequiresExplicitFileName` (no path to infer one from).

`test/Karl.Test/SmtpTransportTests.cs` (additions, following the existing
`FakeSmtpClientAdapter`/`CreateMessage` pattern):
- `SendAsync_WithAttachments_AddsThemToTheMimeMessage` — assert
  `fakeClient.SentMessage` contains a `MimePart` per attachment with the expected file name and
  content type.
- `SendAsync_WithFileAttachment_DisposesStreamAfterSend` (or the inverse, depending on how
  question 1 in §8 is resolved).
- `SendAsync_WithStreamAttachment_DoesNotDisposeCallerStream`.

`test/Karl.Test/FileEmailTransportTests.cs` / `StdOutEmailTransportTests.cs` (additions):
- `SendAsync_WithAttachments_ListsFileNameAndContentTypeInOutput`.
- `SendAsync_WithNoAttachments_OmitsAttachmentsLine` (regression guard so the common no-attachment
  case doesn't grow a stray `Attachments:` line).

`test/Karl.Test/MailBuilderTests.cs` (additions):
- `AttachFile_AddsAttachmentToMessage`.
- `AttachStream_AddsAttachmentToMessage`.

`test/Karl.Test/KarlCliCommandTests.cs` (additions):
- `Send_WithAttach_IncludesAttachmentInSentMessage`.
- `Send_WithMultipleAttach_IncludesAllAttachments` (repeated `-a`/`--attach`).
- `Send_WithMissingAttachFile_ReturnsExitCode1WithClearError`.
- `Send_CsvWithAttach_AppliesSameAttachmentsToEveryRow` — exercises §6.4 directly.

## 11. Documentation updates

- `src/Karl.Cli/README.md`: new "Attachments" section using the §1 example, noting `--attach` is
  repeatable and composes with `--csv` (§6.4).
- `README.md`: brief mention of `EmailAttachment`/`MailBuilder.AttachFile`/`AttachStream` under
  "Sending an Email," plus a short note under "Creating Custom Transports" that a transport
  wanting to support attachments reads `message.Attachments` and calls `Source.OpenReadAsync`
  (§5.1's ownership caveat belongs here once §8 question 1 is resolved).
