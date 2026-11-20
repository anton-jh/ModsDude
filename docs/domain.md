# Domain

The server's model lives in `ModsDude.Server.Domain`. Entities change only through their own methods,
and the database backs every invariant it can express. This page covers the shape of the model and the
reasons for it. Each type's remarks hold the finer detail.

## How it works

### Users

A user is created only by signing up (`UserSignUp`), together with the account they sign in with. Sign-up
takes a trust code, at least one repo invite, or both, and redeems every one of them or none. Two invites
to the same repo are refused. The display name is typed at sign-up and changed in the app afterwards.

Joining a repo by invite goes through `Repo.Join`, which judges the invite against its inviter's current
level in the repo. Sign-up and the in-app invite redemption both use it.

Display names are not unique. Where one list shows two users with the same name, each gets a four-digit
`UserTag` derived from their ID. Repos get a `RepoTag` the same way. The tag belongs to the list, not to
the person, so it only shows when there is something to tell apart.

A user also has two flags that an operator controls:

- **Trusted.** Only trusted users can create repos. Trust comes from redeeming a `TrustCode`, which is
  single use, lasts 48 hours and is issued on the admin pages, or from an operator granting it there
  directly. Revoking trust bumps the user's `TrustEpoch`, so a fresh code can grant it again.
- **Blocked.** A blocked user gets `403 UserBlocked` on every request. They can't be added to a repo,
  and they don't count as an admin when a repo checks who can still administer it.

### Repos and membership

A repo has a name, a game adapter ID and the adapter's base settings (`AdapterData`), which the server
stores but never reads. Repo names are not unique either.

Members have one of three levels:

| Level | Can |
| --- | --- |
| Guest | Read profiles, mods and savegames, download, record their own game activity, leave |
| Member | Also see the repo's members and invites; register and delete mods; create, edit, archive and restore profiles; publish, check in, check out, archive and restore savegames; invite; kick or promote Guests |
| Admin | Also rename, configure, archive, restore and delete the repo; delete profiles, savegames and snapshots; prune revisions; manage Members and Admins |

Nobody can grant a level above their own. A repo always keeps an admin who can act: kicking or demoting
the only unblocked admin is refused.

### Invites

Users join a repo with an invite code. There is no user search. Operators can also add members, and
create invites, on the admin pages.

- A code grants Guest or Member, never Admin.
- A code can be limited by number of uses, by time, or both, and it can be revoked.
- A code works only while its inviter could still create it: still in the repo, at Member or above, and
  at least at the level the code grants. Kicking, demoting or leaving pauses the inviter's codes
  without touching them.
- Only the code's creator can read it, and only while they are at least a Member. The creator or an
  admin can revoke it.
- Codes use Crockford base32 and are entered through the request body, never the URL.

### Mod versions

There is no `Mod` row. A `ModVersion`, keyed `(RepoId, ModId, VersionId)`, is one file of one mod; a
"mod" is the versions that share a `ModId`. Each version stores:

- `SequenceNumber`: its position in the mod's version order. Contiguous from 1 and unique per mod.
- `ContentHash`: SHA-256 of the file, which the database checks is lowercase hex.
- `FileName`: the bare name the file gets in a game's mod folder.
- `Locked`: whether the adapter considers the mod version-sensitive (see [Mods](mods.md#locking)).
- Attributes and image references: display data only.

The client says where a new version goes ("after A, before B"), and `ModVersionSequencer` places it.
Moving a version vacates its slot, then renumbers, because the unique index would otherwise collide
partway through.

Deleting a version is refused while any revision pins it. A `Restrict` foreign key backs this, so no
history refers to a file that is gone.

### Profiles and revisions

A `Profile` holds a name, the number of its head revision and two concurrency tokens. `Version` covers
the name and archive state. `IgnoredVersion` covers the ignore list, which has its own editor.

A `ProfileRevision` is an immutable snapshot of the whole mod list:

- **Pins.** `ProfileModPin` rows: mod, version, the profile's own lock, and when that version arrived in
  the profile.
- **Number.** Increasing per profile. Pruning leaves gaps and never renumbers.
- **Origin.** Created (the first revision), saved, restored or copied from another profile.
- **Label.** Optional.
- **Summary.** A change summary computed at creation.

Saving is a `PUT` of the whole list against the revision it was based on. A save that changes nothing
creates no revision. A restore copies an old revision forward as a new one. A branch copies another
profile's revision into a new profile.

The ignore list sits beside the revisions, unversioned:

- **Ignored mods** never count as having updates.
- **Dismissed versions** each hide one specific newer version.

A mod can't be both pinned and ignored.

### Savegames

A `Savegame` sits in the repo beside profiles. It has a name, an optional profile chosen at publish
and never changed, a head snapshot and a `Version` token.

- **Snapshots.** A `SavegameSnapshot` is one checked-in state. Its blob is stored at its own SHA-256. It
  records the profile revision it was played on: set together with the profile, or both left empty for a
  save that follows no profile. A check constraint backs that pairing. `Restrict` foreign keys from
  snapshots and from `Savegame.ProfileId` mean a profile that any savegame follows or has been played on
  can't be deleted. Snapshot numbers are never reused, so after a prune they
  can have gaps.
- **Check-outs.** A `SavegameCheckout` row is one person's claim, from taking it to ending it. The open
  claim is the row that hasn't ended, and a filtered unique index allows only one. While open, it holds
  every revision of the save's profile from the one checked out onwards, so pruning can't remove the
  revision the coming check-in will name.
- **Details.** `SavegameDetail` entries are typed (text, date, instant, duration) and display only. The
  adapter computes them on the client.

See [Savegames](savegames.md) for how the client uses all of this.

### Game activity

`GameActivity` is one row per user per game: which profile, which pinned revision if any, and a
heartbeat. Repo members read it to see what friends are on, whether they're playing (a heartbeat within
3 minutes), and to follow them onto it. It is a current state, overwritten by each report, not a log.

### Archiving

Repos, profiles and savegames are archived before they can be deleted.

- Archiving changes only visibility. Archived things stay readable by members and keep their history,
  and anything following them keeps following.
- An archived profile or savegame gives up its name. Restoring it may need a new name if the old one has
  been taken.
- Permanent delete happens only from the archive.

### Retention

`RetentionPolicy` decides which old rows can go. Each history keeps a window of its newest rows, and
anything older is scheduled for deletion after a grace period:

| History | Window | Grace |
| --- | --- | --- |
| Savegame snapshots | 3 | 30 days |
| Profile revisions | 3 | 14 days |
| Mod versions | 2 | 14 days |

- A row that something holds is never scheduled. Holders include a head, a pin, a snapshot that names a
  revision, and an open check-out's revision floor.
- An idle history shrinks to its newest row in two steps.
- The reason is stored with the date, and the deletion job checks again before it deletes.
- Labels keep nothing.
- Archived items are pruned too.

The jobs are described under [Server](server.md#background-jobs).

### Repeatable requests

Every mutation the client might send twice carries a client-generated request ID. The server records it
with the outcome:

| Request | Recorded as |
| --- | --- |
| Save or restore a revision | `ProfileRevisionRequest`: the latest per user per profile |
| Check in or restore a snapshot | `SavegameSnapshotRequest`: the latest per user per savegame |
| Publish a savegame | `SavegamePublishRequest`: one per savegame |
| Create a repo | `RepoCreateRequest`: every request, per user |
| Create a profile, an invite or a trust code | The request ID on the created row, unique per repo |

A repeat with the same ID gets the original outcome, even when the concurrency check would now reject it
as stale. The request ID only identifies the request; it is never used as an entity ID.

### Concurrency

Edits to shared state carry the version they were based on. A stale write is refused with a typed
problem that the client turns into "this changed, reload":

| Entity | Tokens | Problem |
| --- | --- | --- |
| Repo | `Version`, `MembersVersion` | `RepoChanged` |
| Profile | `Version`, `IgnoredVersion` | `ProfileChanged` |
| Profile revisions | The head revision number | `ProfileRevisionStale` |
| Savegame | `Version` (rename, archive, restore) | `SavegameChanged` |
| Savegame snapshots | The head snapshot (check-in, restore) | `SavegameSnapshotStale` |
| Savegame check-out | The expected head and the open claim | `SavegameHeadMoved`, `SavegameClaimChanged`, `SavegameCheckoutConflict` |
| Invites, trust codes | `Version` | `InviteRedemptionConflict`, `TrustCodeRedemptionConflict` |

## Decisions

**Names are not unique; tags tell duplicates apart.** Two groups can both call their repo "Farm". Making
names unique would have forced the second to invent a name, and gained nothing.

**Joining is the joiner's act.** Invite codes mean nobody can be added by a stranger who guessed their
name, and nobody can list users. The limits on a code make up for codes travelling further than meant.

**An invite never grants Admin.** Anything a leaked Guest or Member code costs, an admin can undo. A
leaked Admin code hands over the power to undo things, so Admin is granted only to a named member.

**Trust is granted by codes or the admin pages, never by editing the database.** Codes are single use,
short lived and revocable through an epoch.

**One entity per mod version.** A mod record is really a version: both ends of the pipeline (file on
disk, row in a revision) are per version. Grouping by mod is a query.

**Contiguous sequence numbers.** Sparse keys would avoid renumbering on insert, but would add a key space
that can run out. Renumbering a handful of rows is cheap.

**Behaviour gets a real column; attributes are display only.** `ContentHash` and `Locked` drive behaviour,
so they are typed columns. Attributes and savegame details are tags that nothing depends on.

**A revision is a snapshot, not a changeset.** The mod list is the profile. Replaying a log to answer
"what did revision 6 pin?" would turn every history read into a fold, and turn "one version per mod"
from an index into a hope. The cost is rows, which is the cheap side of the trade.

**Old revisions are read-only because nothing can address them.** Writes address the profile and mean
its head. No flag needs checking. Restoring copies forward, so nothing ever recorded stops being true.

**The revision summary is stored, not derived.** It is a fact about an immutable pair, and deriving it
would mean diffing two-thousand-mod snapshots for every history row.

**No navigation from a profile or savegame to its history.** Histories run to hundreds of thousands of
rows, and every load of the parent would drag them in. Histories are queried through their own sets.

**Anything history names can't be deleted.** Pinned versions and played profiles are protected by
`Restrict` foreign keys, so every revision and snapshot stays reproducible. Retention frees them once
nothing holds them.

**Locking lives in two places.** The adapter sets `ModVersion.Locked` from the file. The user sets the
lock on a pin. A version is locked if either is set. There is no repo-wide override; unlocking is per
profile.

**The ignore list isn't versioned.** It is about what to show as an update, not what the group plays, so
it would only add noise to the history.

**A savegame is not owned by a profile.** A save moves from revision 6 to 7 as the group updates, so each
snapshot records its own revision. The profile is fixed at publish because two profiles' revision
numbers can't be compared. Moving a save to another profile means republishing it.

**A check-out is an advisory log, not a lock.** Anyone may take a claim over, with a warning that names
the holder. The real guarantee is the base-snapshot check at check-in. Claims don't expire, because no
fixed time makes a save free to take; the reader sees who took it and when.

**Savegame blobs are addressed by content.** Two concurrent check-ins can't overwrite each other's bytes,
and restoring an old snapshot moves no bytes.

**Retention keeps windows, not ages.** An idle repo keeps its last few states however old they are. The
grace period gives people time to notice before anything goes.

**A repeat gets the original answer.** Without request IDs, a retried save would either create a second
revision or be refused as stale against the revision it just created. Both are wrong. For revisions and
snapshots, one row per user is enough, because a client only ever repeats its latest request.
