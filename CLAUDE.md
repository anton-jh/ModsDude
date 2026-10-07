# ModsDude conventions

These rules override default behaviour.

## Project stage

- ModsDude is in alpha: one developer, no users. Existing data is test data.
- Never build backfills, data migrations for old rows, re-publish logic or backwards-compatibility paths unless asked. Just change behaviour going forward.
- Break the API and client state file formats freely; the API stays at v1. Regenerate the client in the same change. No version bumps, deprecation shims or format migrations.
  - Flag in the summary when a change means local state, the database or a cache must be cleared.
- Treat everything in `docs/` as out of date. Don't rely on it and don't update it; it will be removed or overhauled.

## Priorities

- Correctness and stability > consistency (code and UI/UX) > readability > performance. Correctness and stability carry by far the most weight.
- Exception: in heavy work (uploading, downloading, hashing, scanning mod folders, applying mods, packing saves), performance ranks right after correctness and stability. Design these paths for performance from the start.
- Elsewhere, optimise only when a real cost is shown.

## Workflow

- Plan every non-trivial task before writing code, and wait for a go-ahead. This applies even when the request reads like a spec or starts with "Plan:".
  - Why: design choices made without discussion have been wrong and had to be redone.
  - Trivial, unambiguous edits can just be done.
- Write plans as terse bullets, one fact each, grouped under short phase headings. No paragraphs. Open questions go only in a short list at the end.
- Plans include the refactoring and cleanup the change calls for (see "Changing existing code"), even if that grows the change.
- Never add a NuGet package without asking first. Propose it in the plan.
- When unsure mid-task (an ambiguous requirement, or a design choice the plan doesn't cover), stop and ask. Never guess.

## Subagents and task chips

- Use subagents for big, self-contained, read-heavy work where only the conclusion matters: broad codebase searches, tracing a system across many files, reviewing a large diff, independent parts that can run in parallel.
  - Why: only the subagent's final report enters the main context, so the context window fills more slowly.
- Keep work in the main session when it needs back-and-forth with the user, builds on context already gathered, or is a quick lookup. A subagent starts cold and re-reads what it needs.
- Tell a subagent exactly what to return (terse findings with `file:line` references), not raw file contents.
- Never copy rules from this file into task chip prompts. The new session loads this file itself. Give only task-specific context.

## Definition of done

A change is done only when all of these hold:

- The whole solution builds with zero warnings. Fix existing warnings too, not just new ones.
- Relevant tests pass.
- UI changes have been seen running. Ask first, then launch the real client; start the API if needed (`dotnet run --project ModsDude.Server/ModsDude.Server.Api`). The client logs in automatically.
- The full diff has been re-read against this file.

## Git

- Commit on the current branch, `main` included. Never create a branch unless asked, or unless a PR is requested.
  - Why: solo project; an unrequested branch turns one commit into a commit, a merge and a branch deletion.
- Commit after each task once it meets the definition of done.
- Commit messages: imperative subject line, then a few terse bullets. No prose paragraphs.

## Generated API client

- Never hand-edit `ModsDude.Client/ModsDude.Client.Core/ModsDudeServer/Generated.cs`, even when regeneration reorders unrelated operations.
- After any server API change:
  1. `pwsh scripts/openapi.ps1 -Update` rewrites `openapi/v1.json`. It generates the document during a build, so the dev server does not need to be running or stopped.
  2. `nswag run nswag-config.nswag` in `ModsDude.Client/ModsDude.Client.Core` regenerates `Generated.cs` from the checked-in `v1.json`.

## Architecture

### Boundaries

- `Server.Domain` references no framework or infrastructure packages (EF Core, ASP.NET, Azure).
- `Client.Core` never references WPF or UI concepts. Anything WPF lives in `Client.Wpf`.
- Use strongly typed IDs (`RepoId`, `ProfileId`, ...) everywhere inside. Raw `Guid`s and strings only in DTOs and the generated client.
- Prefer immutable records for models and DTOs. Entities change only through their own methods.

### Server

- Endpoints orchestrate: one class per endpoint, using `ApplicationDbContext` directly, checking authorization and calling domain methods. No handler, mediator or repository layers.
- Invariants live in rich domain entities, not in endpoints.
- The database backs every invariant it can express (unique indexes, foreign keys, check constraints), so concurrent requests can't slip past the domain check.
- Broken invariants throw (`DomainValidationException`). Expected outcomes such as not found, forbidden or conflict are typed `Results` returned by the endpoint.
- Every error carries a distinct problem type that gives the client enough information to react correctly. The HTTP status code is secondary; existing ones stay as they are.
- Every endpoint declares its authorization: the repo level on the route, or an explicit check in the endpoint. Nothing is reachable by default.
- Split an endpoint whose response mixes data for different access levels (for example admin-only and member data) into separate endpoints.
- EF Core: always add a new migration. Never edit or squash existing ones.
- Background jobs (Hangfire) keep their logic in testable code, not in the Hangfire wrapper.

### Client

- Use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`) wherever it fits, which is most places.
- Avoid logic in code-behind (`.xaml.cs`) when a binding, behavior or view model can reasonably do it.
- The app is useless without the server. When it is unreachable, show a clear offline state and block server-backed actions until it reconnects. Never build offline fallbacks on top of cached server data.

### Game adapters

- Game-specific knowledge belongs in client game adapters as much as possible. Adapters are compiled into `Client.Core`.
- Server modules for a single game (such as the ModHub crawler) are allowed and need not be generic. They count as part of the adapter, and only adapter code may talk to them.
- Games to design for: Farming Simulator and BeamNG.drive (+BeamMP) for mods and savegames; Minecraft, Stationeers and Space Engineers for savegames only. More will be added. Load order and dependencies between mods are planned for later.
- Adapters read and compute; they don't write. All writes go through the general engine as:
  - file placements: a mod version is one file (on the server and on disk), and the layout names the file it gets directly in the target's mod folder. Mod folders stay flat; a game whose mods are folders would need them packed like savegames;
  - pure transforms of an existing file (`GameFileEdit`): `current content + desired state → new content`, idempotent, and preserving entries the adapter doesn't manage. Addressed by a path relative to the mod folder or save slot, which may climb out of it with `..`. The adapter flags whether replaced content goes to the Recycle Bin; the engine does the recycling.
- The engine plans, validates, stages, applies, recycles and records every change, so the local file rules under "Correctness and stability" are implemented once.
- Escape hatch: an adapter may own a write step only when a game genuinely can't be expressed as placements or transforms. Raise it in the plan; its safety is reviewed case by case.
- Pass adapters full context of the whole operation (target, the full desired set, the current state), not one item at a time, so they see the whole picture.
- Capabilities (mods, savegames, remote updates, creating slots, and so on) are optional. The general code and UI adapt to what an adapter offers, never to which adapter it is.
- Never change a game's files while any of its processes are running. Refuse, and name the game.
- Always general, never in an adapter: the content store (hashing, dedupe, hardlinks), the safety guarantees, the server model and the UI.
  - Adapters influence the UI only through data (settings forms, attributes, savegame details, capability flags, names) and small presentation hints (for example which attribute to group or filter by, or an icon). The general UI renders them the same way for every game.

## Security

- Blob access only through short-lived, narrowly scoped SAS links.
- Never log tokens, SAS URLs or other credentials.
- Never commit secrets to `appsettings*.json` or source. Use user secrets or environment variables.

## Correctness and stability

- Output is deterministic: the same inputs give the same result in the same order. Sort explicitly; never depend on dictionary or enumeration order or on timing.
- Any server mutation the client may send more than once is idempotent. That covers automatic retries and a user clicking again: a repeat gives the same outcome, not a duplicate or an error.
  - Such requests carry a client-generated request ID. The server recognises a repeat by it and returns the original outcome, including when optimistic concurrency would otherwise reject the retry as stale.
  - The request ID only identifies the request. Never use it as an entity ID.
- Background jobs are idempotent and resumable: a retried, repeated or concurrent run is safe and continues where the last one stopped. They are also safe against API requests changing the same data at the same time.
  - A malicious actor getting into the Hangfire dashboard can't break the system by running jobs repeatedly or concurrently, nor extract any secrets or sensitive data from the job data.
- Concurrent edits to shared state use optimistic concurrency. A write based on a stale version is rejected, and the client says so and lets the user reload.
- Local file work (sync, apply, import, savegame check-in and check-out) must:
  - compute and validate a full plan before touching disk, refusing up front rather than failing halfway;
  - never leave a half-written file or folder that looks complete (write to a temp location, then move atomically);
  - converge on rerun: running again after a crash, kill or cancel reaches the same end state;
  - never lose user data: anything ModsDude doesn't own is left alone, and anything ModsDude deletes or replaces in a game's folders goes to the Recycle Bin.
- Handle a caught failure deliberately or rethrow it, and always log it with enough context to diagnose it later. Never catch and silently continue.
- Trust types and nullable annotations internally. Validate only at boundaries: API input, files, game data, network.

## Code rules

### Design

- Every service registered in DI gets an interface. View models, pages, windows, factories, options classes and middleware don't.
  - Hangfire job classes aren't registered at all; Hangfire builds them itself, in a scope per job.
- Get behaviour from structure, not logic: prefer derived state, types, data binding and composition over imperative code that keeps things in sync.
  - Why: flags, manual refreshes and re-entrancy guards cause "doesn't update until I touch something else" bugs.
  - Look for a single source of truth that everything else derives from, instead of adding another refresh call or guard flag.
- Write readable and maintainable code, not clever code.

### Async and time

- Pass `CancellationToken` through every async call chain.
- No fire-and-forget: every started task is awaited, or explicitly owned with its errors observed.
- No `async void` and no `.Result` / `.Wait()` / `GetAwaiter().GetResult()`, except where WPF requires it (such as event handlers).
- Never read the clock directly (`DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`). Use the time service or `TimeProvider`.
  - Exception: logging, error handling and startup code may read the clock directly.

### Files and size

- A file contains one main type, plus at most a few very small supporting types (such as strongly typed IDs or small records).
- Group by feature (Profiles, Savegames, ...) rather than by kind (Services, Models) where applicable.
  - WPF: one folder per feature with views next to their view models, `Shell/` for the app frame and `Shared/` for what several features use. Each feature maps its view models to views in its own `<Feature>Templates.xaml`, merged by `App.xaml`.
  - A view shown in the modal layer is named `<Name>Modal`, with its view model `<Name>ModalViewModel`.
- One responsibility per class. Split a very large class where it helps readability, even when it is all one concern. Never use `partial` classes for this.

### Comments and line endings

- Keep comments to a minimum. Use descriptive names instead of doc-comment essays on every member.
- A comment only describes what the code does now, never what it used to do or how it got here.
- Use CRLF line endings, except where `.gitattributes` says otherwise (`openapi/*.json` is LF). Correct any wrong line endings you find.

### Changing existing code

- Keep a high standard in all new code and in old code you touch. Do not assume existing code is good.
- Don't be afraid to overhaul or refactor a system when extending or modifying it. Reshape it so the new feature fits naturally, rather than bolting the feature onto a design that wasn't built for it.
  - Why: workarounds layered onto an ill-fitting design are where inconsistency and instability come from.
  - Prefer the refactor over special cases, flags or parallel code paths.
- Never leave two ways of doing the same thing. When a change introduces a better pattern, migrate every existing instance of the old one in the same change.
- When touching an area:
  - fix bad patterns instead of copying them into new code;
  - trim comments that narrate history instead of adding to them;
  - remove dead code, even if someone else wrote it. If unsure, ask;
  - look for opportunities to simplify and to improve stability and readability.

## Tests

- New or changed domain logic (`Server.Domain`) and core logic (`Client.Core`, including adapters) ships with tests in the same change.
- Background job logic ships with tests.
- Test anything else only when it is very specific or critical.
- Test the unhappy paths too: interruption, retries, cancellation and concurrent requests, not just the happy path.

## UI/UX

- UI text is English only, written directly in XAML and view models. No localisation infrastructure.
- Consistent look: reuse the shared styles and controls in the resource dictionaries. No one-off margins, colours or fonts.
- Use identical domain terms (repo, profile, revision, game, target, savegame) in UI text, client code and server code.
- No long sentences to describe state or information. Prefer a layout that makes it obvious what is static (labels, explanations) and what is dynamic (values, state), such as label/value pairs, badges or columns.
- Errors say what happened in plain, short words, without explaining why or how it happened. Never show raw exception text or status codes.
- Each kind of feedback has one channel:
  - An expected failure of something the user just did (validation, conflict) shows where they did it.
  - Something that genuinely broke while the user was doing something shows the error modal.
  - A background failure shows a notice that stays until it is resolved.
  - A completed action, whether the user started it or it ran in the background, shows a toast.
  - A destructive or hard-to-undo action asks for confirmation in a modal that names exactly what will be lost.
  - An unreachable server shows the offline state.
- Long operations (for example sync, import, upload, download, savegame check-in and check-out):
  - always show progress in the progress strip, never on buttons. Rows being processed may also show their own progress in addition to the strip;
  - can always be cancelled, and cancelling leaves a consistent state;
  - disable (not hide) the controls that would conflict with them;
  - keep running when the user navigates away, and stay tracked.
