# Savegames

A repo can share savegames when its adapter supports them. One person at a time checks a save out,
plays it, and checks it back in. Each check-in becomes a snapshot. The server side of the model is
described under [Domain](domain.md#savegames). This page covers how the client uses it, mostly in
`ModsDude.Client.Core/Savegames` and the check-out and publish wizards in the app.

## How it works

### Four verbs

| Verb | Does |
| --- | --- |
| **Publish** | Turns a slot on this machine into a new savegame in the repo. Optionally tied to a profile. The publisher holds it afterwards |
| **Check out** | Takes the claim and writes the head snapshot into a slot here |
| **Check in** | Packs the slot and uploads it as a new snapshot. Either hands the claim back or keeps playing |
| **Take a copy** | Writes a snapshot into a slot without claiming anything. Guests can do this |

A save can also be restored to an older snapshot (copied forward as a new one), renamed, archived and
deleted.

### Holding a save

While a save is checked out, the machine keeps a `SavegameCheckoutBinding` in local state:

- which target and slot the save is in;
- which snapshot was written there, and the slot's hash at that point;
- the hash last observed;
- the revision play was last confirmed on;
- the chosen revision mode.

The binding is the source of truth. Once someone has played, the slot matches no snapshot on the server,
so nothing else could tell which savegame the slot is.

A game holds **at most one savegame tied to a profile** at a time, because the whole game runs on one
mod list. Saves without a profile don't count towards this. While a save is held:

- The game's settings can't be changed (any held save).
- The game can't be disconnected while it holds a save from a repo you are in. Saves from repos you are
  not in are let go with it (see [Client](client.md#game-page)).
- The game can't switch to another profile or be deactivated (a profile-tied save).
- In compatibility mode, no other revision of the profile can be applied.
- Leaving the repo offers to check the save in first.

### Check-out

`SavegameCheckOutWizard` walks through every decision before anything is written:

1. **Take over**, if someone else holds the claim. Their name is shown.
2. **Which mod list**: latest or compatibility mode (below).
3. **Activate** the save's profile on this game, if it isn't active already.
4. **Check in** whatever this game already holds, if that blocks the check-out. A blocking save from a
   repo you are not in can never be checked in, so the row's Check out is greyed with "Stop tracking it
   first" instead (`SavegameRowOffer.StopsTrackingFirst`).
5. **Pick a slot**. The last slot used is preselected, or the first free one if that is gone or taken.
   The full list, named by the adapter, opens on request.
6. **Check in** whatever save occupies that slot, if ModsDude holds it.
7. **Review**, then go.

Writing into a slot follows the local file rules. The pack is downloaded, then unpacked and verified into
a temporary folder. The old slot folder is moved aside and the new one moved into place. The displaced
folder then goes to the Recycle Bin, or to quarantine, or failing both stays beside the slots. A slot
holding play that exists nowhere else is refused until that save is checked in.

### Compatibility mode

A save carries the revision it was last played on. When the profile has moved on since, the save can be
checked out two ways:

| Mode | The game is applied to | The game afterwards |
| --- | --- | --- |
| **Latest** | The profile's head | Follows the head like normal |
| **Compatibility** | The revision the snapshot was played on | Pinned to that revision while the save is held |

The wizard asks only when it matters. `SavegameCompatibility` scores the change between the played
revision and the head, using the adapter's `SavegameCompatibilityPolicy`. It asks when the score reaches
the adapter's threshold, or when a locked mod changed or was removed. A save in compatibility mode shows
a "Compatibility · rev N" chip.

A friend following someone onto a save in compatibility mode is pinned to the same revision, through
[game activity](domain.md#game-activity).

### Play attribution

Which revision a save was played on is **observed, never declared**. When an apply writes a mod folder's
new manifest, and at every check-in, `SavegamePlayAttribution` looks at the held save's slot:

- If the slot changed since the last look, play happened on the revision the folder's manifest names.
  That revision becomes `LastPlayedRevision`.
- If the manifest names another profile, the play is still noted (the hash moves) but no revision is
  credited. A revision number from another profile means nothing to this save.

During an apply, observation reads the outgoing manifest, so play is credited to the revision the
folder was on while it was played. The check-in sends the last played revision, falling back to the
folder manifest's revision (same profile only), then to the revision the save was checked out on; with
none of those it refuses. The only declared revision is a newly published save's first snapshot, which
takes the folder's revision, or the head if the folder is on another profile.

The binding keeps two hashes on purpose:

| Hash | Changes | Answers |
| --- | --- | --- |
| `ContentHash` | At check-out and after an accepted check-in | Has the slot moved away from what the server has? |
| `LastObservedHash` | At every observation | Has the slot moved since we last looked? |

Merging them would make an apply hide unsaved play from the drift notice.

### Check-in

`SavegameCheckIn` packs the slot, leaving out what the adapter's `BelongsInPackedSave` rejects, and
hashes it:

1. Ask the server for an upload link. A blob already at that hash is `AlreadyStored`, so the upload is
   skipped.
2. Upload to the upload's own blob. A renewal answered with `AlreadyStored` ends the upload.
3. Check in against the snapshot the save was based on, naming the upload, which the server copies to the
   save's address (see [Server](server.md#blob-storage)).

If someone else checked in since, the check-in is refused (`SavegameSnapshotStale`). The user can check
theirs in anyway, recorded as `Forced`, or leave it for now. A check-in whose hash matches the head creates
no snapshot.

Each check-in carries a request ID. The binding records a `SavegamePendingCheckIn` before uploading. After
a crash or lost reply:

- If the slot still packs to the same content, the request is simply repeated under the same ID.
- If it changed, the client asks `GET repos/{repoId}/savegames/{savegameId}/check-ins/{requestId}`
  what became of the earlier request, and continues from there.

An unanswered publish is kept in its own store (`SavegamePendingPublishes`). It is settled by reloading the
live and archived savegame lists and looking for the new save.

### Publishing

The slots offered are the ones holding a save ModsDude has no copy of. A held save is checked in rather
than published again.

`SavegamePublishWizard` picks the slot, the name, optionally a profile, and whether to keep playing. When
the publisher keeps playing a save tied to a profile, the wizard works out a keep plan
(`SavegameKeepPlan`):

- activate the chosen profile first, if the game isn't on it;
- check in the profile-tied save already held, if there is one. Where that save is from a repo you are
  not in, the publish step refuses Keep playing with "Stop tracking it first" instead.

The new save's details come from the adapter.

Where the adapter lists savegame mods:

- **Mods differ**: after a profile is picked, `SavegameModComparison` compares the save's mods with the
  revision it would be published on, by mod ID. The step only shows when something differs, as counts:
  not in profile, not in savegame, other version. Locked mods that differ are named as a warning. Members
  can choose **Review in the editor**, which publishes nothing and opens that profile's editor focused on
  the save (see [Mods](mods.md#savegame-sources)).
- **New profile from this savegame**: offered to members in the profile list. It publishes nothing: it asks
  for a name, creates an empty profile, and opens its editor focused on the save. The save is published
  from the toast once that profile is saved.

### Savegame drift

`SavegameDrift` adds savegame cards to the notice column:

| Kind | Meaning |
| --- | --- |
| `UncheckedInPlay` | Play here that hasn't been checked in |
| `TakenOver` | Someone else took the claim |
| `TakenOverAndCheckedIn` | Someone else took the claim and has checked in since |
| `PlayedOnAnotherModList` | The game's folder has moved off the save's mod list |
| `RepoGone` | The save's repo is gone. Offers "Stop tracking" and "Open game" |

Most cards navigate to the save rather than act from the notice. The exceptions are "Stop tracking" and,
for a compatibility-mode save whose folder moved off its revision, "Re-apply revision N". A hold in a folder
the settings no longer name is shown on the repo's Savegames page.

When a game process exits and a held slot has changed since the session started and since the held
snapshot, a reminder to check in is shown (`PlaySessionWatch`).

A binding survives a removed folder or target, so its play is never forgotten. Bindings of saves deleted
on the server are dropped only after both the live and archived lists loaded, so a failed request is
never read as a deletion.

## Decisions

**The claim is advisory; the base check is the guarantee.** Taking over is always allowed, with a warning
that names the holder. What actually protects a save is that a check-in must be based on the current
head.

**One holder at a time, no merging.** Savegames can't be merged. Two people playing the same save means
one of them loses their play. The claim makes that visible, and the base check makes it explicit.

**Observed attribution, not timestamps.** The profile's head moves without anything happening on this
machine. Only the folder's own manifest says what the game actually loaded.

**The binding is local truth.** After play, nothing on the server can identify the slot. So the binding
is never treated as a cache, and survives missing folders.

**Compatibility mode is the user's choice, prompted by the adapter.** Some changes don't matter to a save
(a few added mods in a large list) and some always do (a changed map). The adapter knows which. The user
decides, once, at check-out.

**One profile-tied save per game.** The game loads one mod list. Two saves on different lists can't both
be right.

**Slots are addressed by target and slot together.** `SavegameSlotRef(TargetKey, SlotId)` is unique by
construction, so an adapter that numbers slots from one in each folder can't make two targets collide.

**Unreadable slots count as occupied.** A slot the adapter can't read is still someone's data, and must
stay impossible to overwrite by accident.

**Slot pickers classify every slot in one pass.** `ISavegameSlots.ClassifySlotsAsync` reads the slots once
and hashes only the slots a binding claims. Only for those does the content decide what the slot is.

**Savegame byte work runs on the thread pool.** `SavegamePacker` and `SavegameTransfer` move their hashing,
packing, unpacking and transfers off the caller's context. Their callers are view models on the UI thread,
and each 64 KiB read would otherwise resume there.
