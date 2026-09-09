# 10 — Savegames and profile revisions

*Not implemented.* This describes a design, not the current tree. For what the code does
today see [07 — Mod sync design](07-mod-sync-design.md#drift) and
[08 — Known issues](08-known-issues.md).

The application is in early development. Nothing here migrates existing local or server state,
and no shape below is constrained by what an older client wrote.

## Two successions, not one

Two different things advance over time, and this document is about how they relate.

**Versions of one savegame.** A `Savegame` has a linear history of `SavegameVersion` rows,
numbered from 1. A check-in mints one. They are snapshots of the same farm at different points,
and its head is the newest. Each version records the profile revision it was played on.

**Savegames on one profile.** A `Profile` has a succession of `Savegame` rows. These are
separate farms — separate names, separate claims, separate version histories numbered from 1
each. Starting a second farm on "Old-school" creates a second `Savegame`; it does not add a
version to the first.

| | Versions | Savegames on a profile |
| --- | --- | --- |
| Belong to | One savegame | One profile |
| Created by | Check-in, publish, restore | Publish |
| Newest is called | The **head** version | The **current** savegame |
| Older ones are called | Earlier versions | **Past** savegames |
| Numbered | Yes, from 1 per savegame | No |

## Cardinality

A profile has **at most one current savegame**, enforced. Any other savegame on it is past.

Only *current* is one-to-one. Past savegames keep pointing at the profile — their versions name
its revisions, and `SavegameVersion`'s foreign key onto `ProfileRevision` is `Restrict`. A
profile therefore has many savegames, at most one of which is current.

Current is `SupersededAt IS NULL`, and one-per-profile is enforced by a filtered unique index —
the same shape as the one that makes at most one claim open per savegame
([SavegameCheckoutEntityTypeConfiguration.cs:27](../ModsDude.Server/ModsDude.Server.Persistence/EntityTypeConfigurations/SavegameCheckoutEntityTypeConfiguration.cs)):

```csharp
builder.HasIndex(x => new { x.RepoId, x.ProfileId })
    .IsUnique()
    .HasFilter("\"SupersededAt\" IS NULL");
```

Two details it turns on:

- **Not also filtered on `ArchivedAt`**, unlike the savegame-name index directly above it in the
  same file. Archiving does not change current or past, so an archived savegame still holds its
  profile's slot and a second one must not be able to take it.
- `ProfileId` is nullable, and PostgreSQL treats nulls as distinct in a unique index. Savegames
  with no profile are unconstrained by it, which is the intent.

Making a past savegame current again is a swap, and the index rejects the intermediate state where
both are current. The outgoing row has to be superseded before the incoming one is cleared, which
is an ordering the two updates must guarantee rather than leave to the change tracker.

A profile is durable identity across a succession of savegames. "Old-school" stays one profile
when the group starts a new farm on it; the previous farm becomes past.

An instance holds **at most one checked-out savegame** at a time.

A savegame's profile is fixed at publish. **There is no operation that moves a savegame to a
different profile** — `UpdateSavegameV1Endpoint` becomes a rename. Moving one would make
`Savegame.ProfileId` and every version's `ProfileId` disagree, and revision numbers of two
profiles are not comparable
([SavegameDrift.cs:168](../ModsDude.Client/ModsDude.Client.Core/Savegames/SavegameDrift.cs)).

A user who wants the effect republishes the farm, which is three existing operations:

1. Check it out, so the play is in a slot.
2. `DiscardAsync` — hands the claim back and clears the binding, leaving the slot as an ordinary
   unrecognised one. `Forget` alone is not enough: it is local, and the claim would stay open on a
   savegame nobody is holding.
3. Publish that slot to the target profile, as a new savegame with its own history.

The original savegame stays where it is, on its old profile, with its history intact. Nothing is
moved and nothing is rewritten.

The same three steps are the only route from a savegame with no profile to one that has a
profile, since nothing connects an existing savegame to one.

Running two farms in parallel on one mod list is done by branching the profile —
`POST repos/{repoId}/profiles` with `CopyFrom`, which exists today
([CreateProfileV1Endpoint.cs](../ModsDude.Server/ModsDude.Server.Api/Endpoints/Profiles/CreateProfileV1Endpoint.cs)).

## Savegames without a profile

`Savegame.ProfileId` is optional, and so are `SavegameVersion.ProfileId` and
`SavegameVersion.ProfileRevision`.

The connection is optional in every repo, not only where the adapter lacks mod support. Adapters
with savegame support and no mod support are planned and have no profile to offer; a savegame in
a mod-capable repo may equally be published without one, and publish offers that as a choice.

A savegame with no profile has no current or past state, records no revision, and takes no part
in anything below. Check-out writes the slot and takes the claim; no profile is applied, no apply
is refused on its behalf, and nothing reports it as drifted from a mod list. It is unmanaged, by
the publisher's choice.

A null revision is a valid state meaning "this version is not connected to a profile". The
invalid state is a half-set pair, and it is a database check constraint: `ProfileId` and
`ProfileRevision` are both null or both set, on `Savegame` and on `SavegameVersion`.

A savegame cannot acquire or lose a profile, since nothing moves one between profiles. A history
mixing versions that name a revision with versions that do not therefore cannot arise.

Nothing is left for the client to enforce about whether a profile is present.
`SavegameService.RequireAppliedRevision` still throws where a profile *was* chosen and the folder
has not been synced to it, since a version that names a profile has to name a revision of it too.

## Profiles with no savegame

A profile with no savegame is the ordinary starting state, not a special one. Every profile
begins here, and a profile used only as a mod list — a template to branch from, or a repo whose
group never publishes a save — stays here.

| | Behaviour |
| --- | --- |
| Editing the profile | Unrestricted. Every save mints a revision as it does today |
| Applying it to an instance | Applies head. No savegame to attribute play to, and no past-savegame refusal |
| Checking out | Nothing to check out |
| Publishing to it | Creates the profile's first savegame, which becomes current. Nothing becomes past, and there is no confirmation to show |

A profile returns to this state when its current savegame is deleted. Past savegames stay past
and are not promoted, and the next publish creates a new current savegame.

## Repos that do not support savegames

`IBaseGameAdapter.CanSupportSavegames` is a client-side capability read from the adapter's base
settings. Where it is false, savegames do not exist in the repo: `SavegameService` returns no
adapter, and the savegames pages are not offered
(`RepoPageViewModel`, `InstancePageViewModel`, `InstanceSavegamesPageViewModel`).

Nothing in this document applies to such a repo.

| | Behaviour |
| --- | --- |
| Current and past | Do not exist. No savegame does |
| Applying a profile | Always applies head. Never refused on savegame grounds |
| One checkout per instance | Vacuous |
| `Observe()`, `LastObservedHash`, `LastPlayedRevision` | Never run and never written. They live on the checkout binding, and no binding is ever taken |
| `SyncManifest.ProfileRevision` | Still recorded, as it is today. It describes the folder, not a savegame |

The server is unchanged either way. It has no notion of `CanSupportSavegames`, and a repo whose
adapter does not support savegames is one where no client ever creates any.

## Current and past savegames

A past savegame is **not read-only**. It can be checked out, played, and checked in, and doing
so mints versions as normal. The single restriction is that **its profile revision does not
move**.

Two things change which savegame is current, and both are stated before they run:

- **Publishing** a new savegame to the profile. The savegame that was current becomes past.
- **Making a past savegame current again.** Whichever savegame held the slot becomes past.

Both are the same swap seen from either end, and the count never exceeds one either way. There is
no action that supersedes a savegame on its own, leaving a profile with no current savegame —
that state is reached only by deleting the current savegame.

Savegames are not listed under the profile. They stay on the one repo-level list
([GetSavegamesV1Endpoint](../ModsDude.Server/ModsDude.Server.Api/Endpoints/Savegames/GetSavegamesV1Endpoint.cs):
*"One repo-level list, not a list per profile"*), which gains a column saying whether each row is
its profile's current savegame or a past one, and at which revision.

### Not the same as archived

"Archived" is the repo-wide visibility state — `IArchivable`, a nullable `ArchivedAt`, the
Archive page, and the precondition for permanent deletion
([02 — Domain model](02-domain-model.md#archiving)). It applies to profiles and repos as well.

Past is unrelated, and archived savegames get no special treatment here: a savegame can be
current or past, archived or not, in any combination. The suggested field is
`Savegame.SupersededAt`, distinct from `ArchivedAt`.

Archiving therefore does not change current or past. A profile whose current savegame is
archived still has a current savegame, and that is stated where the profile is shown, along with
the three ways out: un-archive it, delete it, or publish a new savegame to replace it.

`DeleteSavegameV1Endpoint` refuses a savegame that is not archived, so deleting a current
savegame is two steps — archive, then delete — and it remains current in between.

## Which revision a savegame runs on

| Savegame | Check-out applies |
| --- | --- |
| **Current** | The profile's **head** revision |
| **Past** | The revision recorded on its head version |
| **No profile** | Nothing. No profile is applied |

A current savegame follows its profile — that is what current means — so it gets whatever the
profile says now. Preparing the mod list before a session and then checking the farm out is the
ordinary case, and it must not be undone by the check-out.

The head version's revision is **not** the right target for a current savegame: it names the last
list the farm was *played* on, which is older than head whenever the profile has been edited since.

A past savegame gets the `(ProfileId, ProfileRevision)` pair from its head version, never the
number alone. With no operation that moves a savegame between profiles, that `ProfileId` always
equals `Savegame.ProfileId`.

`ModSyncService.GetDesiredAsync` currently passes `null` as the revision, which always resolves
to head. The revision becomes a parameter;
`GET repos/{repoId}/profiles/{profileId}/modDependencies?revision=` already serves any revision.

### Two actions, not one

A savegame carries two separate actions: **Apply profile** and **Check out**. Check out is
disabled until the instance is on the revision the table above names, and says which apply would
enable it.

Where the instance is already there — the ordinary case for a current savegame on an instance
that follows its profile — Check out is enabled on arrival and the flow is one click. The second
click appears only when the mod folder is genuinely wrong.

`SavegameService.CheckOutAsync` is unchanged: mods stay outside it, and no sync is folded into a
claim. Apply keeps its own dialog, so a plan that would quarantine files the repo does not know
about is still shown before anything is written.

### Applying to an instance that holds a savegame

| Instance holds | Apply |
| --- | --- |
| Nothing | Unchanged |
| The **current** savegame of the profile being applied | Allowed. This is how a farm follows its profile |
| A **past** savegame | Allowed only for that savegame's own revision — re-applying it, repairing folder drift. Any other revision is refused |
| Any savegame, and the apply names a **different** profile | Refused. This is the active-profile switch below |

Switching an instance's active profile is refused while a savegame is checked out.

An instance's mod folder never changes. Keeping the folder fixed across a settings change is the
adapter's responsibility, so `SyncManifest.ModFolder` is checked defensively rather than as a
state the design expects.

### Holding a past savegame is stored state

That an instance holds a past savegame is recorded on the instance, not inferred from revision
numbers. Two things read it: the apply table above, and the drift check.

`InstanceDriftService.Check` already takes the revision and the dependencies to compare against
as parameters (`currentRevision`, `profileDependencies`), and its callers pass the profile's head.
For an instance holding a past savegame they pass **the revision that savegame targets** instead.

Nothing in the drift check is suppressed. `profileHasMoved` compares the applied revision against
the targeted one and finds them equal; `CompareProfile` diffs the manifest against that revision's
dependencies. Folder drift — added, removed or changed files, a locked mod the game replaced — is
found and reported exactly as it is for a current savegame, and its re-apply targets the revision
the savegame needs rather than head.

Without this the instance would be behind head by construction and would report drift
permanently, offering a re-apply to head that the apply table refuses.

The instance still says which revision it is holding and for which savegame, so being behind head
is visible without being reported as a problem.

## Play attribution

The revision a savegame was played on is determined by observation, not by timestamps.

The folder's revision is `SyncManifest.ProfileRevision`. It changes only when this machine
applies a profile. The profile's head revision is irrelevant to it — other users may move the
head any number of times with no local effect.

The manifest is per instance. `ActiveProfile` and the manifest are still two different facts —
the active profile is intent and is recorded even when the sync failed
([InstancePageViewModel.cs:201](../ModsDude.Client/ModsDude.Client.Wpf/ViewModel/Pages/InstancePageViewModel.cs):
*"The intent is recorded even where the folder could not be touched"*), while the manifest is
what a sync actually installed. They diverge on a failed or partial apply.

Refusing to switch profile while a savegame is held keeps them from diverging for the life of a
binding, and Check out being disabled until the profile is applied means a binding is only ever
taken when a matching manifest already exists. `Observe()` reads the manifest under those two
conditions and needs no further guard.

### Two hashes, two questions

The binding stores two hashes of the same slot. They answer different questions and have
different lifetimes.

| Field | Rewritten | Question it answers |
| --- | --- | --- |
| `ContentHash` | Never, after check-out | Do the slot's bytes still match the version the server holds? |
| `LastObservedHash` | At every observation | Have the slot's bytes moved since the last time we looked? |

`ContentHash` is what was downloaded into the slot at check-out. `SavegameDriftRules.Classify`
compares the slot against it to report `UncheckedInPlay` — play that exists on this disk and
nowhere else. It stays fixed at the check-out value for as long as the savegame is held, since
the version on the server is the thing being compared to.

`LastObservedHash` tracks a moving boundary instead. It is set to the slot's current bytes every
time `Observe()` runs, so a comparison against it means "since the last observation" rather than
"since check-out".

Collapsing the two into one field breaks the drift notice: after an apply refreshed the single
hash, the slot would compare equal to it, and `UncheckedInPlay` would stop reporting play that
had not been checked in.

In the second worked example below, the slot is H1 after the apply, `LastObservedHash` is H1 and
`ContentHash` is still H0. `Observe()` compares H1 to H1 and reports no new play. `Classify`
compares H1 to H0 and reports that the savegame has been played and not checked in. Both are
correct.

### One hash format

Every hash is produced and compared through `ModContentHasher`, in one format. Comparisons are
ordinary equality on that format.

`ModContentHasher.Matches` is currently case-insensitive so that two parts of the client cannot
disagree over hex casing. Two parts of one application disagreeing on the spelling of a hash is a
bug in whichever one writes the odd spelling, not a case to absorb at every comparison site —
`Matches` hides it, and `Observe()` relying on the same leniency would spread it. The hasher is
the single place a hash string is minted, and the tolerance comes out of the comparison.

### State

Two fields are added to `SavegameCheckoutBinding`:

| Field | Set at check-out | Meaning |
| --- | --- | --- |
| `LastObservedHash` | `= ContentHash` | The slot's bytes when last examined |
| `LastPlayedRevision` | `= null` | Newest revision play has been confirmed on. Null until play is observed |

`ProfileId` and `ProfileRevision` on that record stay nullable, and are set together or not at
all — a binding for a savegame with no profile has neither. The pairing is the same constraint the
server rows carry.

For such a savegame `Observe()` records nothing and check-in sends no revision. The
`LastPlayedRevision ?? AppliedRevision` fallback below applies only where the savegame has a
profile.

### The procedure

```
Observe():
    current = hash(slot)
    if current != LastObservedHash:
        LastPlayedRevision = <folder's revision, from the manifest>
        LastObservedHash   = current
```

Two call sites:

| Site | Order |
| --- | --- |
| Apply | `Observe()` runs **before** sync rewrites the manifest, so it reads the outgoing revision |
| Check-in | `Observe()` runs first; the version is then sent with `LastPlayedRevision ?? AppliedRevision` |

`SavegameService.ResolveAppliedRevision` currently prefers the manifest and falls back to the
binding. That order inverts: the binding's `LastPlayedRevision` is preferred, the manifest is the
fallback.

### Worked examples

Checked out at revision 4. Other users move the profile head to 1004. `Observe()` marked in bold.

| Event | Head | Folder | Slot | `LastObservedHash` | `LastPlayedRevision` |
| --- | --- | --- | --- | --- | --- |
| Check out | 4 | 4 | H0 | H0 | – |
| Others edit ×1000 | 1004 | 4 | H0 | H0 | – |
| Played | 1004 | 4 | H1 | H0 | – |
| **Apply** → 1004 | 1004 | 1004 | H1 | H1 | 4 |
| Played | 1004 | 1004 | H2 | H1 | 4 |
| **Check in** | 1004 | 1004 | H2 | H2 | 1004 |

Version records revision **1004**.

Same start, but the savegame is not played again after the apply:

| Event | Head | Folder | Slot | `LastObservedHash` | `LastPlayedRevision` |
| --- | --- | --- | --- | --- | --- |
| Check out | 4 | 4 | H0 | H0 | – |
| Played | 4 | 4 | H1 | H0 | – |
| **Apply** → 1005 | 1005 | 1005 | H1 | H1 | 4 |
| (a week) | 1005 | 1005 | H1 | H1 | 4 |
| **Check in** | 1005 | 1005 | H1 | H1 | 4 |

Version records revision **4**. The interval between check-out and check-in does not enter into
it.

### Never played

`LastPlayedRevision` stays null and check-in falls back to the folder's current revision. The
slot's bytes equal the head version's, so `CheckInSavegameV1Endpoint` mints no version and
answers with the existing head.

## Publishing

No savegame may be checked out on the instance when one is published.
`PublishSavegameV1Endpoint` opens a claim on the new savegame in the same transaction as the
savegame and its first version, so a publish always leaves exactly one savegame held.

The publish dialog offers every profile in the repo, and **no profile** as an explicit choice.
`PublishSavegameRequest.ProfileId` becomes nullable. The profile need not be the instance's active
one.

**A first version's revision is declared, not observed**, and this is the only version in the
system of which that is true. The bytes predate ModsDude: there is no binding, no
`LastObservedHash` and no prior state, so nothing knows which mods were in the folder while that
farm was actually played. Requiring the target profile to be applied first would not change that —
it would observe the folder at the moment of publishing, which is not the same fact — so it is not
required.

The recorded revision is the applied revision where the chosen profile is the one the folder is on,
and that profile's head otherwise. The dialog shows the number it is going to record, so the
declaration is on screen rather than implied.

Every version after the first is observed, through `Observe()`.

Nothing checks that the farm can actually run on the profile it is published to, and nothing can.
The dialog says so.

A savegame published with no profile records no revision, and `ProfileRevision` goes with
`ProfileId` under the pairing constraint.

Publishing to a profile that already has a current savegame supersedes it, which is stated in the
same dialog.

## Slots ModsDude did not write

A slot occupied by a savegame this machine never checked out is `SavegameSlotAvailability.Unrecognised`.
Writing to one is a confirmation naming the save, and the folder goes to the Recycle Bin rather
than being deleted.

Such savegames do not count towards the one-checkout-per-instance limit, and they do not affect
which profile may be active or whether a profile may be applied.

## Interface

### Tone

`SavegameChipTone` reserves `Caution` for *"something that can damage a save"*, and says that
*"spending it on ordinary staleness is what teaches people to ignore it"*
([SavegameChip.cs:16](../ModsDude.Client/ModsDude.Client.Wpf/ViewModel/ViewModels/SavegameChip.cs)).

**Past is `Neutral`, always.** It is a fact about which farm a profile is following, not a problem
with either.

### Savegames list

Current is the unmarked default. Only the exception carries a chip.

| State | Chip | Tone |
| --- | --- | --- |
| Past | `Past · Old-school rev 4` | Neutral |
| No profile | `No mod list` | Neutral |
| Held by you | existing | Accent |
| Unchecked-in play | existing | Caution |

A **Show past farms** toggle, off by default. Past savegames stay findable without filling the
list, and the toggle keeps this the one repo-level list rather than reintroducing a per-profile
one.

### Row actions

Two buttons, `Apply profile` and `Check out`, with the disabled reason carrying the explanation:

| Situation | Check out reads |
| --- | --- |
| Past savegame, folder elsewhere | *Apply Old-school rev 4 first* |
| Current savegame, instance on another profile | *Apply Old-school first* |
| Another savegame held here | *Riverbend is checked out on this instance* |
| No instance for this game | *No instance for this game* |

### Instance page

The profile dropdown is **disabled** while any savegame is checked out.

The apply button's meaning changes while a **past** savegame is held. It normally applies the
profile's latest; that is refused here, so it reads `Re-apply rev 4` and its only job is repairing
folder drift back to that revision. Underneath: *"Check Riverbend 2023 in to move this instance
forward."*

A status line while a past savegame is held, Neutral: *"Holding Old-school rev 4 for **Riverbend
2023**."*

### Drift notice

Two rules, both about not crying wolf:

- Never "behind the profile" for an instance holding a past savegame. Nothing is suppressed to
  achieve this — the targeted revision is passed into `InstanceDriftService.Check` instead of head,
  so the comparison simply comes out equal.
- Folder drift still reports, and its action reads `Re-apply rev 4`, never "apply latest".

### Check-out dialog

One added line naming the revision the folder will be on: *"Will run on Old-school rev 1004."*

Worth showing even for a current savegame, since that number can differ from the one the farm was
last played on whenever the profile has moved since.

For a past savegame: *"This farm stays on rev 4. Playing it does not move it forward."*

### Check-in dialog

*"Played on Old-school rev 1004."* Read from `LastPlayedRevision`. This is where the attribution
becomes visible, at the moment it is recorded and while a wrong one can still be noticed.

### Publish dialog

- Profile picker: every profile in the repo, plus an explicit **No mod list**.
- The revision that will be recorded, shown as a number, since it is a declaration.
- Superseding, stated inline rather than as a second dialog: *"**Season 4** is Old-school's current
  farm. Publishing this makes it past — it stays playable and stays on rev 1004."*
- Where the chosen profile is not the one the folder is on: *"This folder is on **Vanilla**.
  Nothing checks that this farm can run on Old-school."*

### Profile page

Its current savegame by name, with whoever holds it. Past ones as a count linking to the savegames
list with the toggle on.

The archived-current case stated with its three ways out: *"**Season 4** is this profile's current
farm and is archived. Un-archive it, delete it, or publish a new farm."*

## Consequences elsewhere

`SavegameVersion`'s foreign key onto `ProfileRevision` is `Restrict`, and
`PruneProfileRevisionsV1Endpoint` already refuses to delete a revision a savegame version holds.
A past savegame's revision therefore stays reproducible with no further guarantee.

`SavegameDriftKind.PlayedOnAnotherModList` is retained, and its rule changes what it compares
against.

`SavegameDriftRules.HasMovedOffItsModList` currently compares the binding's revision — the one
check-out applied — against the applied one. That fires on the ordinary follow-the-profile flow:
the farm is checked out at rev 1000, the profile is applied at rev 1004, and the two numbers
differ because the farm is following its profile exactly as intended.

It compares against the savegame's **target** instead:

| Condition | Reported |
| --- | --- |
| Applied profile differs from the savegame's | Yes. Two profiles' revision numbers are not comparable |
| **Past** savegame, applied revision differs from its pinned revision | Yes. The apply table forbids moving it, so a mismatch is an interrupted sync or discarded local state |
| **Current** savegame, applied revision differs from the profile's head | No — not here. That is `profileHasMoved` at the instance level, which already reports it |
| Savegame with no profile | No. There is no mod list it claims to match |

The binding's `ProfileRevision` is then read for play attribution only, and by nothing that
decides drift.

### A savegame never targets a revision older than it was played on

| Transition | Target becomes | Last played | Holds |
| --- | --- | --- | --- |
| Current, profile advances | head | ≤ head | ✓ |
| Current superseded | its head version's revision | that same revision | ✓ equal |
| Past, played and checked in | unchanged | unchanged | ✓ |
| Past made current | head | ≤ head | ✓ |

Superseding lowers the target — from head down to the revision the farm was actually played on —
and that is correct rather than a violation: the farm was never played on the revisions it was
following as current.

The invariant is about revision *numbers*. `RestoreProfileRevisionV1Endpoint` mints a new revision
copying an older one, so restoring rev 4 as rev 1005 keeps the number climbing while the mod list
goes backwards. That is the same hazard as any breaking edit to a profile with a current
savegame, which this design accepts; it is not closed here.

## Limits

Mods changed in the folder outside ModsDude are play on a mod list with no revision number. The
cheap drift check detects that the folder moved; it cannot attribute the play to a revision.

`Observe()` hashes the savegame slot, adding one pass per apply.

Checking out a past savegame costs a full apply back to its revision, and returning to current
play costs another. Both are ordinary syncs against the content store.

A published savegame's first version carries a declared revision rather than an observed one. The
bytes existed before ModsDude saw them, and no arrangement of the publish flow can recover which
mods were in the folder while that farm was played.
