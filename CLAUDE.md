# ModsDude conventions

These rules override default behaviour. Each has a one-line reason.

## Project stage

- ModsDude is in alpha: one developer, no users. Existing data is test data.
- Never build backfills, data migrations for old rows, re-publish logic or backwards-compatibility paths unless asked. Just change behaviour going forward.

## Workflow

- Plan every non-trivial task before writing code, and wait for a go-ahead. This applies even when the request reads like a spec or starts with "Plan:".
  - Why: design choices made without discussion have been wrong and had to be redone.
  - Trivial, unambiguous edits can just be done.
- Write plans as terse bullets, one fact each, grouped under short phase headings. No paragraphs. Open questions go only in a short list at the end.

## Git

- Commit on the current branch, `main` included. Never create a branch unless asked, or unless a PR is requested.
  - Why: solo project; an unrequested branch turns one commit into a commit, a merge and a branch deletion.

## Generated API client

- Never hand-edit `ModsDude.Client/ModsDude.Client.Core/ModsDudeServer/Generated.cs`, even when regeneration reorders unrelated operations.
- After any server API change:
  1. `pwsh scripts/openapi.ps1 -Update` regenerates `openapi/v1.json` at build time. The dev server does not need to be running or stopped.
  2. `nswag run nswag-config.nswag` in `ModsDude.Client/ModsDude.Client.Core` regenerates `Generated.cs` from the checked-in `v1.json`.

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