# Phase 10 — working notes

Scratch for the sessions building [PLAN.md § Phase 10](PLAN.md#phase-10--one-game-many-targets).
**The plan is the specification; this is the order, the measurements and the traps.** Delete it when
the phase is done.

## What this actually is

One interface change, a keying band, one new loop, and a lot of removal. Nothing deep is rewritten.

| Layer | Fate |
| --- | --- |
| `ModSyncPlanner`, `ContentStore`, `FileLinks`, `RecycleBin`, `ModFileDownloader`, `ModContentHasher` | **Untouched.** They take data and paths, never ids |
| `SavegamePacker` | Not untouched after all: it asks an adapter where a slot is, so it takes the target as well as the slot. Still no ids |
| `ILocalModAdapter` | The root change: `ModFolder` → `ModTargets` |
| `ILocalSavegameAdapter` | The same change in slice 3: `SavegameTargets`, and a target on `GetSlots` and `GetSlotPath` |
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
| 3 | Savegames go per game | 42 | **done** |
| 4 | Activate and apply become two verbs | 6 + 7 + 13 | **done** |
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

**3 was estimated at ~15 and was 42**, in one commit, and the hole was the shape the note above
predicted plus one it did not: **the adapter contract**. `ObserveAsync` and the manifest lookups were
about ten files and went as expected; the picker, the slot list and the hold status were another ten,
as predicted. The twenty nobody counted are what giving `ILocalSavegameAdapter` targets costs — the
packer takes a target now, and so does every fake adapter and every test harness that ever addressed
a slot. Slice 5 has no contract change in it, so its ~25 is probably honest.

**4 was estimated at ~12 and was 26 across three commits** — the rename (6), the drift split (7) and
the two verbs (13) — which is the first estimate in the phase that was only about twice out. The
reason is worth knowing: **this slice changed no keys**. Nothing was re-addressed, so nothing fanned
out; what it touched, it touched deliberately. The commits split cleanly along the rename rule and
each one compiled and was green on its own, which none of 2b's or 3's could have been.

Three things are independent and can land beside any of it:

- **Relocating publish** to the repo's Saves page. Purely additive, and it is what lets slice 5
  delete the sidebar without making publish unreachable in the meantime.
- **The store's filename encoding.** Landed with slice 1.
- **The documentation pass** over 02, 04, 05 and 06, at the very end. **Add 10 to that list, and
  expect it to be the biggest single piece**: it is written end to end in the instance vocabulary,
  its apply table and check-out table are per instance, and slices 4 and 5 change what those tables
  say. 04 is kept current as each slice lands instead, because it is the contract an adapter author
  reads — slice 3 gave it the savegame half of a target.

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
- Everything in slice 3. It is one concept with shared test fakes. **It was, and it had to be**: the
  slot reference on the binding, the target on the savegame adapter and the target on the packer are
  one edit seen from three sides, and nothing between them compiles on its own. One commit of 42
  files, which is the largest single one of the phase and the right shape for it.

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
  with activation in their names. **It did not collide**: the pages' own members are
  `ActivationKind`, `ActivationLabel` and `ActivationStatus`, and the type is `ProfileActivation`,
  so the prefix that looked redundant is what kept them apart. The collision to watch for in slice 5
  is the other direction - `Game` the model beside `Game` the property name, which several view
  models already have.
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
  A while target B holds a save uses **B's** outgoing revision. **Both there, and the second is the
  whole slice in one test**: the server's apply attributes nothing while the MP client holds the
  evening, and the client's apply then attributes it to 4 rather than to the 1004 the server moved
  to. It only discriminates because the two folders are on different revisions between the two
  applies, which is the ordinary state for the seconds an activation takes.
- **Slot collisions are not a risk** — `SavegameSlotRef` makes uniqueness a construction. Do not
  reintroduce global uniqueness as an adapter obligation.

- **`CheckDriftAsync` kept the game as its key, and that was the right call.** The plan said both
  halves go per target; three of the four keys did. The drift check answers a *list*, its cost is one
  hash per held slot, and the holds are the game's — so a call per folder would re-read the same list
  N times. What went per target is the part that was wrong: each answer is classified against its own
  target's manifest and carries the target it is about, and `DriftMonitor` places it on that folder's
  entry. See PLAN, where the bullet now says so.

- **The notice reads every entry of the game it is showing**, which is the consequence of that
  placement and was nearly missed. `Drifted.FirstOrDefault()` picks one entry; with the savegame half
  now sitting on its own folder's entry, a game whose server folder drifted and whose client folder
  holds an unchecked-in evening would have shown the mod half and said nothing about the save. The
  notice gathers the savegame half across the game's entries and passes it to `DescribeHeadline` and
  `DescribeSavegames` separately from `report`. **Slice 5 rewrites both of those sentences — keep the
  gathering.**

- **`TryReadAgreed` did not die here.** Three of its four callers became lookups, as planned. The
  fourth is the savegame row's *can this be taken here*, asked before anybody has chosen a slot, and
  that is a genuine question about the game rather than scaffolding: every target follows one profile,
  so where they all agree the row can state it, and where they do not the honest answer is "apply the
  profile first". Its doc comment says that now instead of naming slice 3.

- **The fake adapter earned its place a second time**, exactly where the plan said it would: a target
  with savegames and no mod folder is a shape fixtures cannot make, and it is what proved the drift
  monitor needed a game-level entry for holds that belong to no folder entry.

- **An unreachable hold is a row, not a slot.** `SavegameSlotRowViewModel.ForUnreachableHold` builds
  one from the binding alone — there is no slot to build it from, which is the state — and it offers
  Disconnect and nothing else: check-in and discard both need a folder to pack or recycle, and
  `RequireTarget` refuses them before the server is told anything. Slice 5 rewrites this page's
  neighbours; the row shape should survive it.

- **`SlotGrouping` is shared by the two slot lists** and groups only where a row carries a target
  name, which `SavegameService` sets only for a game with more than one savegame folder. Slice 5's
  flat check-out list is that same call.

- **FS's two halves share one key**, `FarmingSimulatorTarget.Key`, still spelling `mods`. Renaming it
  to something that reads better for a savegame folder would orphan every manifest and binding on
  every machine, which is the rule this phase spends a bullet on — a key is not a description.

- **`RecordsIntent` was deleted rather than made structural, and that is what structural meant.**
  The plan said to check the refusals, record the intent, then do the work; once `ActivateAsync` does
  that in that order, a property saying *whether* to record one has nothing left to decide. Every
  caller that used to ask it now names the verb it meant instead — which turned out to be the whole
  point: two of them had been calling one method and telling the difference afterwards. The outcome
  keeps an `Activated` flag, and it is a fact rather than a rule: it is read only by pages with their
  own bookkeeping to do about it.

- **The savegame page was the site that proved the order matters.** Its unrecognised-mods branch
  bypasses `ProfileApplyService` and executes plans itself, and it recorded the intent *after* the
  loop — so an exception mid-apply left a half-applied folder and a game still following the old
  profile, which is exactly the state slice 4 exists to remove. It records before the loop now.
  **Slice 5 should look at that branch again**: it is the last place that applies without going
  through the two verbs, and its Review path is a third way of saying "left drifted deliberately".

- **`ProfileApplyService` is in the WPF project and has no tests, which is now the phase's biggest
  untested seam.** What can be exercised from `ModsDude.Client.Core.Tests` is the rule material -
  `ProfileActivation`, `ProfileApplyTarget`, `SavegameHoldRules`, the drift split - and all of it is.
  What is not is the *order*: refuse, ask, record, work. It is four statements in one method and it
  is the guarantee the whole slice rests on. Moving the service into Core would need `IModalService`
  and `IBackgroundTaskReporter` to go with it, which is a bigger move than this slice wanted; worth
  considering when slice 5 has finished moving the surfaces around.

- **`NeverSynced` was meant to stay quiet and does not, and the argument for the reversal is the one
  the whole slice rests on.** The plan said no manifest promises nothing; I first added a second
  argument for the same conclusion - `SyncManifestStore.TryRead` answers null for a locked file, a
  half-written one and an older format too, so making absence drift would fire on every game at once
  after a manifest format bump. Both were wrong, for the same reason: **the active profile and the
  manifest are both local state written by the same client**, so an intent standing with no record of
  any work behind it is a statement about this machine rather than an absence of one. Either nothing
  was ever applied here - a first activation whose apply failed, which is precisely the BeamMP evening
  on a folder nobody had applied to yet - or the record was lost, in which case drift detection is
  blind for that folder and nobody is told. The format-bump case turns out to argue the same way: "your
  profiles need applying again" on every game is the honest outcome, and the alternative is every
  folder silently unchecked. All three of the pre-comparison states are drift now.

- **The re-apply button does not name the folder; the sentence does.** The plan's bullet reads as
  though the button should, but the action applies the whole game - every folder, the ones already
  right included - and a button claiming to act on one of them would be lying about its scope. The
  `NotApplied` and `FolderRepointed` sentences carry *"in the 'server' folder"* instead, by key,
  because the notice is up before the repo list loads and a display name needs a hydrated adapter.
  **Slice 5 is where every folder name in the app gets one answer**, and this is one of the sites.

- **"One installation" is the conflation wearing a different word, and it got into three comments
  before anybody caught it.** A game reaching three targets is *usually three installations* - a
  dedicated server, an MP client and a singleplayer copy are separate downloads in separate folders -
  and this system has never modelled installations at all; the superseded decision in PLAN says so
  outright. What a machine has one of is the **policy holder**: one active profile, one savegame
  hold, for however many folders the adapter reaches. "A machine configures that game once" is the
  sentence that means that. **Slice 5's documentation pass wants this on its list**: 02 and 06 still
  say "one installation is configured once", which was true of an instance and is not true of a game.

- **"Save and apply, always" is not what landed.** With the target collapsed to a lookup the plural
  wording died, which is what that bullet was about, but the zero case is still *Save changes*: a
  profile no game follows yet is the onboarding case the editor already handles with an offer after
  the save, and a button promising an apply that would reach nowhere is a worse lie than a plain one.

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

**4 had almost nothing new to cover, which is itself the measurement.** Its rules are pure and were
already under test - what changed is where they are called from and in what order. The drift split
got five tests (three statuses in `DriftServiceTests`, and in `DriftMonitorTests` that an activation
which did not land reaches the notice, that a folder with no manifest does too, and that a game
following no profile is the one state that stays quiet), and
`ProfileApplyTarget` was rewritten around the lookup. The order the verbs run in is untested and
cannot be tested from here; see the trap about `ProfileApplyService` living in the WPF project.

**3 split the same way, and the split is worth repeating in 4 and 5.** The fake adapter holds the
pairing — that both halves come from one settings form under one key, that either half can be absent
— and the service harness grew a second savegame folder for everything else, because what a hold, an
observation and a check-in need is *two keyed folders on two revisions* and the fake would have put a
settings round trip in the middle of a test about attribution. Three test projects are green
(650 + 239 + 109 after slice 4), and the solution builds; the Farming Simulator flow is one target
and unchanged by these slices, so the multi-target half is those tests and nothing else until a
BeamNG adapter exists.
