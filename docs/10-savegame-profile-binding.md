# 10 — Savegames and profile revisions

*Built.* Schema, publish, the swap and the rename are in the tree
([Phase 9 slice 1](PLAN.md#1-server-schema-and-api)); the two hashes, the observation and the
revision a sync installs are ([slice 2](PLAN.md#2-play-attribution-on-the-client)); so are the
check-out targets, the apply table, the drift rules and the one-mod-list-per-game limit
([slice 3](PLAN.md#3-the-rules)); and so is the [interface](#interface) — the chips, the two row
actions, the wording of the notices and the three dialogs ([slice 4](PLAN.md#4-interface)). A refusal
now reaches the user as a button that was never offered, with the engine's own sentence behind it as
the backstop it always was.

[Phase 10](PLAN.md#phase-10--one-game-many-targets) then moved the policy up from the folder to the
game. What that changed here: the hold limit counts per **game** rather than per folder, a slot is
addressed by the folder it is in as well as its own id, publishing is reached from the repo's saves
list, and the check-out dialog has no step asking which installation.

The application is in early development. Nothing here migrates existing local or server state,
and no shape below is constrained by what an older client wrote.

## Two successions, not one

Two different things advance over time, and this document is about how they relate.

**Snapshots of one savegame.** A `Savegame` has a linear history of `SavegameSnapshot` rows,
numbered from 1. A check-in mints one. They are snapshots of the same savegame at different points,
and its head is the newest. Each snapshot records the profile revision it was played on.

**Savegames on one profile.** A `Profile` has a succession of `Savegame` rows. These are
separate savegames — separate names, separate claims, separate snapshot histories numbered from 1
each. Starting a second savegame on "Old-school" creates a second `Savegame`; it does not add a
snapshot to the first.

| | Snapshots | Savegames on a profile |
| --- | --- | --- |
| Belong to | One savegame | One profile |
| Created by | Check-in, publish, restore | Publish |
| Newest is called | The **head** snapshot | The **current** savegame |
| Older ones are called | Earlier snapshots | **Past** savegames |
| Numbered | Yes, from 1 per savegame | No |

## Cardinality

A profile has **at most one current savegame**, enforced. Any other savegame on it is past.

Only *current* is one-to-one. Past savegames keep pointing at the profile — their snapshots name
its revisions, and `SavegameSnapshot`'s foreign key onto `ProfileRevision` is `Restrict`. A
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
when the group starts a new savegame on it; the previous savegame becomes past.

A game holds **at most one checked-out savegame that has a profile**, counted across every folder it
reaches rather than per folder.

The limit is not about savegames but about the mod list, which the whole game is on: every folder
follows the game's one active profile, so two folders cannot be on two revisions and there is
nothing per folder to count. It is also the answer to the other question — hosting one save on
the dedicated server while playing another in singleplayer would hold two of the group's saves and
block two people.

A savegame with no profile makes no claim on the mod list and cannot conflict with anything, so any
number of those may be held alongside — bounded only by the slots the adapter offers, and by
`SavegameBindingStore`'s existing one-binding-per-slot rule.

Stating it this way needs no adapter-capability check. In a repo whose adapter has no mod support
no savegame has a profile, so nothing is ever limited.

Enforced by `CheckOutAsync` and by `PublishAsync`, before either takes a claim. It had been assumed
rather than checked until then: nothing but the slot safety check stood between two savegames and one mod
folder, and two savegames in two slots never touched it.

A savegame's profile is fixed at publish. **There is no operation that moves a savegame to a
different profile** — `UpdateSavegameV1Endpoint` becomes a rename. Moving one would make
`Savegame.ProfileId` and every snapshot's `ProfileId` disagree, and revision numbers of two
profiles are not comparable
([SavegameDrift.cs:168](../ModsDude.Client/ModsDude.Client.Core/Savegames/SavegameDrift.cs)).

A user who wants the effect republishes the savegame, which is three existing operations:

1. Check it out, so the play is in a slot.
2. `DiscardAsync` — hands the claim back and clears the binding, leaving the slot as an ordinary
   unrecognised one. `Forget` alone is not enough: it is local, and the claim would stay open on a
   savegame nobody is holding.
3. Publish that slot to the target profile, as a new savegame with its own history.

The original savegame stays where it is, on its old profile, with its history intact. Nothing is
moved and nothing is rewritten.

The same three steps are the only route from a savegame with no profile to one that has a
profile, since nothing connects an existing savegame to one.

Running two savegames in parallel on one mod list is done by branching the profile —
`POST repos/{repoId}/profiles` with `CopyFrom`, which exists today
([CreateProfileV1Endpoint.cs](../ModsDude.Server/ModsDude.Server.Api/Endpoints/Profiles/CreateProfileV1Endpoint.cs)).

## Savegames without a profile

`Savegame.ProfileId` is optional, and so are `SavegameSnapshot.ProfileId` and
`SavegameSnapshot.ProfileRevision`.

The connection is optional in every repo, not only where the adapter lacks mod support. Adapters
with savegame support and no mod support are planned and have no profile to offer; a savegame in
a mod-capable repo may equally be published without one, and publish offers that as a choice.

A savegame with no profile has no current or past state, records no revision, and takes no part
in anything below. Check-out writes the slot and takes the claim; no profile is applied, no apply
is refused on its behalf, and nothing reports it as drifted from a mod list. It is unmanaged, by
the publisher's choice.

A null revision is a valid state meaning "this snapshot is not connected to a profile". The
invalid state is a half-set pair, and it is a database check constraint: `ProfileId` and
`ProfileRevision` are both null or both set on `SavegameSnapshot`, which is the only row carrying
both. `Savegame` pins no revision — that is the whole of the two-successions argument above — so
what it carries instead is the other half of the same idea: `SupersededAt` requires a `ProfileId`,
since a savegame following no mod list is in no succession and is neither current nor past.

A savegame cannot acquire or lose a profile, since nothing moves one between profiles. A history
mixing snapshots that name a revision with snapshots that do not therefore cannot arise.

Nothing is left for the client to enforce about whether a profile is present. The pair travels as one
value — `SavegamePublishTarget`, or null — so a caller cannot set half of it, and
`SavegameService.DeclaredRevisionFor` answers the other half for whichever profile the dialog was
given. A folder that has never been synced is not an obstacle: the first snapshot's revision is
declared, so the answer there is the chosen profile's head.

`SavegameService.ResolveAppliedRevision` still throws at *check-in* where a profile was chosen and no
revision of it can be found anywhere — a snapshot that names a profile has to name a revision of it
too, and that one is observed rather than declared.

## Profiles with no savegame

A profile with no savegame is the ordinary starting state, not a special one. Every profile
begins here, and a profile used only as a mod list — a template to branch from, or a repo whose
group never publishes a save — stays here.

| | Behaviour |
| --- | --- |
| Editing the profile | Unrestricted. Every save mints a revision as it does today |
| Applying it to a game | Applies head. No savegame to attribute play to, and no past-savegame refusal |
| Checking out | Nothing to check out |
| Publishing to it | Creates the profile's first savegame, which becomes current. Nothing becomes past, and there is no confirmation to show |

A profile returns to this state when its current savegame is deleted. Past savegames stay past
and are not promoted, and the next publish creates a new current savegame.

## Repos that do not support savegames

`IBaseGameAdapter.CanSupportSavegames` is a client-side capability read from the adapter's base
settings. Where it is false, savegames do not exist in the repo: `SavegameService` returns no
adapter, and the repo's Saves entry is not offered (`RepoPageViewModel`).

Nothing in this document applies to such a repo.

| | Behaviour |
| --- | --- |
| Current and past | Do not exist. No savegame does |
| Applying a profile | Always applies head. Never refused on savegame grounds |
| One checkout per game | Vacuous. The limit counts savegames with a profile, and none has one. Any number may be held at once |
| `Observe()`, `LastObservedHash`, `LastPlayedRevision` | Never run and never written. They live on the checkout binding, and no binding is ever taken |
| `SyncManifest.ProfileRevision` | Still recorded, as it is today. It describes the folder, not a savegame |

The server is unchanged either way. It has no notion of `CanSupportSavegames`, and a repo whose
adapter does not support savegames is one where no client ever creates any.

## Current and past savegames

A past savegame is **not read-only**. It can be checked out, played, and checked in, and doing
so mints snapshots as normal. The single restriction is that **its profile revision does not
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
| **Past** | The revision recorded on its head snapshot |
| **No profile** | Nothing. No profile is applied |

A current savegame follows its profile — that is what current means — so it gets whatever the
profile says now. Preparing the mod list before a session and then checking the savegame out is the
ordinary case, and it must not be undone by the check-out.

The head snapshot's revision is **not** the right target for a current savegame: it names the last
list the savegame was *played* on, which is older than head whenever the profile has been edited since.

A past savegame gets the `(ProfileId, ProfileRevision)` pair from its head snapshot, never the
number alone. With no operation that moves a savegame between profiles, that `ProfileId` always
equals `Savegame.ProfileId`.

`ModSyncService.GetDesiredAsync` used to pass `null` as the revision, which always resolved to head;
`GET repos/{repoId}/profiles/{profileId}/modDependencies?revision=` had served any revision all along.

The table is enforced by resolution rather than by every caller reading it. `ModSyncRequest.Revision`
null means "whatever this game must be on", and `PlanAsync` resolves it from what the game is
holding — so the drift notice's re-apply, the mod list editor's save and the profile page's activation all
target a past savegame's revision without any of them knowing what a savegame is. The one caller that
names a number is the check-out dialog, previewing the apply for a savegame nothing is holding yet.

### Activating before the claim

There is no separate **Apply profile** button on a savegame. Where the mod folder is not on the
revision the table above names, **Check out** asks to activate the profile first — *Activate it,
then check out* or *Cancel* — and runs that activation before the slot dialog opens. **Take a copy**
asks the same question with a third answer, *Leave the mods as they are*: a copy claims nothing, so
writing it next to whatever the folder has now is the user's to choose.

Where the folder is already there — the ordinary case for a current savegame on a game that follows
its profile — neither asks, and the flow is one click.

**Mods stay outside `SavegameService.CheckOutAsync`**, and no sync is folded into a claim. The
activation is the ordinary one with its own disclosure, so a plan that would quarantine files the
repo does not know about is still shown before anything is written. What check-out gained is bookkeeping and a refusal, not a sync: it records
`TargetRevision` from the savegame's current-or-past state, and it refuses a second savegame that
claims the same mod folder.

### Applying to a game that holds a savegame

Only savegames **with a profile** appear here. One holding nothing but profile-less savegames is
the "Nothing" row: they claim no mod list, so nothing about the mod folder is theirs to constrain.

| The game holds | Apply |
| --- | --- |
| Nothing, or only savegames with no profile | Unchanged |
| The **current** savegame of the profile being applied | Allowed. This is how a savegame follows its profile |
| A **past** savegame | Allowed only for that savegame's own revision — re-applying it, repairing folder drift. Any other revision is refused |
| A savegame with a profile, and the apply names a **different** profile | Refused. This is the active-profile switch below |

Switching a game's active profile is refused while a savegame with a profile is checked out.

A target's mod folder never changes under it. Keeping the folder fixed across a settings change is the
adapter's responsibility, so `SyncManifest.ModFolder` is checked defensively rather than as a
state the design expects.

### Holding a past savegame is stored state

That a game holds a past savegame is recorded on the binding, not inferred from revision
numbers: it is `SavegameCheckoutBinding.TargetRevision`, written at check-out from the savegame's own
current-or-past state and read back off local state afterwards. Inferring it would need the server's
answer to "is this still its profile's current savegame?", and the two things that read it — the apply
table above and the drift check — both have to work offline and cost a directory listing.

**Decided once, and it does not move under the holder.** Somebody else can publish to this profile
while the savegame is checked out here, which supersedes it; the binding goes on saying what it said at
check-out, and that is the answer this design wants. The two things that change which savegame is
current are both stated before they run, and somebody else's publish is not stated to a holder — so
the alternative is the apply button and the drift notice quietly changing meaning because of an action
the person looking at them did not take. Nothing breaks either way: the savegame goes on following its
profile until it is checked in, the snapshot that check-in mints records the revision it was genuinely
played on, and its target moves forward to that revision with it, so the invariant below still holds.
The next check-out reads the truth.

`DriftService.Check` already took the revision and the dependencies to compare against as
parameters (`currentRevision`, `profileDependencies`), and its callers passed the profile's head. For
a game holding a past savegame they pass **the revision that savegame targets** instead.

Nothing in the drift check is suppressed. `profileHasMoved` compares the applied revision against
the targeted one and finds them equal; `CompareProfile` diffs the manifest against that revision's
dependencies. Folder drift — added, removed or changed files, a locked mod the game replaced — is
found and reported exactly as it is for a current savegame, and its re-apply targets the revision
the savegame needs rather than head.

Without this the folder would be behind head by construction and would report drift
permanently, offering a re-apply to head that the apply table refuses.

The game still says which revision it is holding and for which savegame, so being behind head
is visible without being reported as a problem.

## Play attribution

The revision a savegame was played on is determined by observation, not by timestamps.

The folder's revision is `SyncManifest.ProfileRevision`. It changes only when this machine
applies a profile. The profile's head revision is irrelevant to it — other users may move the
head any number of times with no local effect.

**The manifest is per folder, and the active profile is per game.** That is the whole of the two
verbs: activating is intent and is recorded before a single file moves — so it stands even where a
folder could not be touched — while a manifest is what a sync actually installed in one folder,
written only on success. They diverge on a failed or partial apply, and that divergence has a name:
`DriftStatus.NotApplied`, an intent recorded with the work not done. See
[07](07-mod-sync-design.md#it-has-to-be-unmissable-everywhere).

An apply attributes play using **the manifest of the folder it is about to rewrite**, which is why
this stayed per folder when the hold limit went per game: a save in the MP client's savegame folder
was played against the MP client's mods, whatever the dedicated server happens to be on.

Refusing to switch profile while a savegame is held keeps them from diverging for the life of a
binding, and Check out being disabled until the profile is applied means a binding is only ever
taken when a matching manifest already exists. `Observe()` reads the manifest under those two
conditions and needs no further guard about *whether* the folder has been synced.

It does check *which list* the folder holds. A manifest naming another profile carries a number this
savegame cannot record — revision 6 of two lists is one integer and two mod lists, and the server
refuses a revision that is not the savegame's profile's. That reading is the same one
`ResolveAppliedRevision` has always applied to the manifest, and `LastPlayedRevision` must not become
a way around it. The play is still observed; only the number is withheld, and the check-in falls back
to the list the save was handed over on.

### Two hashes, two questions

The binding stores two hashes of the same slot. They answer different questions and have
different lifetimes.

| Field | Rewritten | Question it answers |
| --- | --- | --- |
| `ContentHash` | Never, after check-out | Do the slot's bytes still match the snapshot the server holds? |
| `LastObservedHash` | At every observation | Have the slot's bytes moved since the last time we looked? |

`ContentHash` is what was downloaded into the slot at check-out. `SavegameDriftRules.Classify`
compares the slot against it to report `UncheckedInPlay` — play that exists on this disk and
nowhere else. It stays fixed at the check-out value for as long as the savegame is held, since
the snapshot on the server is the thing being compared to.

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

`ModContentHasher.Matches` was case-insensitive so that two parts of the client could not disagree
over hex casing. Two parts of one application disagreeing on the spelling of a hash is a bug in
whichever one writes the odd spelling, not a case to absorb at every comparison site — `Matches` hid
it, and `Observe()` relying on the same leniency would have spread it. The hasher is the single place
a hash string is minted, so the tolerance came out of the comparison instead. The slot safety check
and the drift rules got stricter with it; for the safety check that means erring towards refusing a
write, which is the direction it errs in everywhere else.

### State

Two fields on `SavegameCheckoutBinding`:

| Field | Set at check-out | Meaning |
| --- | --- | --- |
| `LastObservedHash` | `= ContentHash` | The slot's bytes when last examined |
| `LastPlayedRevision` | `= null` | Newest revision play has been confirmed on. Null until play is observed |

Publishing and checking in while carrying on playing leave the same pair, for the same reason: the
snapshot on the server is these bytes, so the next evening is the first that has not been recorded
anywhere. `LastObservedHash` unset reads as `ContentHash`, which is what makes that hold for any
route into a binding rather than only the three that exist.

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
| Apply | `Observe()` runs **inside** the manifest write, before it, so it reads the outgoing revision |
| Check-in | `Observe()` runs first; the snapshot is then sent with `LastPlayedRevision ?? AppliedRevision` |

Inside the write rather than beside it, so that no path can move the revision without attributing the
play first — including the one that installs nothing, since a revision can move without a single mod
doing so. Sync reaches it through `ISavegamePlayObserver`, one method, which is the whole of what the
sync engine knows about savegames.

`SavegameService.ResolveAppliedRevision` used to prefer the manifest and fall back to the binding.
That order is inverted: the binding's `LastPlayedRevision` is preferred, the manifest is the fallback,
and the binding's check-out revision is the last resort.

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

Snapshot records revision **1004**.

Same start, but the savegame is not played again after the apply:

| Event | Head | Folder | Slot | `LastObservedHash` | `LastPlayedRevision` |
| --- | --- | --- | --- | --- | --- |
| Check out | 4 | 4 | H0 | H0 | – |
| Played | 4 | 4 | H1 | H0 | – |
| **Apply** → 1005 | 1005 | 1005 | H1 | H1 | 4 |
| (a week) | 1005 | 1005 | H1 | H1 | 4 |
| **Check in** | 1005 | 1005 | H1 | H1 | 4 |

Snapshot records revision **4**. The interval between check-out and check-in does not enter into
it.

### Never played

`LastPlayedRevision` stays null and check-in falls back to the folder's current revision. The
slot's bytes equal the head snapshot's, so `CheckInSavegameV1Endpoint` mints no snapshot and
answers with the existing head.

## Publishing

`PublishSavegameV1Endpoint` opens a claim on the new savegame in the same transaction as the
savegame and its first snapshot, so a publish always leaves the new savegame held.

Publishing **to a profile** therefore requires that no savegame with a profile is already checked
out on the game — the same limit as check-out, reached from the other side, rather than a rule
of its own. Publishing without a profile has no such precondition.

The publish dialog offers every profile in the repo, and **no profile** as an explicit choice.
`PublishSavegameRequest.ProfileId` becomes nullable. The profile need not be the game's active
one.

### Whether the publish leaves you holding it

**Publishing to the profile the game is on** offers the choice check-in offers, ticked by default:
keep the save and the claim, or hand both straight back. Publishing with **no profile** offers the
same, since such a savegame claims no mod folder.

**Publishing to any other profile takes the choice away** and hands the save back — the snapshot is
minted, the claim is released and the local copy goes to the Recycle Bin, which is exactly
`DiscardAsync`, called by `PublishAsync` once the publish has committed. The dialog says so before
the button is pressed and the button says it too.

The reason is that the alternative is unreachable ground. A savegame following profile B, checked
out into a folder on profile A, is `PlayedOnAnotherModList` — the state that damages saves
— and it is reached in one gesture by somebody who did nothing wrong. Worse, **no apply clears it**:
the [apply table](#applying-to-a-game-that-holds-a-savegame) refuses B because the folder is on A's
list under a held savegame, and refuses A because the held savegame follows B. The only ways out
were to check the savegame in or to give it back, so the publish gives it back at the moment the
choice is legible rather than leaving somebody to discover it from a drift notice whose one action
is refused.

Nothing is lost by it: the savegame is in the repo, the claim is free, and checking it out again
after applying its profile is the ordinary flow.

**A first snapshot's revision is declared, not observed**, and this is the only snapshot in the
system of which that is true. The bytes predate ModsDude: there is no binding, no
`LastObservedHash` and no prior state, so nothing knows which mods were in the folder while that
savegame was actually played. Requiring the target profile to be applied first would not change that —
it would observe the folder at the moment of publishing, which is not the same fact — so it is not
required.

The recorded revision is the applied revision where the chosen profile is the one the folder is on,
and that profile's head otherwise. The dialog shows the number it is going to record, so the
declaration is on screen rather than implied.

Every snapshot after the first is observed, through `Observe()`.

Nothing checks that the savegame can actually run on the profile it is published to, and nothing can.
The dialog says so.

A savegame published with no profile records no revision, and `ProfileRevision` goes with
`ProfileId` under the pairing constraint.

Publishing to a profile that already has a current savegame supersedes it, which is stated in the
same dialog.

## Which folder a slot is in

A slot is addressed by `SavegameSlotRef(TargetKey, SavegameSlotId)` — which of the game's savegame
folders, and which slot in it. That is what a binding and a slot hint record, and it renders as
`{target}:{slot}` when persisted, exactly as a game identity renders as `_farming_simulator#fs25`.

**Uniqueness across the game is a construction rather than a contract.** An adapter mints slot ids
unique within the folder it was asked about, which it cannot get wrong, and the engine pairs each
answer with the key it asked for. Requiring global uniqueness instead would put two folders' slots
on one binding the first time somebody numbered from one twice, and the one-binding-per-slot rule
would enforce the collision rather than catch it.

The pairing is what makes attribution honest: a save in target T's savegame folder was played
against target T's mod folder, so an apply to one folder attributes only the play that happened in
it. See [04 — Game adapters](04-game-adapters.md#targets).

### A hold survives a folder the settings no longer name

Emptying a folder field in the local settings takes a target away, and an adapter author renaming a
key does exactly the same thing from here — the two are indistinguishable, so nothing tries to tell
them apart. A stale sync manifest is dropped for it: losing one costs a rescan.

**A binding is not.** It is a savegame on this disk and a claim somebody else is waiting on, so it
outlives the folder it can no longer address. Its row in the repo's Saves list says so in caution
colour — *"Your copy is in 'MP client', a folder this game's settings no longer name"* — and offers
**Stop tracking** and nothing else: check-in and discard both need a folder to pack or recycle, and
the engine refuses them before the server is told anything. Putting the setting back is the other
way out.

**A binding whose savegame the repo has deleted is the opposite case, and is dropped unasked.**
There is no claim left to hand back, no history to check a snapshot into, and every server-side verb
on it answers 404 — so the only thing anybody can do about it is stop tracking it, and a dialog
offering a choice with one sane answer exists to be clicked through. `RepoSavegamesPageViewModel`
forgets those holds when it loads, and only when **both** the live and archived lists came back:
a failed round trip must never be read as a deletion. Nothing on disk is touched, which is what
makes it safe to do without asking — the save stays where it is and becomes an ordinary save of the
user's own, which is exactly what the button did.

## Slots ModsDude did not write

A slot occupied by a savegame this machine never checked out is `SavegameSlotAvailability.Unrecognised`.
Writing to one is a confirmation naming the save, and the folder goes to the Recycle Bin rather
than being deleted.

Such savegames are not held by ModsDude at all, so they do not count towards the limit, and they
do not affect which profile may be active or whether a profile may be applied. They are, however,
exactly what a publish is made of — see the [publish dialog](#publish-dialog).

## Interface

### Tone

`SavegameChipTone` reserves `Caution` for *"something that can damage a save"*, and says that
*"spending it on ordinary staleness is what teaches people to ignore it"*
([SavegameChip.cs:16](../ModsDude.Client/ModsDude.Client.Wpf/ViewModel/ViewModels/SavegameChip.cs)).

**Past is `Neutral`, always.** It is a fact about which savegame a profile is following, not a problem
with either.

### Savegames list

The list is grouped by profile, in profile-name order, with savegames that follow no mod list last.
Inside a profile its current savegame comes first and the past ones hang under it, most recently
displaced first. A past row is also indented behind an elbow connector and dimmed - its buttons are
not, since a past savegame is still playable - so which is which reads before any chip is read.

| State | Chip | Tone |
| --- | --- | --- |
| Current | `Current` | Neutral |
| Past | `Past · Old-school rev 4` | Neutral |
| No profile | `No mod list` | Neutral |
| Held by you | existing | Accent |
| Unchecked-in play | existing | Caution |

A **Show past savegames** toggle, off by default. Past savegames stay findable without filling the
list, and the toggle keeps this the one repo-level list rather than reintroducing a per-profile
one.

### Row actions

`Check out` and `Take a copy`, with the disabled reason on Check out carrying the explanation:

| Situation | Check out reads |
| --- | --- |
| Another savegame **with a profile** held here | *'Riverbend' is checked out here* |
| The game is not connected on this machine | *This game is not connected here* |

**A folder on the wrong list does not disable anything - it asks.** Where the mod folder is not on
the revision the savegame runs on (a past savegame with the folder elsewhere, or a current one on a
game following another profile), both buttons stay enabled and their tooltips say which list would
be activated. The question - *Activate 'Old-school' rev 4 first?* - is described under
[Activating before the claim](#activating-before-the-claim). The activation names the revision it
installs, so a past savegame gets its own revision rather than head. One that does not finish
(declined, refused, unreachable) stops there, with its own toast.

**Take a copy is open to a Guest, activation included.** Activating writes nothing anybody else can
see, and a guest taking a copy wants the mod folder on the list that copy was played on. Check out
stays Member, and a guest's row carries no refusal - one beside a button they do not have would be
a line about nothing they can do.

**There is nothing to choose between.** A game is keyed by its identity and a repo is about one
game, so both buttons act on the one this repo offers, and the last row is the absence of it rather
than a fact about the savegame — which is why `SavegameRowRules` is never asked in that case and
the page says so itself. The ranking that used to pick between installations — the one that would
accept, else the one already following this profile, else the first — had nothing left to rank.

**The ways out of a hold are on the row too, both of them.** A row holding the local copy carries
`Check in` as its accent button and `Discard` immediately under it — the two answers to the same
question, and the choice between them is about what happened while you had the save rather than
about where you are standing. `Discard` used to be a row action on the game's own slot list, one
page and one sidebar away from the list somebody is looking at when they realise they took the
wrong save. Which of the game's savegame folders the copy is in is a line on the row, said only
where the game reaches more than one — the single-folder case has no folder *called* anything, and
a slot id is a name the player has never thought in.

A **past** row carries a third: `Make current`. It is the other end of the swap a publish performs,
so it is stated before it runs the same way — a confirmation naming the savegame it displaces, what
happens to it (past, still playable, and its mod list stops moving), and that this one stops being
pinned. Nothing about this machine gates it: which savegame a profile follows is a decision about the
repo. Where this machine <em>is</em> holding it, the swap clears the pin, because a savegame that is
current again follows its profile and a number left behind would hold that folder at revision 4
forever. That is not the hold moving under its holder — the argument against that is about
somebody else's publish, which nobody states to whoever is playing.

### Where activation is refused

**On the profile's page, which is the only place a profile is activated** — a bar across the top of
it, present on every sub-page. The game's own page used to carry a second copy of the control — a
profile dropdown, greyed out while a savegame with a profile was held — and two places to set one
thing is how they come to disagree. The refusal is
asked once, before the click: a control that offers the move and then reports a refusal is the
thing this section exists to remove.

Profile-less savegames leave it alone.

**Deactivating is refused by the same hold**, and by any savegame with a profile rather than only one
following another. Taking a game off its profile is the limit of the profile switch - it leaves the folder
on no list at all - and the savegame that claims one is exactly what that guard is for. See
[Deactivating](07-mod-sync-design.md#deactivating).

A held past savegame changes what the button means rather than whether it works. It normally
applies the profile's latest; that is refused here, so it reads `Re-apply rev 4` and its only job
is repairing folder drift back to that revision.

The repo's Overview keeps the sentence, Neutral, because what a game is holding is a fact about it
rather than a control on it: *"Holding a savegame that runs on 'Old-school' rev 4. Check it in from
Saves to move this game forward."* It names the mod list rather than the savegame: naming the save
cost a round trip per repo holding one, and the Saves list is both where the name is and where
anything can be done about it.

### Drift notice

Two rules, both about not crying wolf:

- Never "behind the profile" for a game holding a past savegame. Nothing is suppressed to
  achieve this — the targeted revision is passed into `DriftService.Check` instead of head,
  so the comparison simply comes out equal.
- Folder drift still reports, and its action reads `Re-apply rev 4`, never "apply latest". The
  button does not name a folder even where the game reaches several, because it applies the whole
  game — every folder, the ones already right included — and a button claiming to act on one of
  them would be lying about its scope. The **sentences** name the folder instead.

`SavegameDriftKind.PlayedOnAnotherModList` gets **two sentences for the one kind**, because the rule
reaches it two ways and only one of them is about numbers. A past savegame on the wrong revision names
its own target against the folder's; a savegame following a different profile entirely names no numbers
at all, since revision 6 of 'Season 4' and revision 6 of 'Vanilla' are different mod lists that share
an integer. Neither names `SavegameDrift.PlayedRevision`: that is what the save was checked out
against, it belongs to play attribution, and it is not what the comparison used.

### Taking a save from somebody

The claim is advisory, so taking a save somebody else has checked out is always allowed. Both people
are told:

- **Whoever takes it** is asked first, before the check-out dialog. The question names the holder
  and since when: *"Bob has 'Riverbend' checked out"*, with *Take it from Bob* and *Leave it with them*.
  It comes before the slot question because it decides whether there is a check-out at all. Take a
  copy and *Check out again* on your own claim do not ask. If the list was behind the server and the
  check-out's `TakenFrom` names somebody the question never asked about, a warning toast says whose
  it was.
- **Whoever had it** gets a Critical `SavegameDriftKind.TakenOver` card: *"'Riverbend' was taken over
  by Bob"*. Once somebody else has it there are already two copies of one save, whether or not
  either side has played, so the sentence is about which check-in wins: whoever checks in second has
  to force it, and that overwrites the other's play. A claim that was taken and then let go again
  (nobody holds it, head unchanged) is the same kind with nobody to name. It is never reported beside
  `TakenOverAndCheckedIn`, which is the same event one step further.

The drift check stays offline. It reads who holds the claim from `ISavegameSightings`, which the
Saves page fills whenever it reads a list, and which `SavegameClaimWatch` fills for every repo this
machine holds a save in. `SavegameClaimWatcher` runs that every three minutes (the first look 20 s
after the repos load), **whether or not the window is in sight**: the person who most needs to hear is
the one playing the save, with ModsDude behind the game, and the notice becomes a Windows toast
exactly then. With nothing held it asks nothing. A check-out records the claim it took into the same
cache straight away, so a list read before it cannot report somebody's own check-out as a takeover.

### Check-out dialog

Three sections — mods, slot, revision — each absent when it has nothing to say. **There is no
step asking where**: a machine has one installation of the game a repo is about, so the question
had a single answer.

The slot picker is one flat list across every savegame folder the game reaches, under a folder
heading only where there is more than one to tell apart. Which folder a save goes into is a fact
about the slot rather than a step of its own, and the grouping is `SlotGrouping.Apply`, shared with
the publish picker so the two cannot decide differently about the same slots.

One line naming the revision the folder will be on: *"Will run on Old-school rev 1004."*

Worth showing even for a current savegame, since that number can differ from the one the savegame was
last played on whenever the profile has moved since.

For a past savegame: *"This savegame stays on rev 4. Playing it does not move it forward."*

### Check-in dialog

*"Played on Old-school rev 1004."* Read from `LastPlayedRevision`. This is where the attribution
becomes visible, at the moment it is recorded and while a wrong one can still be noticed.

### Publish dialog

**Reached from the repo's Saves list**, not from a game page. Publishing is how a repo's first
savegame comes into existence, so it has to be reachable from the list that is empty and saying
so — and under one game per machine there is no sidebar of installations to go looking through
for it. It is still inherently about a slot, so the slot is asked for first, out of the same flat
list the check-out dialog shows, filtered to the ones ModsDude has no copy of: an empty slot has
nothing to publish, and a checked-out one is checked in rather than published again under a new
name.

- Profile picker: every profile in the repo, plus an explicit **No mod list**.
- The revision that will be recorded, shown as a number, since it is a declaration.
- Superseding, stated inline rather than as a second dialog: *"**Season 4** is Old-school's current
  savegame. Publishing this makes it past — it stays playable and stays on rev 1004."*
- Where the chosen profile is not the one the folder is on: *"This folder is on **Vanilla**.
  Nothing checks that this savegame can run on Old-school."*

### Profile page

Its current savegame by name, with whoever holds it. Past ones as a count linking to the savegames
list with the toggle on.

The archived-current case stated with its three ways out: *"**Season 4** is this profile's current
savegame and is archived. Un-archive it, delete it, or publish a new savegame."*

Three actions under it, for members: **Publish** on every profile, opening the publish dialog on this
profile; **Check out** and **Check in** only where there is a current savegame. They are the savegames
list's own flows (`SavegameFlowService`), enabled by the same row rules, and greyed with the reason as a
tooltip where this machine cannot do them. An archived current savegame is not checked out from here
either, since the savegames list does not show it.

## Consequences elsewhere

`SavegameSnapshot`'s foreign key onto `ProfileRevision` is `Restrict`, and
`PruneProfileRevisionsV1Endpoint` already refuses to delete a revision a savegame snapshot holds.
A past savegame's revision therefore stays reproducible with no further guarantee.

`SavegameDriftKind.PlayedOnAnotherModList` is retained, and its rule changes what it compares
against.

`SavegameDriftRules.HasMovedOffItsModList` used to compare the binding's revision — the one check-out
applied — against the applied one. That fired on the ordinary follow-the-profile flow: the savegame is
checked out at rev 1000, the profile is applied at rev 1004, and the two numbers differ because the
savegame is following its profile exactly as intended.

It compares against the savegame's **target** instead:

| Condition | Reported |
| --- | --- |
| Applied profile differs from the savegame's | Yes. Two profiles' revision numbers are not comparable |
| **Past** savegame, applied revision differs from its pinned revision | Yes. The apply table forbids moving it, so a mismatch is an interrupted sync or discarded local state |
| **Current** savegame, applied revision differs from the profile's head | No — not here. That is `profileHasMoved` at the folder level, which already reports it |
| Savegame with no profile | No. There is no mod list it claims to match |

The binding's `ProfileRevision` is then read for play attribution only, and by nothing that
decides drift.

### A savegame never targets a revision older than it was played on

| Transition | Target becomes | Last played | Holds |
| --- | --- | --- | --- |
| Current, profile advances | head | ≤ head | ✓ |
| Current superseded | its head snapshot's revision | that same revision | ✓ equal |
| Past, played and checked in | unchanged | unchanged | ✓ |
| Past made current | head | ≤ head | ✓ |

Superseding lowers the target — from head down to the revision the savegame was actually played on —
and that is correct rather than a violation: the savegame was never played on the revisions it was
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

A published savegame's first snapshot carries a declared revision rather than an observed one. The
bytes existed before ModsDude saw them, and no arrangement of the publish flow can recover which
mods were in the folder while that savegame was played.
