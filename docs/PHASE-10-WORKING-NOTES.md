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

**It held through 2b, and the line came out exactly there.** `PlanAsync` gained a `ModTarget` on its
request and nothing else; the loop lives in `ProfileApplyService` and in the sync page. The one place
the line is visibly load-bearing is the eviction pin pass, which is per-folder work inside a
per-folder method and had to learn that **a game's other folders are other folders** — its skip was
the game, so syncing the MP client evicted what the dedicated server was running.

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
| 2a | The `Game` and its state | 117 + 51 | **done** |
| 2b | Re-key the per-folder stores | 18 + 48 + 3 | **done** |
| 3 | Savegames go per game | ~15 | |
| 4 | Activate and apply become two verbs | ~12 | |
| 5 | Interface | ~25 | |

**2a was ~15 files in two commits of 117 and 51.** The estimate was of the *semantic* half and it was
about right — 51 files, nearly all of them a parameter type. The rename was the other 117, and it is
only that large because "instance" was in the prose of every file that mentioned a mod folder. Worth
knowing before slice 5: the rename table's remaining rows are all in files that slice rewrites
anyway.

**2b was estimated at ~20 and was 48**, in three commits: the drift rename (18), the re-keying (48)
and the sweep (3). The estimate was of the stores, and the stores were the easy half — the four
re-keyings are about ten files and went exactly as the batch note predicted. **The other thirty are
the fan-out reaching the interface**, which the estimate did not account for at all: deleting
`RequireSingleTarget` means every caller that held a game now loops its folders, and four WPF
surfaces turned out to be holding one plan, one report or one sentence where there are now N. Slice
3's estimate has the same shape of hole in it — `ObserveAsync` and `CheckDriftAsync` going per target
is cheap, and the check-out list, the picker and the hold status are where the work is.

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
  `ModSyncService.PlanAsync` and `GameRepository.GetModFolders` are the whole list, which is
  also where 2b has to reach in from the game loop. **It did, and both reached in upwards rather
  than down**: `PlanAsync` takes the target on the request and the loop went to
  `ProfileApplyService`, and `GetModFolders` became `GetTargets` and returns what gets persisted.
- **The key swap in 2a** — dropping the `Guid` forces `instanceId` → `GameIdentity` through the
  manifest, the bindings, eviction, the drift check, the integrity check and the mod source id in one
  go, because each of them is a parameter type on a call the others make. One compile error at a time
  would have been six half-broken builds.
- The four re-keyings in 2b — manifest, drift, eviction, mod sources. **2a already moved them once**,
  from a `Guid` to a `GameIdentity`; 2b widens the same parameters to carry a `TargetKey` beside it.
  **Right, and it was one `ModTargetRef` rather than four parameter pairs** — the compound value is
  what let the persisted target list, the eviction pin set and the sweep's expected-name set all be
  keyed on the same thing without any of them building the join themselves. Threading two parameters
  would have been the same edit four times over with four chances to pass somebody else's identity.
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
  author renaming a target key is indistinguishable from a removal. The fake makes the transition
  reachable in slice 1; the two answers belong where the two things are keyed — **the manifest in
  2b, the binding in 3** — because until then nothing is keyed on a target to orphan.
- **`GameIdentity` as a JSON dictionary key.** A record struct needs a converter or it silently
  serializes as an object. The version bump means the failure is "everything resets" rather than
  corruption, but round-trip a `LocalState` with two games. **Done in 2a, and the failure is worse
  than advertised**: `JsonConverter<T>`'s `ReadAsPropertyName`/`WriteAsPropertyName` *throw* by
  default rather than falling back to the object form, so the state file would not have saved at all.
  `LocalStateTests` round-trips two games and asserts the key is a property name somebody can read.
- **Manifests from before 2a are orphans on disk.** They are named `{guid}.json` and nothing will
  ever look for one again. Harmless — the state that named them was discarded by the same version
  bump, and a manifest nobody reads costs a few hundred kilobytes — but **2b's stale-manifest sweep
  should drop any file it does not expect**, not only the ones whose target key has gone, and that is
  what collects these. **Done, and it worked on the first real launch**: the log says
  `Dropped the stale sync manifest 52eebec3-….json`. Sweeping by *what is still expected* rather than
  by what has gone is what made one pass collect the guid-named files, the renamed keys and a `.tmp`
  an interrupted write left behind. It runs from `GameRepository`'s constructor as well as after
  every edit, because a version bump changes the expected set without any edit having happened.
- **`Game.SingleModFolderOrNone` is the second narrowing, and it is the quiet one.**
  `RequireSingleTarget` throws because sync cannot proceed; this returns null for *several* as well as
  for none, because its callers are the drift check and the import's source list and neither can
  throw on a background thread. Several is unreachable while sync refuses such a game anyway. Both
  die in 2b; a `grep` for either is the checklist. **Both gone, and the grep is clean.**

- **The savegame side had to be given an answer before slice 3, which was not planned for.** A
  binding is keyed on the game and a slot id, and four call sites ask "which mod list is this game
  on" — attribution, the drift classification, the check-in revision and the publish dialog. Once the
  manifest is per target a game has N answers to a question asked of the game. Picking the first is
  the silent-first-target-wins failure this phase exists to prevent, and throwing is unacceptable on
  a background thread, so it is `SyncManifestStore.TryReadAgreed`: **every target of a game follows
  one profile, so where they all report the same profile and revision the ambiguity does not
  matter**, and where they do not it is unknown, which records nothing. Slice 3 puts the target key
  on the binding and this becomes a lookup — **delete it there**, it is scaffolding with a date on
  it. It also gave `SavegameService` an `IModFolders`, which slice 3 should be able to take back out.

- **A sentence and an enum cannot share a name.** `GamePageViewModel.DriftStatus` was a bound string,
  and it collided with `DriftStatus` the moment the rename took the prefix off the enum. It is
  `DriftNote` now, which is what the sync page had always called the same sentence. Worth expecting
  again in slice 4: `InstanceActivation` → `ProfileActivation` lands next to view-model properties
  with activation in their names.
- **The notice's subject is a target now, and slices 4 and 5 need to know the shape.**
  `InstanceDrift` became `TargetDrift(Game, Target, Report, ProfileName)` — one per folder, with
  `Target` null for the entry that is about the game rather than one of its folders (it reaches none,
  or follows no profile). **The savegame half is the game's and is carried on every one of its
  entries**, which is what keeps one notice saying both halves rather than two racing to; a
  consequence is that any count over `Drifted` has to decide whether it is counting games or folders.
  `DescribeHeadline` counts distinct games, because "3 games have drifted" for one game's three
  folders is a lie. Slice 5's *"your MP client folder has 2 differences"* is a detail line on that
  same entry.

- **Play attribution.** Two tests: a failed apply attributes nothing, and an apply landing on target
  A while target B holds a save uses **B's** outgoing revision.
- **Slot collisions are not a risk** — `SavegameSlotRef` makes uniqueness a construction. Do not
  reintroduce global uniqueness as an adapter obligation.

## Definition of done, per slice

Three test projects green, the app launches, and the Farming Simulator flow works end to end —
import, pin, apply, drift, check out, check in. FS exercises one target only, so the multi-target
half is covered by the fake adapter's tests and nothing else until a real BeamNG adapter exists.

**2b's multi-target half is covered by fixtures with a second target rather than by the fake
adapter**, which turned out to be the right shape for the stores: what the manifest, the eviction
sweep and the drift monitor need is *two keyed folders*, and standing a whole adapter up to get them
would have put a settings round trip in the middle of a test about a file name. The fake earns its
place where the *targets themselves* are under test — that a settings edit removes one — and slice 3
is where it comes back, because a savegame folder without a mod folder is a shape only it can make.
