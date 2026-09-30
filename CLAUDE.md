# ModsDude conventions

These rules override default behaviour. Each has a one-line reason.

## Project stage

- ModsDude is in alpha: one developer, no users. Existing data is test data.
- Never build backfills, data migrations for old rows, re-publish logic or backwards-compatibility paths unless asked. Just change behaviour going forward.
- Treat everything in `docs/` as out of date. Don't rely on it and don't update it; it will be removed or overhauled.

## Priorities

- Correctness and stability > readability > performance. Correctness and stability carry by far the most weight.
- Optimise only when a real cost is shown.

## Workflow

- Plan every non-trivial task before writing code, and wait for a go-ahead. This applies even when the request reads like a spec or starts with "Plan:".
  - Why: design choices made without discussion have been wrong and had to be redone.
  - Trivial, unambiguous edits can just be done.
- Write plans as terse bullets, one fact each, grouped under short phase headings. No paragraphs. Open questions go only in a short list at the end.
- Include cleanup of the surrounding area in the plan (see "When touching an area" below), even if it grows the change.
- Never add a NuGet package without asking first. Propose it in the plan.

## Definition of done

A change is done only when all of these hold:

- The build passes with zero warnings. Fix old warnings too, not just new ones.
- Relevant tests pass.
- UI changes have been seen running. Ask first, then launch the real client; start the API if needed (`dotnet run --project ModsDude.Server/ModsDude.Server.Api`). The client logs in automatically.
- The full diff has been re-read against this file: dead code, comments, simplification, patterns.

## Git

- Commit on the current branch, `main` included. Never create a branch unless asked, or unless a PR is requested.
  - Why: solo project; an unrequested branch turns one commit into a commit, a merge and a branch deletion.
- Commit after each task once it meets the definition of done.
- Commit messages: imperative subject line, then a few terse bullets. No prose paragraphs.

## Generated API client

- Never hand-edit `ModsDude.Client/ModsDude.Client.Core/ModsDudeServer/Generated.cs`, even when regeneration reorders unrelated operations.
- After any server API change:
  1. `pwsh scripts/openapi.ps1 -Update` regenerates `openapi/v1.json` at build time. The dev server does not need to be running or stopped.
  2. `nswag run nswag-config.nswag` in `ModsDude.Client/ModsDude.Client.Core` regenerates `Generated.cs` from the checked-in `v1.json`.

## Architecture

### Server

- Endpoints orchestrate: one class per endpoint, using `ApplicationDbContext` directly, checking authorization and calling domain methods. No handler, mediator or repository layers.
- Invariants live in rich domain entities, not in endpoints.
- Broken invariants throw (`DomainValidationException`). Expected outcomes such as not found or forbidden are typed `Results` returned by the endpoint.
- EF Core: always add a new migration. Never edit or squash existing ones.

### Client

- Use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`) wherever it fits, which is most places.
- Avoid logic in code-behind (`.xaml.cs`) when a binding, behavior or view model can reasonably do it.

### General

- Every DI-registered service gets an interface.
- Trust types and nullable annotations internally. Validate only at boundaries: API input, files, game data, network. Fail loudly; never swallow errors.
- Pass `CancellationToken` through every async call chain.
- No fire-and-forget: every started task is awaited, or explicitly owned with its errors observed.
- No `async void` and no `.Result` / `.Wait()` / `GetAwaiter().GetResult()`, except where WPF requires it (such as event handlers).

## Tests

- Every new or changed piece of domain or core logic ships with tests in the same change.

## Code style

- Keep a generally high standard in all new code that you write or old code that you touch.
  - If the existing code has bad patterns, change that when touching it, instead of copying the bad pattern into new code.
  - Do not assume that existing code is good.
- Get behaviour from structure, not logic: prefer derived state, types, data binding and composition over imperative code that keeps things in sync.
  - Why: flags, manual refreshes and re-entrancy guards cause "doesn't update until I touch something else" bugs.
  - Look for a single source of truth that everything else derives from, instead of adding another refresh call or guard flag.
- Write readable and maintainable code, not clever code. Avoid cleverness that makes the code harder to read or maintain.
- Keep comments to a minimum. Use descriptive names instead of doc-comment essays on every member.
- A comment only describes what the code does now, never what it used to do or how it got here.
- When touching an area...
  - ...trim comments that narrate history instead of adding to them.
  - ...identify and remove dead code, even if it was written by someone else. If unsure, ask.
  - ...identify opportunities to simplify and improve stability and readability.
