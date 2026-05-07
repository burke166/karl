# Karl the Mailman

Karl is a modular, extensible email composition and delivery framework for modern .NET applications. It provides a clean abstraction for:

* Building emails
* Rendering templates
* Delivering messages through interchangeable transports
* Integrating with ASP.NET Core dependency injection
* Running email jobs from the command line

Karl is designed for developers who want:

* Strong separation of concerns
* Swappable transports and template engines
* Clean DI integration
* A lightweight alternative to large monolithic mail systems
* A reusable framework suitable for APIs, workers, console apps, and SaaS platforms

---

# Why Karl?

Excellent libraries like MailKit and Scriban already exist.

Karl does not attempt to replace them.

Instead, Karl provides a unified, modular framework that composes proven email-related libraries into a clean, developer-friendly architecture for modern .NET applications.

Karl focuses on:
- Dependency Injection integration
- Transport abstraction
- Template rendering pipelines
- Environment-specific delivery
- Consistent developer ergonomics
- Reusable email infrastructure
- Testing and development workflows

Instead of building email infrastructure from scratch in every project, developers can use Karl to quickly assemble production-ready email workflows using proven underlying components.

---

# No, Why the Name 'Karl?'

Because of NBA great Karl Malone, nicknamed "the Mailman" as a college player because he delivers.

---

# Features

## Core Features

* Strongly-typed email message builder
* Transport abstraction layer
* Template rendering abstraction
* Async-first API design
* Dependency Injection support
* Multi-project modular architecture
* CLI tool support
* File output transport for testing/debugging
* Console/stdout transport for development
* SMTP delivery via Karl.Transport.Smtp (MailKit-based)
* Scriban template support
* Optional CSS inlining via PreMailer.Net

---

# Solution Structure

| Project                     | Purpose                                                           |
| --------------------------- | ----------------------------------------------------------------- |
| `Karl`                      | Convenience wrapper package containing the most common components |
| `Karl.Abstractions`         | Core interfaces and contracts                                     |
| `Karl.Extensions.Microsoft` | ASP.NET Core / DI registration extensions                         |
| `Karl.Template.Scriban`     | Scriban template rendering engine                                 |
| `Karl.Transport.Smtp`       | SMTP transport using MailKit                                      |
| `Karl.Transport.File`       | Writes emails to files                                            |
| `Karl.Transport.StdOut`     | Writes emails to stdout/console                                   |
| `Karl.Cli`                  | Command-line email sending utility                                |

---

# Installation

## Option 1 — Recommended Wrapper Package

For most applications, install the main Karl package.

```bash
dotnet add package Karl
```

This package is intended to provide a convenient “batteries included” experience.

Typical included functionality:

* Core abstractions
* Microsoft DI integration
* Common transports
* Scriban template support

---

# Quick Start

## ASP.NET Core Example

```csharp
using Karl;
using Karl.Extensions.Microsoft;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddKarl()
    .UseSmtp(options =>
{
    options.Host = "smtp.example.com";
    options.Port = 587;
    options.Username = "smtp-user";
    options.Password = "smtp-password";
    options.SecurityMode = "StartTlsRequired";
});

var app = builder.Build();
```

## ASP.NET Core With IConfiguration

Karl supports binding transport options from `IConfiguration` via `UseConfiguration(...)`.

```csharp
using Karl;
using Karl.Extensions.Microsoft;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddKarl()
    .UseConfiguration(builder.Configuration)
    .UseSmtp(_ => { }) // still choose a transport
    .UseScribanTemplates();
```

`UseConfiguration` binds these sections:

* `Karl:Smtp` -> `SmtpTransportOptions`
* `Karl:File` -> `FileTransportOptions`

---

## appsettings.json Example

```json
{
  "Karl": {
    "Smtp": {
      "Host": "smtp.example.com",
      "Port": 587,
      "SecurityMode": "StartTlsRequired",
      "Username": "smtp-user",
      "Password": "smtp-password"
    },
    "File": {
      "DirectoryPath": "./mail-output",
      "FileNamePrefix": "mail"
    }
  }
}
```

---

## Sending an Email

```csharp
using Karl;

public class WelcomeService
{
    private readonly IEmailService _emailService;

    public WelcomeService(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task SendWelcomeAsync()
    {
        await _emailService.SendAsync(
            new MailBuilder()
                .To("user@example.com")
                .Subject("Welcome")
                .HtmlBody("<h1>Hello!</h1>")
                .Build());
    }
}
```

---

# Using Individual Components

Karl is intentionally modular.

You can install only the components you need.

---

# Minimal Core Installation

If you want complete control over dependencies:

```bash
dotnet add package Karl.Abstractions
```

This package contains:

* Interfaces
* Contracts
* Shared models
* Base abstractions

Useful for:

* Custom transports
* Plugin systems
* Shared libraries
* Framework integrations

---

# Dependency Injection Support

```bash
dotnet add package Karl.Extensions.Microsoft
```

Provides:

* IServiceCollection extensions
* Standardized registration helpers
* ASP.NET Core integration

---

# Scriban Template Support

```bash
dotnet add package Karl.Template.Scriban
```

Example:

```csharp
var template = """
<h1>Hello {{ name }}</h1>
""";

var model = new
{
    name = "Christopher"
};
```

---

# SMTP Transport

```bash
dotnet add package Karl.Transport.Smtp
```

Example:

```csharp
using Karl.Extensions.Microsoft;

builder.Services.AddKarl().UseSmtp(options =>
{
    options.Host = "smtp.example.com";
    options.Port = 587;
    options.Username = "username";
    options.Password = "password";
    options.SecurityMode = "StartTlsRequired";
});
```

---

# File Transport

Useful for:

* Local development
* Testing
* Email previews
* CI pipelines

```bash
dotnet add package Karl.Transport.File
```

Example:

```csharp
using Karl.Extensions.Microsoft;

builder.Services.AddKarl().UseFile(options =>
{
    options.DirectoryPath = "./mail-output";
    options.FileNamePrefix = "mail";
});
```

Generated messages are written to disk instead of sent.

---

# StdOut Transport

Useful for:

* Debugging
* Containers
* Local testing
* CI logs

```bash
dotnet add package Karl.Transport.StdOut
```

Example:

```csharp
using Karl.Extensions.Microsoft;

builder.Services.AddKarl().UseStdOut();
```

---

# Why Use Individual Packages?

You may prefer individual packages if:

* You want minimal dependencies
* You are building a reusable framework
* You are creating custom transports
* You want strict control over transitive packages
* You do not need SMTP or templating support
* You are optimizing container image size

---

# Architecture Overview

Karl separates responsibilities into distinct layers:

| Layer           | Responsibility          |
| --------------- | ----------------------- |
| Builder         | Constructs messages     |
| Template Engine | Renders templates       |
| Transport       | Delivers messages       |
| DI Integration  | Wires services together |

This allows developers to:

* Swap transports without changing business logic
* Change template engines later
* Add custom delivery mechanisms
* Test email generation independently

---

# Creating Custom Transports

Implement the transport interface:

```csharp
using Karl;
using Karl.Models;

public class MyTransport : IEmailTransport
{
    public Task SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        // Custom delivery logic
        return Task.CompletedTask;
    }
}
```

Register it:

```csharp
services.AddSingleton<IEmailTransport, MyTransport>();
```

---

# CSS Inlining

Karl.Template.Scriban can optionally use PreMailer.Net to inline CSS styles for improved email client compatibility.

This is especially useful for:

* Outlook compatibility
* HTML email rendering
* Responsive email templates

---

# Karl.Cli

Karl includes a standalone CLI tool for:

* Sending test emails
* Rendering templates
* Local development workflows
* CI/CD pipelines
* Administrative scripts

---

# Installing Karl.Cli

```bash
dotnet tool install --global Karl.Cli
```

Or locally:

```bash
dotnet new tool-manifest
dotnet tool install Karl.Cli
```

---

# Basic Usage

## Send an Email

```bash
karl send \
  --from noreply@example.com \
  --smtp-host smtp.example.com \
  --to user@example.com \
  --subject "Hello" \
  --body "# Hello World"
```

## CLI Configuration Sources

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

---

# Send Using a Template

```bash
karl send \
  --from noreply@example.com \
  --smtp-host smtp.example.com \
  --to user@example.com \
  --subject "Welcome {{name}}" \
  --markdown ./welcome.md \
  --model ./model.json
```

---

# SMTP Send Example

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

---

# File Output Mode

```bash
karl file \
  --from noreply@example.com \
  --to user@example.com \
  --subject "File output test" \
  --body "This message is written to disk." \
  --output ./mail-output
```

---

# StdOut Mode

```bash
karl preview \
  --from noreply@example.com \
  --to user@example.com \
  --subject "Preview test" \
  --body "This message is printed to stdout."
```

---

# Example Development Workflow

## Local Development

Use:

* `Karl.Transport.StdOut`
* `Karl.Transport.File`

to avoid accidentally sending real emails.

---

## Production

Use:

* `Karl.Transport.Smtp`

with:

* SMTP credentials
* STARTTLS or SSL/TLS
* Secret management
* Production template rendering

---

# Recommended Package Combinations

## Typical Web Application

```text
Karl
```

---

## Minimal API + SMTP

```text
Karl.Abstractions
Karl.Extensions.Microsoft
Karl.Transport.Smtp
```

---

## Template Rendering Service

```text
Karl.Abstractions
Karl.Template.Scriban
```

---

## Testing / Preview Environment

```text
Karl.Transport.File
Karl.Transport.StdOut
```

---

# Target Frameworks

Karl supports:

* .NET 8.0
* .NET 10.0

---

# Future Roadmap

Potential future features include:

* Razor template engine support
* MJML integration
* Queue integrations
* Background job helpers
* Webhook transports
* API transports (SendGrid, SES, Mailgun, Postmark)
* OpenTelemetry instrumentation
* Retry policies

---

# Example Solution Layout

```text
src/
 ├── Karl/
 ├── Karl.Abstractions/
 ├── Karl.Extensions.Microsoft/
 ├── Karl.Template.Scriban/
 ├── Karl.Transport.Smtp/
 ├── Karl.Transport.File/
 ├── Karl.Transport.StdOut/
 └── Karl.Cli/
```

---

# Acknowledgements

Karl is built on top of several excellent open-source projects:

- MailKit — SMTP and mail transport functionality
- MimeKit — MIME message generation
- Scriban — High-performance template rendering
- PreMailer.Net — CSS inlining for HTML email compatibility
- Microsoft.Extensions.* — Dependency Injection and configuration infrastructure

These projects provide the foundation that allows Karl to focus on composition, architecture, and developer experience.

---

# License

MIT License

---

# Contributing

Contributions, issues, and feature requests are welcome.
