# CLAUDE.md

## Karl

Karl is a modular, extensible email composition and delivery framework for
modern .NET applications.

Its primary responsibilities are:

- Composing email messages
- Rendering templates
- Delivering messages through interchangeable transports
- Supporting dependency injection in ASP.NET Core
- Providing a command-line interface for email automation

Karl is designed for developers who want:

- Strong separation of concerns
- Swappable transports and template engines
- Clean DI integration
- A lightweight alternative to large monolithic mail systems
- A reusable framework suitable for APIs, workers, console apps, and SaaS
  platforms

---

## Karl Principles

Karl composes and delivers email.

Karl does not own:

- User management
- Scheduling
- Background job orchestration
- Business workflows
- Persistence

Those concerns belong to consuming applications.

Karl should expose reusable abstractions rather than application-specific
behavior.

---

## Design Philosophy

When making changes, follow these priorities in order:

1. Simplicity
2. Correctness
3. Maintainability
4. Performance

Do not introduce complexity unless there is measurable benefit.

Avoid premature optimization.

Favor explicit code over magic.

---

## Architecture

Karl is composed of independent packages.

Core packages should have no dependency on transports,
template engines, or hosting frameworks.

Example dependency direction:

Karl.Core
↑
Karl.Templates
↑
Karl.Transports
↑
Karl.Hosting
↑
Karl.Cli

Infrastructure must never contain business rules.

---

## Development Philosophy

Assume this repository will exist for ten years.

Write code that another engineer can understand in five minutes.

Avoid creating abstractions for future requirements.

Only build what is currently needed.

---

## AI Expectations

Before making changes:

- Understand the surrounding code.
- Identify existing patterns.
- Extend existing patterns before creating new ones.

If a proposed design differs from existing conventions, explain why.

Never rewrite large portions of the project without justification.

Prefer incremental improvements.

---

## Coding Style

Prefer small classes.

Prefer small functions.

Functions should usually fit on one screen.

Avoid boolean flag parameters.

Avoid deep inheritance.

Prefer composition.

Use dependency injection where appropriate.

Avoid static mutable state.

---

## C#

Target the latest supported .NET LTS unless otherwise specified.

Enable nullable reference types.

Treat warnings as errors whenever practical.

Prefer async APIs.

Use cancellation tokens for long-running work.

Avoid synchronous blocking of async code.

Prefer records for immutable models.

---

## Testing

Every bug fix should include a regression test when practical.

Test observable behavior.

Do not test implementation details.

Favor fake transports and in-memory implementations
for unit tests.

Network transports should be covered by integration tests.

Avoid tests that depend on external SMTP servers or
third-party email providers unless explicitly marked as
integration tests.

---

## Logging

Logging exists to diagnose production issues.

Log:

- important state transitions
- external API failures
- retries
- startup
- shutdown

Never log secrets.

Never log authentication tokens.

---

## Configuration

Configuration belongs outside code.

Use strongly typed options.

Validate configuration at startup.

Fail fast on invalid configuration.

---

## Security

Treat all external input as untrusted.

Validate before processing.

Escape before rendering.

Use least privilege.

Never hardcode:

- passwords
- API keys
- client secrets
- tokens

---

## Dependencies

Minimize third-party dependencies.

Prefer Microsoft libraries when sufficient.

Before adding a package:

1. Is it necessary?
2. Is it maintained?
3. Can we reasonably implement the functionality ourselves?

---

## Documentation

Public APIs should be documented.

Complex algorithms deserve comments.

Simple code should not.

If the architecture changes, update documentation.

---

## Git

Keep commits focused.

Do not mix formatting with functional changes.

Avoid mass file rewrites.

Preserve file history whenever possible.

---

## Refactoring

Improve code when touching it.

Avoid drive-by refactors.

Large refactors should be proposed before implementation.

---

## Performance

Measure first.

Optimize second.

Document significant optimizations.

Readable code is preferred over micro-optimizations.

---

## Error Handling

Prefer explicit failures.

Do not silently swallow exceptions.

Return meaningful error information.

Retry only transient failures.

---

## Automation

Karl interacts with external systems.

Assume:

- APIs fail.
- Networks disconnect.
- Authentication expires.
- Rate limits exist.

Code should degrade gracefully.

---

## Agent Guidance

When uncertain:

- Ask questions.
- Do not invent APIs.
- Do not invent configuration.
- Do not assume database schemas.
- Search the repository first.

---

## Project Priorities

The project values:

- Reliability over features.
- Correctness over speed.
- Maintainability over cleverness.
- Consistency over novelty.
- Small improvements over rewrites.

Every contribution should leave the project slightly better than it was found.

---

## Repository Preferences

Preferred technologies

- C#
- .NET
- Microsoft.Extensions.*
- Dependency Injection
- Options Pattern
- Logging Abstractions

Avoid introducing

- ASP.NET dependencies into Core
- ORM dependencies
- Database requirements
- UI frameworks
- Node.js build tooling

Karl should remain operable from the command line whenever practical.