# ModsDude conventions

These rules override default behaviour.

## Project stage

- ModsDude is in alpha: one developer, no users. Existing data is test data.
- Never build backfills, data migrations for old rows, re-publish logic or backwards-compatibility paths unless asked. Just change behaviour going forward.
- Treat everything in `docs/` as out of date. Don't rely on it and don't update it; it will be removed or overhauled.

## Priorities

- Correctness and stability > consistency (code and UI/UX) > readability > performance. Correctness and stability carry by far the most weight.
- Optimise only when a real cost is shown.

## Workflow

- Plan every non-trivial task before writing code, and wait for a go-ahead. This applies even when the request reads like a spec or starts with "Plan:".
  - Why: design choices made without discussion have been wrong and had to be redone.
  - Trivial, unambiguous edits can just be done.
- Write plans as terse bullets, one fact each, grouped under short phase headings. No paragraphs. Open questions go only in a short list at the end.
- Plans include the refactoring and cleanup the change calls for (see "Changing existing code"), even if that grows the change.
- Never add a NuGet package without asking first. Propose it in the plan.

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

### Server

- Endpoints orchestrate: one class per endpoint, using `ApplicationDbContext` directly, checking authorization and calling domain methods. No handler, mediator or repository layers.
- Invariants live in rich domain entities, not in endpoints.
- The database backs every invariant it can express (unique indexes, foreign keys, check constraints), so concurrent requests can't slip past the domain check.
- Broken invariants throw (`DomainValidationException`). Expected outcomes such as not found, forbidden or conflict are typed `Results` returned by the endpoint.
- Every error carries a distinct problem type that gives the client enough information to react correctly. The HTTP status code is secondary; existing ones stay as they are.
- EF Core: always add a new migration. Never edit or squash existing ones.

### Client

- Use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`) wherever it fits, which is most places.
- Avoid logic in code-behind (`.xaml.cs`) when a binding, behavior or view model can reasonably do it.
- The app is useless without the server. When it is unreachable, show a clear offline state and block server-backed actions until it reconnects. Never build offline fallbacks on top of cached server data.

### Game adapters

- Game-specific knowledge belongs in client game adapters as much as possible. Adapters are compiled into `Client.Core`.
- Server modules for a single game (such as the ModHub crawler) are allowed and need not be generic. They count as part of the adapter, and only adapter code may talk to them.
- Games to design for: Farming Simulator and BeamNG.drive (+BeamMP) for mods and savegames; Minecraft, Stationeers and Space Engineers for savegames only. More will be added. Load order and dependencies between mods are planned for later.
- Adapters read and compute; they don't write. All writes go through the general engine as:
  - file placements: the adapter declares which files a mod version or save consists of, at which relative paths;
  - pure transforms of an existing file: `current content + desired state → new content`, idempotent, and preserving entries the adapter doesn't manage (for example BeamNG's `db.json`, or renaming a save).
- The engine plans, validates, stages, applies, recycles and records every change, so the local file rules under "Correctness and stability" are implemented once.
- Escape hatch: an adapter may own a write step only when a game genuinely can't be expressed as placements or transforms. Raise it in the plan; its safety is reviewed case by case.
- Pass adapters full context of the whole operation (target, the full desired set, the current state), not one item at a time, so they see the whole picture.
- Capabilities (mods, savegames, remote updates, creating slots, and so on) are optional. The general code and UI adapt to what an adapter offers, never to which adapter it is.
- Never change a game's files while any of its processes are running. Refuse, and name the game.
- Always general, never in an adapter: the content store (hashing, dedupe, hardlinks), the safety guarantees, the server model and the UI.
  - Adapters influence the UI only through data (settings forms, attributes, savegame details, capability flags, names) and small presentation hints (for example which attribute to group or filter by, or an icon). The general UI renders them the same way for every game.

## Correctness and stability

- Output is deterministic: the same inputs give the same result in the same order. Sort explicitly; never depend on dictionary or enumeration order or on timing.
- Any server mutation the client may send more than once is idempotent. That covers automatic retries and a user clicking again: a repeat gives the same outcome, not a duplicate or an error.
  - Such requests carry a client-generated request ID. The server recognises a repeat by it and returns the original outcome, including when optimistic concurrency would otherwise reject the retry as stale.
  - The request ID only identifies the request. Never use it as an entity ID.
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

- Every service registered in DI gets an interface. View models, pages, windows and options classes don't.
- Get behaviour from structure, not logic: prefer derived state, types, data binding and composition over imperative code that keeps things in sync.
  - Why: flags, manual refreshes and re-entrancy guards cause "doesn't update until I touch something else" bugs.
  - Look for a single source of truth that everything else derives from, instead of adding another refresh call or guard flag.
- Write readable and maintainable code, not clever code.

### Async and time

- Pass `CancellationToken` through every async call chain.
- No fire-and-forget: every started task is awaited, or explicitly owned with its errors observed.
- No `async void` and no `.Result` / `.Wait()` / `GetAwaiter().GetResult()`, except where WPF requires it (such as event handlers).
- Never read the clock directly (`DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`). Use the time service or `TimeProvider`.

### Comments and files

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

- Every new or changed piece of logic ships with tests in the same change.
- Test the unhappy paths too: interruption, retries, cancellation and concurrent requests, not just the happy path.

## UI/UX

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
