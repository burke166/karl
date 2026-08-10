# Design: Config File Values Are Silently Overwritten by CLI Defaults

Status: Accepted
Owner: Karl.Cli
Target version: 1.3.2 (patch — bug fix, no new public surface)
Related packages: `Karl.Cli`, `Karl.Extensions.Microsoft`

## 1. Problem statement

`karl` supports supplying `Karl:Smtp`/`Karl:File` settings via a JSON configuration file — either
an explicit `--json <path>`/`-j <path>`, or auto-discovery of `.karl`/`karl`/`karl.json` in the
current directory (`src/Karl.Cli/KarlCliConfiguration.cs`). In practice, none of those values ever
reach the transport that actually sends or writes the email: `send`'s SMTP port, username,
password, and TLS mode, and `file`'s output-file prefix, are always whatever the CLI's built-in
defaults are, regardless of what's in the config file.

Example of the symptom:

```jsonc
// karl.json in the current directory
{
  "Karl": {
    "Smtp": {
      "Host": "smtp.example.com",
      "Port": 2525,
      "Username": "svc-mailer",
      "Password": "s3cret",
      "SecurityMode": "StartTlsOptional"
    }
  }
}
```

```bash
karl send --smtp-host smtp.example.com --from noreply@example.com --to user@example.com \
  --subject "Hi" --body "Hello"
```

Expected: port `2525`, username `svc-mailer`, password from the file, `StartTlsOptional`.
Actual: port `587`, empty username/password, `StartTlsRequired` — the file is read (no error, no
warning) but every value it set for `Karl:Smtp` is discarded before the message is sent.

## 2. Root cause

The config file itself loads correctly — `KarlCliConfiguration.Load`/`ResolveConfigFilePath`
(`src/Karl.Cli/KarlCliConfiguration.cs:8-44`) correctly resolves `--json`, then `.karl`/`karl`/
`karl.json` in `Environment.CurrentDirectory`, then user-profile/XDG locations, then `KARL_`
env vars, using `AddJsonFile(..., optional: true)`. `test/Karl.Test/CliConfigurationLoaderTests.cs`
covers this path and it's correct.

The bug is in how that configuration is wired into the options pattern, in
`HandleEmailAsync` (`src/Karl.Cli/KarlCliCommandFactory.cs:220-226`):

```csharp
var services = new ServiceCollection();
var karlBuilder = services.AddKarl();
var configuration = loadConfiguration(jsonPathValue);
karlBuilder.UseConfiguration(configuration);        // (1) binds Karl:Smtp / Karl:File from JSON
configureKarlTransport(karlBuilder, parseResult);   // (2) always runs after (1)
```

`UseConfiguration` (`src/Karl.Extensions.Microsoft/ServiceCollectionExtensions.cs:66-75`) registers

```csharp
builder.Services.Configure<SmtpTransportOptions>(configuration.GetSection("Karl:Smtp"));
builder.Services.Configure<FileTransportOptions>(configuration.GetSection("Karl:File"));
```

`configureKarlTransport` is the per-command lambda passed into `HandleEmailAsync`. For `send`
(`KarlCliCommandFactory.cs:423-441`):

```csharp
builder.UseSmtp(options =>
{
    options.Host = smtpHostValue ?? "localhost";
    options.Port = smtpPortValue != 0 ? smtpPortValue : 25;
    options.Username = usernameValue ?? string.Empty;
    options.Password = passwordValue ?? string.Empty;
    options.SecurityMode = string.IsNullOrWhiteSpace(tlsValue) ? "StartTlsRequired" : tlsValue;
});
```

and `UseSmtp` (`ServiceCollectionExtensions.cs:30-39`) registers a **second**
`Configure<SmtpTransportOptions>` for the same options type:

```csharp
public static IKarlBuilder UseSmtp(this IKarlBuilder builder, Action<SmtpTransportOptions> configure)
{
    builder.Services.Configure(configure);
    ...
}
```

`Microsoft.Extensions.Options`' `OptionsFactory<TOptions>` applies every registered
`IConfigureOptions<TOptions>` in DI registration order against the same instance. Because (1)
(config-file binding) is registered before (2) (the CLI lambda), (2) always wins for every
property it touches — and it touches all of them, unconditionally, every time:

- `--smtp-port` has `DefaultValueFactory = _ => 587` (`KarlCliCommandFactory.cs:129-134`), so
  `smtpPortValue` is never `0` unless the user explicitly passes `--smtp-port 0`. `Karl:Smtp:Port`
  from the config file is therefore unreachable — it's always replaced by 587 or whatever was
  passed on the CLI. Note this CLI-level default (587) also disagrees with
  `SmtpTransportOptions.Port`'s own class default (`25`, `src/Karl.Transport.Smtp/SmtpTransportOptions.cs:6`)
  — see §4 for why that matters for the fix.
- `Username`/`Password` are reset to `string.Empty` whenever `--username`/`--password` aren't
  passed, erasing `Karl:Smtp:Username`/`Karl:Smtp:Password`. (This one is functionally harmless
  once fixed the straightforward way — `SmtpTransport.cs` treats `null` and `""` identically via
  `string.IsNullOrEmpty(_options.Username)` — so no config-presence check is needed for these two.)
- `SecurityMode` is reset to `"StartTlsRequired"` whenever `--tls` isn't passed, erasing
  `Karl:Smtp:SecurityMode`. The CLI default matches `SmtpTransportOptions.SecurityMode`'s own class
  default, so this one is also harmless to fix the straightforward way.
- `Host` is moot today since `--smtp-host` is `Required = true`, but the same pattern applies —
  there's no way to source it from config either. Its CLI fallback (`"localhost"`) matches the
  class default too.

`file`'s command (`KarlCliCommandFactory.cs:410-421`) has the same shape, plus the same
CLI-default-vs-class-default disagreement as `Port`, on both of its properties:

```csharp
builder.UseFile(options =>
{
    options.DirectoryPath = outputValue;
    options.FileNamePrefix = "email";   // no CLI option exists for this at all
});
```

`--output`'s CLI default is `"emails"` (`KarlCliCommandFactory.cs:148-153`), but
`FileTransportOptions.DirectoryPath`'s own class default is `"outgoing-mail"`
(`src/Karl.Transport.File/FileTransportOptions.cs:5`). `FileNamePrefix` has no CLI option at all —
it's hardcoded to `"email"` here — while `FileTransportOptions.FileNamePrefix`'s own class default
is `"mail"` (`FileTransportOptions.cs:6`). Either way, `Karl:File:DirectoryPath`/
`Karl:File:FileNamePrefix` set in a config file can never take effect for the `file` command —
there's no code path, CLI flag or config, that reaches them other than these two literals.

This affects both `--json <path>` and an auto-discovered `karl.json`/`.karl` equally, since both
go through the same `loadConfiguration` → `UseConfiguration` → `configureKarlTransport` sequence.
It is not a file-discovery or JSON-parsing bug; it's an options-registration-order bug.

## 3. Why existing tests didn't catch it

- `CliConfigurationLoaderTests.cs` only exercises `KarlCliConfiguration` in isolation (file
  discovery, precedence, env vars) — it never goes through `KarlCliCommandFactory`.
- `ServiceCollectionExtensionsTests.UseConfiguration_BindsSmtpOptions` calls
  `UseConfiguration(...).UseSmtp(_ => {})` with an **empty** configure lambda, so it never
  exercises the overwrite.
- `KarlCliCommandTests.Send_DefaultsPort_WhenNotProvided` and `Send_DefaultsTlsMode_WhenNotProvided`
  assert that the CLI's hardcoded defaults (587 / `StartTlsRequired`) win when the corresponding
  flag is omitted — this encodes the buggy behavior as the expected/tested behavior. Neither test
  supplies a `karl.json`/`--json` file, so nothing in the suite verifies a config file's SMTP/File
  values actually survive into the resolved `IOptions<T>` used by a command.

## 4. Proposed fix

### 4.1 Confirmed `System.CommandLine` API (repo pins `2.0.10`)

Verified directly against `System.CommandLine.dll` 2.0.10 (`lib/net8.0`) via reflection — not
assumed from memory:

- `ParseResult.GetResult(Option option)` → `OptionResult?` (nullable — confirmed via
  `NullabilityInfoContext`; returns `null` only for a symbol not part of the parsed command, which
  doesn't apply here since every option checked below is always added to its command).
- `OptionResult.Implicit` → `bool`, a real public property on
  `System.CommandLine.Parsing.OptionResult`. `true` means the value came from the option's
  `DefaultValueFactory` rather than being typed on the command line; `false` means the user
  explicitly supplied it.

So `parseResult.GetResult(option) is { Implicit: false }` is valid, compiles against the pinned
version, and is a safe null-and-explicitness check in one pattern (a `null` result also fails the
pattern, which is the correct "treat as not explicit" behavior).

### 4.2 The remaining wrinkle: CLI-level defaults that disagree with class-level defaults

A plain "only apply the CLI value when `Implicit: false`, otherwise leave the options instance
alone" guard is correct for `Host`, `Username`, `Password`, and `SecurityMode` — their CLI
fallbacks already match (or are behaviorally identical to) the corresponding class defaults on
`SmtpTransportOptions`, so "leave it alone" and "re-apply the CLI fallback" produce the same
result when there's also no config value.

It is **not** correct on its own for `Port`, `DirectoryPath`, or `FileNamePrefix`, because their
CLI-level defaults (`587`, `"emails"`, `"email"`) differ from the underlying options classes' own
defaults (`25`, `"outgoing-mail"`, `"mail"`, §2). A plain `Implicit: false` guard would silently
change today's zero-flag, zero-config behavior (port `587`, directory `"emails"`, prefix
`"email"`) to the library defaults (`25`, `"outgoing-mail"`, `"mail"`) — trading one silent
behavior change for another. Confirmed with you: these three should all keep their current
CLI-level defaults as the fallback of last resort, with config (when present) taking priority over
that fallback, and an explicit CLI flag taking priority over both. That needs a second check —
whether the config file already supplies a value — in addition to the `Implicit` check:

```csharp
if (pr.GetResult(smtpPort) is { Implicit: false })
{
    options.Port = smtpPortValue;                       // 1. explicit CLI flag wins
}
else if (configuration["Karl:Smtp:Port"] is null)
{
    options.Port = 587;                                  // 3. nothing set it — CLI's own default
}
// else: leave the value UseConfiguration already bound   // 2. config value wins over CLI default
```

This requires `configuration` (the `IConfiguration` already built earlier in `HandleEmailAsync`,
`KarlCliCommandFactory.cs:222`) to be visible inside `configureKarlTransport`. Today that delegate
is `Action<IKarlBuilder, ParseResult>`
(`HandleEmailAsync(ParseResult, CancellationToken, Action<IKarlBuilder, ParseResult>)`,
`KarlCliCommandFactory.cs:199`); it needs a third parameter,
`Action<IKarlBuilder, ParseResult, IConfiguration>`, with the call site
(`KarlCliCommandFactory.cs:224`) updated to `configureKarlTransport(karlBuilder, parseResult,
configuration)`, and all three command lambdas (`file`, `send`, `preview`) updated to accept it
(`preview`'s and `send`'s `Host`/`Username`/`Password`/`SecurityMode` don't need it, but the
delegate signature is shared across all three commands).

### 4.3 `send`

```csharp
builder.UseSmtp(options =>
{
    if (pr.GetResult(smtpHost) is { Implicit: false })
    {
        options.Host = smtpHostValue!;
    }

    if (pr.GetResult(smtpPort) is { Implicit: false })
    {
        options.Port = smtpPortValue;
    }
    else if (configuration["Karl:Smtp:Port"] is null)
    {
        options.Port = 587;
    }

    if (pr.GetResult(username) is { Implicit: false })
    {
        options.Username = usernameValue ?? string.Empty;
    }

    if (pr.GetResult(password) is { Implicit: false })
    {
        options.Password = passwordValue ?? string.Empty;
    }

    if (pr.GetResult(tls) is { Implicit: false })
    {
        options.SecurityMode = tlsValue!;
    }
});
```

### 4.4 `file`

```csharp
builder.UseFile(options =>
{
    if (pr.GetResult(output) is { Implicit: false })
    {
        options.DirectoryPath = outputValue!;
    }
    else if (configuration["Karl:File:DirectoryPath"] is null)
    {
        options.DirectoryPath = "emails";
    }

    if (configuration["Karl:File:FileNamePrefix"] is null)
    {
        options.FileNamePrefix = "email";
    }
});
```

`FileNamePrefix` has no CLI option (confirmed with you — none is being added), so there's no
`Implicit` check for it; it's purely "config value if present, else the CLI's existing `"email"`
default" — which is exactly the behavior you asked to keep.

Because `Configure<T>(Action<T>)` delegates run in registration order and mutate the *same*
instance that `Configure<T>(IConfiguration)` already populated, a config-file value now survives
whenever nothing more specific overrides it, and an explicit CLI flag still wins when passed —
precedence is CLI flag > config file > CLI-level default, restored consistently across all five
affected properties.

No changes are needed to `KarlCliConfiguration`, `UseConfiguration`, or the file-discovery logic —
those already work correctly.

## 5. Alternatives considered

- **Swap registration order** (register `UseSmtp`/`UseFile` before `UseConfiguration`). Rejected:
  this would flip the bug rather than fix it — config would then unconditionally win over
  explicit CLI flags, which is the opposite of the precedence documented in
  `src/Karl.Cli/README.md` (`--json`/config as the base, CLI flags as overrides).
- **Post-configure delegate that merges explicitly** (`services.PostConfigure<SmtpTransportOptions>`
  reading raw CLI values and only overwriting non-default fields). Considered, but it duplicates
  the same "was this explicitly passed" problem one layer further away from where the CLI options
  are defined, with no real benefit over guarding at the point the lambda is built. Not pursued.
- **Give `SmtpTransportOptions`/`FileTransportOptions` nullable properties and merge at the
  `SmtpTransport`/`FileEmailTransport` construction site instead of in `Karl.Cli`.** Rejected as
  larger-than-necessary surgery: it would change public option types consumed by non-CLI callers
  (anyone using `Karl.Extensions.Microsoft` directly, e.g. in ASP.NET Core hosting) to work around
  a problem that's entirely local to how `Karl.Cli` composes CLI flags with config. The fix belongs
  in `Karl.Cli`, not in the shared options types.

## 6. Testing plan

New tests in `test/Karl.Test/KarlCliCommandTests.cs`, following the existing pattern of writing a
temp `karl.json` via `TemporaryDirectory` and invoking the command with `--json`:

- `Send_JsonConfigPort_UsedWhenSmtpPortFlagNotProvided` — config file sets `Karl:Smtp:Port`,
  `--smtp-port` omitted, assert the resolved transport used the config value, not `587`.
- `Send_JsonConfigUsernamePassword_UsedWhenFlagsNotProvided`.
- `Send_JsonConfigSecurityMode_UsedWhenTlsFlagNotProvided`.
- `Send_CliSmtpPort_OverridesJsonConfig` — both a config value and `--smtp-port` are supplied;
  assert the CLI value wins (regression guard for precedence, so the fix doesn't overcorrect into
  "config always wins").
- `Send_NoJsonConfigNoFlag_DefaultsPortTo587` — no config file, `--smtp-port` omitted; assert the
  CLI-level default (`587`) still applies, not `SmtpTransportOptions`'s own class default (`25`).
  Regression guard for §4.2 — the fix must not swap one silent default for another.
- `File_JsonConfigFileNamePrefix_UsedWhenSet` — config file sets `Karl:File:FileNamePrefix`, assert
  it reaches `FileTransportOptions` instead of the current hardcoded `"email"`.
- `File_JsonConfigDirectoryPath_UsedWhenOutputFlagNotProvided` — config file sets
  `Karl:File:DirectoryPath`, `--output` omitted, assert the config value is used instead of the
  CLI's `"emails"` default.
- `File_CliOutput_OverridesJsonConfigDirectoryPath` — both a config value and `--output` are
  supplied; assert the CLI value wins.
- `File_NoJsonConfigNoFlag_DefaultsToEmailsDirectoryAndEmailPrefix` — no config file, `--output`
  omitted; assert `DirectoryPath == "emails"` and `FileNamePrefix == "email"` (today's CLI-level
  defaults), not `FileTransportOptions`'s own class defaults (`"outgoing-mail"` / `"mail"`).
  Regression guard mirroring `Send_NoJsonConfigNoFlag_DefaultsPortTo587`.
- Update `Send_DefaultsPort_WhenNotProvided`/`Send_DefaultsTlsMode_WhenNotProvided` if their
  asserted values change shape (they shouldn't — no config file is supplied in those tests, so the
  CLI's own defaults should still apply; they mainly serve as a check that the fix doesn't regress
  the no-config-file case, and now overlap with the two `_NoJsonConfigNoFlag_` tests above).

## 7. Non-goals

- Adding a `--file-name-prefix` CLI flag. Confirmed with you: keep the current hardcoded `"email"`
  CLI-level default (files would otherwise only be distinguishable by timestamp), and let a config
  file override it — no new CLI surface needed for that.
- Changing config file discovery, precedence order, or supported file names/locations — §2
  confirmed that logic is already correct.
- Revisiting `--smtp-host`'s `Required = true`, which currently makes it impossible to source the
  host purely from config either. Same class of problem, but a separate, larger discussion (would
  mean validating "host must come from somewhere, CLI or config" post-parse instead of via
  `System.CommandLine`'s built-in required-option check) — flagged here for awareness, not
  addressed by this fix.

## 8. Decisions

Resolved during review, recorded here for traceability:

1. **`System.CommandLine` API** — confirmed by reflecting directly on the pinned `2.0.10` assembly
   (§4.1): `ParseResult.GetResult(Option)` → `OptionResult?`, `OptionResult.Implicit: bool`. No
   further confirmation needed before implementation.
2. **Version** — ships as `1.3.2` (patch).
3. **`FileNamePrefix` default** — keep `"email"` as the CLI-level fallback (no `--file-name-prefix`
   flag); a config file may override it (§4.4). The same review surfaced that `Port` and
   `DirectoryPath` have the identical CLI-default-vs-class-default conflict (§4.2) and should be
   treated the same way for consistency — config overrides the CLI default, and the CLI default
   itself is preserved rather than silently falling back to the underlying options classes'
   defaults (`25` / `"outgoing-mail"`).
