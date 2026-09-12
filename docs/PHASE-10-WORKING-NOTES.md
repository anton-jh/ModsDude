# Phase 10 — working notes

Scratch for the sessions building [PLAN.md § Phase 10](PLAN.md#phase-10--one-game-many-targets).
**The plan is the specification; this is the order, the measurements and the traps.** Delete it when
the phase is done.

## What this actually is

One interface change, a keying band, one new loop, and a lot of removal. Nothing deep is rewritten.

| Layer | Fate |
| --- | --- |
| `ModSyncPlanner`, `ContentStore`, `FileLinks`, `RecycleBin`, `ModFileDownloader`, `SavegamePacker`, `ModContentHasher` | **Untouched.** They take data and paths, never ids |
| `ILocalModAdapter` | The root change: `ModFolder` → `ModTargets` |
| `SyncManifest(Store)`, `DriftService`, store eviction, `SavegameBindingStore`, `ModSource` | **Re-keyed**: `instanceId` → `(GameIdentity, TargetKey)`, or `GameIdentity` |
| `Game`, `GameRepository`, `LocalState` | Rewritten |
| `ProfileApplyService` | Gains the target loop and the activate/apply split |
| WPF | Mostly deletion |

**Sync stays per folder.** `ModSyncService.PlanAsync` goes on operating on one folder — that is
genuinely its unit of work. What changes is that `ProfileApplyService` loops over a game's targets
and calls it once each. Resisting the urge to make the sync engine target-aware is what keeps this
refactor a band in the middle rather than a rewrite.

## Blast radius, measured

78 files reference the moving symbols, excluding `obj/` and `bin/`:

| Project | Files |
| --- | --- |
| `ModsDude.Client.Core` | 32 |
| `ModsDude.Client.Wpf` | 31 |
| `ModsDude.Client.Core.Tests` | 15 |

Roughly one context per slice. Slice 5 may want two.

## Order

Sequential. It is a stack, not a set.

| | Slice | Files | |
| --- | --- | --- | --- |
| 1 | The adapter answers with targets | ~20 | **done** |
| 2a | The `Game` and its state | ~15 | |
| 2b | Re-key the per-folder stores | ~20 | |
| 3 | Savegames go per game | ~15 | |
| 4 | Activate and apply become two verbs | ~12 | |
| 5 | Interface | ~25 | |

Three things are independent and can land beside any of it:

- **Relocating publish** to the repo's Saves page. Purely additive, and it is what lets slice 5
  delete the sidebar without making publish unreachable in the meantime.
- **The store's filename encoding.** Landed with slice 1.
- **The documentation pass** over 02, 04, 05 and 06, at the very end.

## What batches, and what must not

**Batch** — these are one edit repeated, and splitting them means several rounds of half-compiling:

- `ModTargets` + `SavegameSlotRef` + the FS adapter returning one target (slice 1) — one contract.
- `RequireSingleTarget()` across every caller (slice 1) — one mechanical pass. Estimated ~16 sites;
  it was **two**, because widening `GetInstalledMods` and `GetModFilePath` to take the `ModTarget`
  turned the rest into threading one value through rather than re-deriving a folder at each site.
  `ModSyncService.PlanAsync` and `LocalInstanceRepository.GetModFolder` are the whole list, which is
  also where 2b has to reach in from the game loop.
- The four re-keyings in 2b — manifest, drift, eviction, mod sources.
- Everything in slice 3. It is one concept with shared test fakes.

**Do not batch** — **renames go in their own commits**, separate from semantic change. A pure rename
is reviewable by "it compiled"; mixed into a behaviour change it hides the behaviour change. This
matters more than usual here, because the rename table touches most of the 78 files.

## Traps

- **Multi-target code would otherwise never have run.** FS has one target and the BeamNG adapter does
  not exist. Hence the fake with settings-driven optional targets, in slice 1, before anything needs
  it. It is the single highest-value item in the phase.
- **Removing a target orphans things.** Emptying its settings field takes away a target that has a
  manifest and possibly a held savegame behind it. A stale manifest is droppable; a **binding is a
  savegame this machine is still holding** and must not vanish with a settings edit. An adapter
  author renaming a target key is indistinguishable from a removal.
- **`GameIdentity` as a JSON dictionary key.** A record struct needs a converter or it silently
  serializes as an object. The version bump means the failure is "everything resets" rather than
  corruption, but round-trip a `LocalState` with two games.
- **Play attribution.** Two tests: a failed apply attributes nothing, and an apply landing on target
  A while target B holds a save uses **B's** outgoing revision.
- **Slot collisions are not a risk** — `SavegameSlotRef` makes uniqueness a construction. Do not
  reintroduce global uniqueness as an adapter obligation.

## Definition of done, per slice

Three test projects green, the app launches, and the Farming Simulator flow works end to end —
import, pin, apply, drift, check out, check in. FS exercises one target only, so the multi-target
half is covered by the fake adapter's tests and nothing else until a real BeamNG adapter exists.
