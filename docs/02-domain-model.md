# Domain model

All server entities live in `ModsDude.Server.Domain` and have no framework dependencies.
Identity is expressed with strongly-typed record structs (`RepoId`, `ModId`,
`ModVersionId`, `ProfileId`, `RevisionNumber`, `UserId`, `DisplayName`, `ProfileName`, `InviteCode`) so that a `Guid` from one
aggregate cannot be passed where another is expected. EF maps these through a convention
registered in `ModelConfigurationBuilderExtensions.ConfigureValueObjectConversionsFromAssembly`,
and ASP.NET binds them through `StronglyTypedIdModelBinder`.

## User

`ModsDude.Server.Domain/Users/User.cs`

| Field | Notes |
| --- | --- |
| `Id` | `UserId(string)` — the `sub` claim from Entra. Not a Guid we mint |
| `DisplayName` | The `name` claim, verbatim. **Not unique** — re-read from the token on every request, so a rename at the identity provider propagates |
| `Created`, `LastSeen`, `ProfileLastUpdated` | `LastSeen` is refreshed at most once an hour |
| `IsTrusted` | Gates repo creation. Private setter — **there is no code path that sets it to true** |

Users are **auto-provisioned**. There is no registration endpoint; `UserLoadingMiddleware`
creates the row on the user's first authenticated request. See
[03 — Server](03-server.md#user-provisioning).

`IsTrusted` is deliberately flipped by hand in the database. For a group of this size that
is the intended process, not an oversight — but it means a brand new user cannot create a
repo until someone runs an `UPDATE`.

### Names are not unique, and nothing tries to make them

Two people called Anton are both called Anton. Nothing here appends a suffix, and no index
forbids the second one: `DisplayName` is a label, `UserId` is the identity, and the only
lookup anybody does is by invite code.

What separates them is `UserTag` — four digits derived from the subject id by SHA-256:

```csharp
UserTag.For(new UserId("...")) // "4821"
```

Derived from the id rather than from arrival order, so of two Antons neither is the
original and neither is the copy. It is the same four digits everywhere, it survives a
rename at the identity provider, and it is not an identifier — nothing looks a user up by
one. It exists to be read, which is why four digits is enough: it only has to separate the
handful of people in front of a reader.

`UserDto` carries `DisplayName` and `Tag` separately, and the client decides per rendered
list whether the tag is worth the space. See
[05 — Client](05-client.md#telling-two-users-with-one-name-apart).

## Repo

`ModsDude.Server.Domain/Repos/Repo.cs`

The aggregate root that owns everything else. A repo is a shared mod collection for one
game.

| Field | Notes |
| --- | --- |
| `Id` | `RepoId(Guid)`, minted client-side in the constructor |
| `Name` | `RepoName(string)`, **not unique** — see below |
| `AdapterData` | `(AdapterIdentifier Id, AdapterConfiguration Configuration)` — an EF complex property |
| `Created` | |
| `_memberships` | Private set, mapped by EF through the backing field and auto-included |

### Repo names are not unique either

Same rule as display names, for the same reason. Two groups who both called theirs Vanilla
both have a Vanilla — there is no index forbidding the second one, and no endpoint asking
whether a name is free.

Uniqueness bought nothing. Nothing looks a repo up by name: the only way into one is an
invite code, so the name is display text. What it charged for that was a rename forced on
whoever happened to name their repo second, by a clash with a repo they cannot see and a
group they have never met.

What separates them is `RepoTag` — the same four-digit derivation as `UserTag`, over the
repo id:

```csharp
RepoTag.For(new RepoId(...)) // "4821"
```

Both share `Domain/Tags/FourDigitTag.cs`. Derived from the id rather than from creation
order, so of two Vanillas neither is the original; it is the same four digits for every
member of the repo, and it survives a rename — which matters more here than for users,
since renaming is the thing somebody reaches for to escape a clash.

A client shows the tag **only where a list actually holds two of a name**, and shows it on
every repo in that group rather than on the latecomer. That decision is
`RepoDisplay.FindAmbiguous`, the repo twin of `UserDisplay.FindAmbiguous`, and it is made
at the moment of rendering because ambiguity is a property of the list, not of the repo.

`AdapterData.Id` is a serialized `GameAdapterId` such as `_farming_simulator@1`.
`AdapterData.Configuration` is an opaque JSON string — **the server never parses it**. It is
produced and consumed entirely by the client's adapter layer. That opacity is what lets a
new game be supported without a server deployment.

### Invariants enforced on the entity

- `AddMember` throws if the user is already a member. `UpdateMembershipLevel` upserts instead.
- `KickMember` throws if the user is not a member, or is the only Admin.
- The creator becomes Admin (`UpdateMembershipLevel(firstAdmin, Admin)` in the constructor).

The "only Admin" rule is checked both on the entity (`IsOnlyAdmin`) and again in the
endpoint, which returns a typed problem rather than letting the exception escape.

## RepoMembership

`ModsDude.Server.Domain/RepoMemberships/RepoMembership.cs`

A `(UserId, RepoId)` pair with a level. The level enum is deliberately numeric:

```csharp
public enum RepoMembershipLevel
{
    Guest = 100,
    Member = 200,
    Admin = 300
}
```

The gaps leave room to insert levels later without renumbering, and the ordering makes
`membership.Level < minimumLevel` a valid authorization check. It is persisted through
`RepoMembershipLevelValueConverter` rather than as a raw int, so the stored representation
survives a renumbering.

Roughly, what each level can do — see [03 — Server](03-server.md#endpoint-reference) for
the exact per-endpoint requirements:

| Level | Can |
| --- | --- |
| Guest | Read mods, profiles, and mod dependencies |
| Member | Everything a Guest can, plus register mods, request upload links, and create/edit/delete profiles and dependencies |
| Admin | Everything a Member can, plus rename the repo, change its adapter configuration, delete it, and promote others to Admin |

Membership is also navigable from `User.RepoMemberships`, auto-included by EF. That is what
makes the fluent authorization builder cheap — one `Users.GetAsync` load brings the whole
membership set with it.

## RepoInvite

`ModsDude.Server.Domain/Invites/RepoInvite.cs`

The only way a membership is ever created after a repo's first Admin. A Member or Admin
mints a code; whoever holds it joins with it themselves.

| Field | Notes |
| --- | --- |
| `Code` | `InviteCode(string)` — twelve Crockford base32 characters, unique by index |
| `GrantedLevel` | What the redeemer joins as. Capped at the creator's own level, and **never Admin** — see below |
| `ExpiresAt`, `MaximumUses` | Either, both, or neither. Null means unlimited |
| `Uses` | Successful joins only |
| `IsRevoked` | One-way. A code that has been out in the world cannot be un-retired |
| `DismissedAt` | When somebody took it off the repo's invite list, or null while it is on it |

**An invite can never grant Admin**, however senior the person minting it. A code is a secret
that travels, and the limits above are an admission that one can end up somewhere it was not
meant to go. Everything a leaked Guest or Member code costs is recoverable by an admin — kick
the stranger, revoke the code. A leaked Admin code hands over the power to do that, which
nothing can take back. Admin is granted deliberately, to a named person already in the repo.
The constructor refuses it and `CreateInviteV1Endpoint` reports it as
`invite-cannot-grant-admin`; the client simply does not offer it in the picker.

`GetStatus(now)` folds the three limits into `Active | Expired | Exhausted | Revoked`,
reporting revocation ahead of the other two because it is the one somebody chose. `Redeem`
refuses anything but `Active`, and the row carries Postgres' `xmin` as a concurrency token
so two people racing for the last use of a capped invite cannot both win.

**Revoked invites leave the list; spent ones do not.** `Revoke` stamps `DismissedAt` as well,
because switching a code off and taking it off the list are one act, and every retired code would
otherwise accumulate there forever. An `Expired` or `Exhausted` invite stays until somebody removes
it: it is the only evidence the invite was made at all, and its absence reads as "I forgot to create
one" rather than "it ran out". `Dismiss` refuses an `Active` invite, which is the one state this
must never produce — a working code, out in the world, off the only screen that could revoke it.

Dismissal hides rather than deletes. The row keeps the join count and keeps the code unusable, and
the listing query is what filters it.

Codes are read aloud and typed back in, so the alphabet excludes `I`, `L`, `O` and `U`;
`InviteCodes.TryParse` folds the first three into the digits they resemble and accepts any
casing or spacing. `InviteCodes.Format` prints them in threes of four — `ABCD-EFGH-JKMN`.
Twelve characters is sixty bits, which is not a password but is not guessable in any number
of attempts a server will answer either.

### Why invites rather than a user search

Searching for a user by name is an enumeration surface: it makes every person with a
guessable name reachable by a stranger, and it lets somebody be added to a repo without ever
agreeing to it. An invite inverts both. There is no directory to walk, a user is reachable
only through a code they were handed, and joining is an act by the person joining. That is
also what makes non-unique display names safe — no lookup depends on a name being unique.

## ModVersion

`ModsDude.Server.Domain/Mods/`

There is **one entity**, `ModVersion`, keyed `(RepoId, ModId, Id)`. There is no `Mod`: a "mod"
is not a row, it is the set of rows sharing a `ModId`. See [Flattening](#flattening) at the end
of this section for why, and what it removed.

**`ModId` and the version id are supplied by the game, not minted by us** — for Farming
Simulator, the archive filename without its extension and the `modDesc/version` string, which is
how the game itself identifies a mod. The same conceptual mod in two different repos is
therefore two different sets of rows.

| Field | Notes |
| --- | --- |
| `RepoId`, `ModId`, `Id` | The composite key. `Id` is `ModVersionId(string)` |
| `SequenceNumber` | Position in the upgrade order. Unique per `(RepoId, ModId)` |
| `DisplayName`, `Description` | Per-version, because mods rename themselves between releases |
| `FileName` | What the archive is called on disk, in the casing it was imported with. See [The casing trap](09-mod-catalog.md#the-casing-trap) |
| `ContentHash` | SHA-256 of the mod file. See [Content hash](#content-hash) |
| `Locked` | The mod is version-sensitive. See [Locking, in two places](#locking-in-two-places) |
| `Attributes` | An owned collection of free-form `(Key, Value?)` pairs |
| `Images` | An ordered collection of `ModImageReference`. See [Images](#images) |
| `Created`, `Updated` | `Updated` is what the mod list's delta form is keyed on |

`Attributes` is for **tags and categories** — free-form labels an adapter attaches for
searching and filtering. Nothing populates it beyond what the registering client sends.

**The system must never depend on an attribute.** Attributes are opaque, optional, and written
by whichever client registered the version; anything the system needs in order to behave
correctly is a real property with a real column. If a piece of data changes what the software
*does* rather than what it *displays*, it does not belong here — see `ContentHash` and `Locked`
below, both of which were briefly and wrongly proposed as attributes.

### Version ordering

`SequenceNumber` is the source of truth for "which version is newer", and it is kept
**contiguous**. With no parent entity left to hang it on, the logic lives in a static
`ModVersionSequencer` that takes the sibling set — every version sharing one `(RepoId, ModId)` —
as a parameter:

- `CheckPlacementIsValid` / `MakeRoomAt` — validate a placement and shift the siblings that
  follow it up by one, returning the sequence number the new version takes.
- `CheckMoveIsValid` / `CheckMoveChangesTheOrder` / `VacateForMove` / `MoveTo` — the same for an
  already-registered version being moved.
- `CloseGap` — closes the hole a removed version leaves.

**Order derives from the mod's own version string**, compared by a comparer the adapter
supplies — Farming Simulator's `1.2.3.4` and another game's `v2-beta` do not compare the same
way. The comparison runs **client-side**: the server has no adapters and cannot parse a version
string, so registration carries a placement and the server validates and stores it.

The comparer is best-effort and allowed to **abstain**: `modDesc/version` is free text, and
cases like `v1` against `1.0` are genuinely undecidable from the strings alone. Where it
abstains, the user arbitrates in a single batched dialog and the answer is persisted — ordering
is a repo-level fact every member shares.

Because the comparer may abstain, ordering a set of versions is a **partial order plus a
topological sort**, not a call to `OrderBy` — and an abstention only becomes a question when
nothing else settles the pair transitively. An import can also introduce several new versions of
one mod at once, which shifts already-registered rows. See
[09 — Mod catalog](09-mod-catalog.md#ordering-a-set-is-a-partial-order-not-a-sort).

`MakeRoomAt` and `MoveTo` capture the target position into a local and materialise the shift
query before mutating anything. That is deliberate: the predicate reads a sequence number the
loop body changes, so leaving the query lazy made the result depend on enumeration order.

#### A move is a rotation, and a rotation cannot be renumbered in place

Worth knowing before touching `MoveTo`, because it is not obvious and only a real database shows
it. An insert or a removal is a *chain* of row writes — each row takes the slot of its
neighbour, and there is an order in which no two rows ever hold the same sequence number at
once, which EF finds. A move is a *cycle*: everything between where the version left and where
it lands shifts by one, and the moved version takes the slot at the far end. **No order of
single-row writes takes a cycle through the unique index on `(RepoId, ModId, SequenceNumber)`**
— PostgreSQL rejects it as a circular dependency.

So a move is two writes. `VacateForMove` parks the moved version past the end of the ordering
and that write reaches the database first, breaking the cycle into a chain; `MoveTo` then
renumbers the rest. The halfway state is non-contiguous, so the two belong in one transaction,
which is what makes it unobservable.

### Content hash

`ModVersion.ContentHash` is a SHA-256 of the mod file, computed by the client while it uploads
and sent with registration. It is what makes the local content store safe to share between
repos, and it is a first-class property rather than a `ModAttribute`, because the system depends
on it for correctness rather than merely storing it on an adapter's behalf. See
[07 — Mod sync design](07-mod-sync-design.md#content-hashing).

The server does not verify it. The guarantee comes from verification on the *download* side —
see [Cache isolation](07-mod-sync-design.md#cache-isolation) — and from the upload recording the
same hash as blob metadata, so an orphaned blob can be identified before anything is registered
against it.

### Images

`ModVersion` carries an **ordered collection of `ModImageReference`** — hash, kind (icon or
store image), **rendition** (thumbnail or full), position, original filename — so a mod nobody
has locally still renders with its real artwork.

**`Rendition` is the field the original design did not have**, and its absence showed. Every
image is stored as two derivatives; with only kind and position to distinguish references, an
icon could have at most one of them, and store images had to smuggle the rendition into
`Position` as arithmetic. `Position` now means only where the source image sits in the mod's own
list, the two renditions of one image share it — which is what identifies them as one image,
including when only one of the pair made it up — and `CheckImagesAreValid` enforces at most one
icon *per rendition* and no two images of a kind at the same rendition and position. See
[09 — Mod catalog](09-mod-catalog.md#two-sizes).

References, not blobs: the images live in blob storage keyed by content hash, so versions that
reuse the same artwork share one copy, which is the normal case when a release changes only a
script. Deleting a version drops its references; a blob is collectable once nothing points at
it.

`SetImages` replaces the whole set rather than adding to it. Imagery is uploaded best-effort
after registration, so it arrives late, in unknown completeness, and possibly more than once
when a client retries or a backfill fires — a replace is the only shape of that which is
idempotent.

Structural, so **not `ModAttribute`s** — the system dereferences these to decide what to render.
See [09 — Mod catalog](09-mod-catalog.md#mod-imagery).

### Flattening

`Mod` used to *contain* an ordered set of `ModVersion`. It is gone. There is one entity, keyed
`(RepoId, ModId, Id)`, holding everything: display name, description, attributes, ordering,
`ContentHash`, image references, `Locked`, both timestamps. A "mod" stopped being a row and
became what it always was in practice — a group of version rows sharing a `ModId`.

The containment was the wrong way round. A record about a mod is really a record *of a version*
of it, and the data had already migrated accordingly: `Mod` was left holding identity, two
timestamps and a collection.

What this removed:

- The create-or-append branch in `RegisterModV1Endpoint` — every registration is one insert.
- The shadow FK properties and owned-collection mapping in the EF configuration.
- `Navigation(x => x.Versions).AutoInclude()`, which was part of why
  `GET repos/{id}/mods` materialised every mod, every version and every attribute at once.
- The `Mod.RepoId` `TODO`, by answering it.
- Two entities where the wire format, the adapter output and the UI rows are all per-version
  anyway.

**Ordering stays contiguous.** `SequenceNumber` works as it did: a placement shifts later rows
up, a removal closes the gap, and the unique index on `(RepoId, ModId, SequenceNumber)` enforces
it.

A sparse key with gaps was considered, to make every insert a single row. It is not worth it.
The shift touches one mod's versions — tens of rows, found by an indexed query, mutated in
memory and written by one `SaveChanges`, which is already atomic. The aggregate disappears
because the *entity* flattens, not because the shift does, so nothing is gained by removing a
write that was never the problem.

Gaps would also introduce an exhaustion case to reason about — halving a 1024 gap runs out after
ten consecutive back-fills between the same pair — in exchange for solving nothing. Contiguous
numbering has no such case, and keeps the values readable when someone is looking at the table.

The logic itself is unchanged; it moved off the `Mod` entity into `ModVersionSequencer`, since
there is no longer a parent to hang it on.

**`Locked` moved to the version.** It is set by the adapter from the mod file, and since every
version of a map mod declares its maps, the adapter's answer is the same each time — consistent
by derivation rather than by being stored once. The trade is that there is no repo-wide
*user* override: someone who disagrees unlocks on the `ModDependency` instead, which is
per-profile and survives version changes. See [Locking, in two places](#locking-in-two-places).

**Two domain methods lost their navigation.** `ModDependency.CanBeUpgraded()` and `Upgrade()`
reached the sibling versions through `ModVersion.Mod.Versions`, and were changed to take the
candidate set as a parameter instead. That was the real cost of flattening, and it was small.
Both are gone now: a dependency belongs to an immutable revision and no longer moves — see
[Profile revisions](#profile-revisions) — and what counts as an update is decided client-side.

It was done **early**, in one migration against an empty database — the same argument as
normalising mod-id casing.

## Profile

`ModsDude.Server.Domain/Profiles/`

A `Profile` is a named mod list inside a repo, keyed `(RepoId, ProfileId)` with a unique
index on `(RepoId, Name)`. The row itself holds identity, a name, and one number:

| Field | Notes |
| --- | --- |
| `Id`, `RepoId` | The composite key |
| `Name` | Unique within the repo |
| `Created` | |
| `HeadRevision` | `RevisionNumber(int)` — which revision is current |

**What it pins is not here.** It lives on `ProfileRevision`, and there is deliberately no
navigation from a profile to its revisions: a profile's history is hundreds of thousands of
dependency rows at the volumes this targets, and a navigation would drag it into every load of a
profile — renaming one, deleting one, checking a name.

## Profile revisions

`ModsDude.Server.Domain/Profiles/ProfileRevision.cs`

A `ProfileRevision` is **one immutable snapshot of a profile's mod list**, keyed
`(RepoId, ProfileId, Number)`. Numbers are contiguous and one-based, so "revision 7" is something
somebody can say out loud and find.

| Field | Notes |
| --- | --- |
| `Number` | `RevisionNumber(int)`. Position in this profile's history |
| `ModDependencies` | The snapshot. An owned collection, as it was on `Profile` before |
| `ModCount`, `Changes` | Denormalized at creation — see [Why the summary is stored](#why-the-summary-is-stored) |
| `CreatedBy`, `Created` | Who saved it and when. The first thing about a shared profile that was never recorded before |
| `Label` | What somebody called this save. Optional; most saves are not named |
| `Origin` | `Created \| Saved \| Restored \| Copied` |
| `SourceProfileId`, `SourceRevision` | Where the contents came from, for a restore or a branch |

`ModDependency` is unchanged in shape and now set once:

```csharp
public class ModDependency
{
    public required ModVersion ModVersion { get; init; }
    public required bool Locked { get; init; }
}
```

Three rules make this work as a coordination mechanism:

1. **A revision pins each mod at exactly one version.** `ProfileRevision`'s constructor throws
   on a set that pins one mod twice, and the unique index on
   `(RepoId, ProfileId, RevisionNumber, ModId)` enforces the same rule underneath it. This is
   what makes a profile unambiguous — it is not a set of constraints to be solved, it is a
   pinned list.
2. **`Locked` decides whether the pin may move.** A locked dependency is one the group has
   decided to hold, typically because a newer release broke something. It no longer *moves*: a
   changed pin is a new revision carrying a different set.
3. **A revision is immutable, and nothing can address one to write to it.** See below.

### Read-only by having no address

An old revision is not read-only because a flag says so. It is read-only because **no route
names a revision to write to**: writes address the profile and always mean its head.
`ProfileRevision` has no method that changes what it pins, and
`PUT repos/{repoId}/profiles/{profileId}/revisions` produces a *successor* rather than editing
anything.

That is the whole enforcement. There is no `IsReadOnly` column, and therefore no fifteen places
that have to remember to check it.

### A snapshot, not a changeset

The mod list *is* the profile, so an event log would make every read of history a fold — and the
one-version-per-mod rule would stop being an index and become a hope. The cost is rows: a
two-thousand-mod profile writes two thousand narrow rows per revision, which at a hundred
revisions is two hundred thousand rows for one profile. That is the cheap half of the trade.

Structural sharing — deduplicating identical dependency sets by hash — was considered and
rejected. It buys nothing at this scale and makes every read indirect.

### Why the summary is stored

`ModCount` and `Changes` (added, changed, removed) are computed once, when the revision is
created, and written to its own row. The history page renders tens of revisions at a time, and
deriving those numbers on demand would mean diffing every adjacent pair of two-thousand-mod
snapshots to produce three integers per line.

They are facts about an immutable pair, so there is nothing to keep in step.
`ProfileRevisionChanges.Between` computes them, keyed by mod — which is what makes a mod that
moved version a *change* rather than a removal and an addition, and a toggled lock a change at
all.

### Rolling back copies forward

Restoring revision 3 while the head is 8 produces **revision 9**, pinning what 3 pinned, stamped
`Origin = Restored` and `SourceRevision = 3`. Nothing is deleted.

Moving the head backwards instead would strand 4 through 8 as a future nobody can reach, and
force a tree the moment anyone saved after rolling back. Deleting them would destroy the record
of what people were actually running, and can invalidate the sync manifest of an instance that
applied one. So a rollback is an ordinary edit whose contents happen to equal an old revision's,
and undoing a bad rollback is another rollback.

Branching a profile off is the same primitive pointed somewhere else: `POST .../profiles` with
`CopyFrom` materializes an old snapshot as revision 1 of a **new** profile, stamped
`Origin = Copied`. One domain method — `Profile.CreateRevision` — with three callers: saving,
restoring, and branching.

### A pinned version cannot be deleted any more

This is the price of history, and it is worth stating plainly.

`ModDependency`'s foreign key onto `ModVersion` is `Restrict`, and dependencies now live on
revisions. So a version any revision of any profile has **ever** pinned stays pinned by that
revision — which means in practice that **a mod version that has been used cannot be deleted**.
`ProfileRevisionExtensions.CheckIfVersionIsDependedOn` reports it, the delete endpoints refuse
it, and the database refuses it again underneath them.

That is accepted rather than worked around. The alternatives are worse: scoping the check to
head revisions lets old revisions dangle, which destroys the one property that makes keeping
history worth anything — that an old revision is still *reproducible*. Blobs are shared by
content hash, so the storage cost is bounded by distinct files rather than by pins.

`GetModUsageAsync` counts accordingly: a profile that pinned a version across ten revisions
counts once, but it does count, whether or not its current revision still pins it. The number
exists to tell somebody whether a delete will be refused.

### Concurrent saves

A save names the revision it was built on. If that is no longer the head, it is refused with
`profile-revision-stale`, carrying what the head is now.

Underneath, the primary key on `(RepoId, ProfileId, Number)` is what makes it true rather than
merely likely: two saves based on the same head both compute the same next number and exactly
one of them commits. The check is the good error message; the key is the guarantee.

Before revisions there was no way to even ask this question. Two members editing one profile
wrote per dependency, last write silently winning per mod, and the profile could end up as
neither person's list.

### Locking, in two places

Locking stops version-sensitive mods being bumped by accident. The motivating case is a
Farming Simulator map: changing map versions partway through a save can corrupt it, and the
damage shows up long after the change that caused it.

`Locked` is a real property in **two** places, and they mean different things:

| Where | Scope | Set by |
| --- | --- | --- |
| `ModVersion.Locked` | The mod itself is version-sensitive | The **adapter**, from the mod file, at registration |
| `ModDependency.Locked` | This profile only | The **user**, from within the profile |

The effective answer is the disjunction — a mod is treated as locked in a profile's mod list
when **either** is true:

```csharp
public bool IsEffectivelyLocked => Locked || ModVersion.Locked;
```

Keeping that on `ModDependency` puts the rule in one place.

**The adapter sets `ModVersion.Locked` from the file, on every registration.** Every version of
a map mod declares its maps, so the answer comes out the same each time — consistent because it
is re-derived, not because it was stored once. There is no prompt at import.

The consequence to know about: **there is no repo-wide user override.** A user who thinks the
adapter is wrong unlocks on the `ModDependency` instead, which is per-profile and carried into
every later revision the client saves. "Unlock" therefore means "in my profile" rather than "in
this repo" — acceptable for a group this size, and the price of flattening `Mod` away.

An adapter can never set `ModDependency.Locked`. Profile-level locking is a human decision about
a human's profile.

`ProfileRevision`'s constructor also refuses a `ModVersion` whose `RepoId` differs from the
profile's — mods do not cross repo boundaries.

### What a revision costs to read

**A revision's checks read the mod's identity off `ModVersion`**, so building one needs the
versions themselves, tracked — a dependency's foreign key is a navigation, and EF cannot be
handed a key on its own. `ProfileRevisionWrites.ResolveAsync` is the single place a save pays
that cost, once per save rather than once per mod.

Reading is the opposite: **nothing outside that materializes a `ProfileRevision`.** Its
dependencies are an owned collection, which EF loads with the entity whether or not anything
asked, so a page of fifty revisions would read a hundred thousand rows to render fifty summary
lines. Everything in `ProfileRevisionExtensions` and `ProfileRevisionReads` projects instead.
See [03 — Server](03-server.md#persistence).

`ProfileModPin` — `(ModId, VersionId, Locked)` — is the form those projections come back in, and
what a comparison works in. It is the shape of a dependency with the version's whole record left
behind.

## Savegame

`ModsDude.Server.Domain/Savegames/`

A named savegame inside a repo, keyed `(RepoId, Id)` with a unique index on `(RepoId, Name)` — the
same aggregate placement as `Profile`, and for the same reasons.

| Field | Notes |
| --- | --- |
| `Id`, `RepoId` | The composite key |
| `Name` | `SavegameName(string)`, unique within the repo |
| `ProfileId` | The profile this save **follows**. Intent, not history — see below |
| `Created` | |
| `HeadVersion` | `SavegameVersionNumber(int)` — which version is current |

**A savegame is not owned by a profile.** It sits beside profiles in the repo, and it is the
*version* that records the one profile revision it was played on. A save moves from revision 6 to
revision 7 as the group updates its mods, so pinning a revision on the savegame would either forbid
that or lie about it.

`ProfileId` is therefore a different fact from the version's, and the two may legitimately disagree:
branch a profile, move the save onto the branch, and the older versions still honestly name the old
profile's revisions. It is the distinction `ActiveProfile` draws against the sync manifest in
[07 — Mod sync design](07-mod-sync-design.md#what-sync-records-and-why-it-has-to), one aggregate over.

As with a profile, **there is no navigation to the versions**. A savegame's history is read through
its own set; this row only ever says which version is current.


## Savegame versions

`ModsDude.Server.Domain/Savegames/SavegameVersion.cs`

One immutable version, keyed `(RepoId, SavegameId, Number)`.

| Field | Notes |
| --- | --- |
| `Number` | `SavegameVersionNumber(int)`. One-based, and **not contiguous** — see below |
| `ProfileId`, `ProfileRevision` | What it was played on. Never null. FK is `Restrict` |
| `ContentHash`, `SizeBytes` | SHA-256 of the packed save, and what it weighs |
| `CreatedBy`, `Created`, `Label` | `Label` is optional, and is what exempts a version from pruning |
| `Origin` | `Created \| CheckedIn \| Forced \| Restored` |
| `BaseVersion` | What the uploader was holding |
| `CheckoutId` | The claim it was checked in against, or null for a publish |

Read-only by the same mechanism a profile revision is: **nothing addresses one to write to it**. A
check-in produces a successor and a restore copies an old one forward, so no route names a version
and there is no `IsReadOnly` column for fifteen places to remember to check.


### What a version says about itself

`SavegameVersion` carries an owned collection of **`SavegameDetail`** — `(Key, Label, Value,
Position)` — written by the client's game adapter and **never parsed by the server**. Same bargain
as `ModAttribute` and the repo's adapter configuration, and it is what lets a new game describe its
saves without a server deployment: Farming Simulator has a map, a difficulty and a money balance,
another game has a seed and a chapter, a third has neither.

**The same rule applies as to attributes: nothing may depend on one.** A fact the system needs in
order to behave correctly is a real property with a real column — `ContentHash` and
`ProfileRevision` are what that looks like. Details exist to be displayed, and a client that ignores
them entirely is still correct.

**`Key` is not shown, and that is the point.** A label is prose: rewordable, translatable,
shortenable because a column got narrow. The key is the stable name for "this is the map", so a
fact that turns out to be worth promoting to a real column later can be found and migrated rather
than parsed back out of a sentence.

**On the version, not the savegame.** A map, a playtime and a money balance describe the bytes
somebody checked in — two versions of one save legitimately disagree about every one of them.
`Position` is stored because "map, then when, then how long" is a judgment the adapter made and a
set has no order to recover it from; a restore copies them forward with the bytes, since the same
bytes were played on the same map and the server has never looked inside a savegame.

### The blob is addressed by content, not by number

`ContentHash` is the address: `{repoId}/{savegameId}/{contentHash}`. This is the one place the
savegame storage layout deliberately diverges from `ModStorageService`, which addresses by identity.

Numbering the blob would have two people checking in at the same moment mint upload links for the
same name, so whichever wrote second would silently replace the other's bytes — and the stale-base
check that decides who takes the head runs *after* that, by which point the loser's save is already
gone. Hashing also makes a restore a pure metadata operation and a duplicate check-in free.

The consequence: **several versions can share one blob**, so what the reclamation sweep reads is a
set of addresses rather than one entry per version.

### Numbers are not contiguous

Unlike `RevisionNumber`, pruning leaves the gap where an old version was. Numbers exist to be said
out loud, and renumbering would make yesterday's sentence point at a different save.

### A played profile cannot be deleted

`SavegameVersion`'s foreign key onto `ProfileRevision` is `Restrict`, and `Savegame`'s onto `Profile`
is too — so a profile any savegame follows, or any version was ever played on, cannot be deleted.
The same bargain as a pinned mod version, one aggregate up, and accepted for the same reason: a save
whose mod list is gone is not restorable, which is the only thing that made keeping it worth
anything. `DeleteProfileV1Endpoint` reports it; the database refuses it again underneath.

### Retention

`SavegameRetention.PlanPrune` keeps the last N versions (default 10), and never prunes the head or
anything carrying a `Label` — labelling a version is the gesture by which somebody keeps it.

Labelled versions are **exempt rather than counted**: the recency window is taken over the unlabelled
ones. Otherwise naming your last two saves would silently leave you with two backups where the
policy promised ten, the keeping gesture causing the loss.

Pruning a savegame's history is legitimate where pruning a profile's is not: a savegame version is a
backup, and an old profile revision has to stay *reproducible*.

## Savegame checkouts

`ModsDude.Server.Domain/Savegames/SavegameCheckout.cs`

One person's claim on one savegame, keyed on `Id` and carrying `(RepoId, SavegameId)`.

| Field | Notes |
| --- | --- |
| `UserId`, `TakenAt` | Who took it, and when |
| `ExpiresAt` | Pushed forward by `Renew` while the holder still has the app open |
| `EndedAt`, `EndedReason` | `CheckedIn \| TakenOver \| Discarded`. Null while this is the open row |

**A log, not a field.** The current holder is the row that has not ended — a filtered unique index on
`(RepoId, SavegameId) WHERE "EndedAt" IS NULL` permits exactly one — so there is no
current-checkout column to keep in step with a history sitting beside it. Check-ins are already
history, because they are versions; `SavegameVersion.CheckoutId` joins the two halves into one
timeline.

**The claim is advisory.** Anybody may take it from anybody, which closes the previous row as
`TakenOver` and warns naming who held it. What actually protects a save is the base-version check on
check-in: the claim is the social half, and only the mechanical half is a guarantee.

**Expiry is not an end reason.** An expired claim is still the open row; it just reads as stale,
because nothing runs to close it and a job that did would be inventing an event nobody caused.
`GetStatus(now)` folds the two facts into `Held | Stale | Ended`, reporting `Ended` ahead of expiry —
what actually happened outranks what would have happened. The distinction is the point of the type:
"Anton has had this since 3 March" must not read as "Anton has this".

## Archiving

Repos, profiles and savegames are **never deleted directly**. Each is the thing a group's shared
work hangs off, and each takes something irreplaceable with it — a profile's history, a savegame's
backups, a repo's whole catalog. Archiving is how one goes away; permanent deletion is a second act,
reached from an archive and refused outright on anything still live (`not-archived`).

`IArchivable` is the whole contract: a nullable `ArchivedAt`, `Archive(now)`, and a `Restore` that
takes a replacement name for the two kinds that need one.

**Archiving changes exactly two things: visibility, and the name.** The entity still exists, still
answers to its id, and everything pointing at it keeps pointing at it — an instance goes on tracking
an archived profile, a savegame goes on following one, a claim on an archived savegame is not
released. Anything more would make the archive a second kind of deletion wearing a gentler word.

**An archived profile or savegame does not hold its name.** Those two are unique within their repo,
and the indexes enforcing that are filtered (`WHERE "ArchivedAt" IS NULL`), the same shape as the
checkout log's one-open-row index — so a name is free the instant it is archived and any number of
archived things may share one. They are told apart by **when they were archived**, which is why
`ArchivedAt` is on the DTO rather than being an implementation detail.

The cost lands on the way back. `Restore` takes an optional new name, and restoring into a name
something live has since taken is refused with `name-taken` — the clash is deferred to the one
moment somebody is present to decide. `Archive` is idempotent and does **not** restamp: the
timestamp is what orders somebody's archive, so archiving twice must not move it.

**Repos are the exception, because their names are not unique to begin with.** Nothing is freed by
archiving one and nothing can take its name while it is away, so `Repo.Restore()` takes no name and
cannot fail on one, and the repo Archive page has no rename prompt. Two rows there called the same
are told apart by `RepoTag`, the same four digits the sidebar shows — `ArchivedAt` is still on the
row, now saying which *archiving* it is for a repo that has been put away more than once.

| Action | Level |
| --- | --- |
| Read an archive | Guest — a profile that quietly vanished has to be explainable to whoever noticed |
| Archive or restore a **profile** or **savegame** | Member — curating a repo's profiles and saves is what a Member is for, and archiving is reversible |
| Archive or restore a **repo** | **Admin** — it leaves every member's sidebar at once |
| Delete permanently, all three | **Admin** — the irreversible half |

**What still refuses a permanent delete** is unchanged and unrelated to archiving: a repo that holds
mods (`repo-not-empty`), and a profile a savegame follows or was played on
(`profile-in-use-by-savegame`). A save whose mod list is gone is not restorable, which is the only
thing that made keeping it worth anything.

## Client-side models

The client does not reuse the server's entities. It has its own, in
`ModsDude.Client.Core/Models/`:

- **`Repo`** — wraps `RepoMembershipDto`, resolves the game adapter from `AdapterId` and
  hydrates it with the stored base settings, and exposes the `LocalInstance` list matching its
  `InstanceScope`. Disposable, because it holds a collection synchronizer.
- **`ModKey` / `ModVersionKey`** — the join keys, and the reason mod-id casing can no longer
  leak. `ModKey.From` normalizes, and the type has no other representable form, so nothing can
  hand raw casing to blob storage. See
  [09 — Mod catalog](09-mod-catalog.md#the-casing-trap).
- **`ModFileName`** — the other half of that: what a mod's file is *called*, carried beside the
  normalized id so that the mod folder does not inherit the normalization. Also a private
  constructor, because the value arrives from another member's disk and becomes a path here.
- **`CatalogModVersion`** — the merged model the catalog, the management list and the profile
  editor all render: **one record per version, no parent**, carrying `IsLocal` and `IsOnServer`
  as independent facts. There is no client-side `Mod`; the wrapper that pre-split latest from
  older versions was deleted with the flattening, and grouping is a `ToLookup(x => x.ModId)`
  built where it is needed. Full reasoning in
  [09 — Mod catalog](09-mod-catalog.md#a-merged-model).
- **`LocalInstance`** — **one mod folder** on this machine: a sync target. Holds the
  deserialized `DynamicForm` instance settings and the adapter instance built from them.
  **Never sent to the server**; persisted in `state.json` (see [05 — Client](05-client.md)).

  An instance is scoped to a **game**, not to a repo. That matters as soon as someone joins
  two repos for the same game: they have one installation, and it should be configured once
  and offered under both. The scope is not the adapter id — one adapter serves both Farming
  Simulator 22 and 25 — but an `InstanceScope` the base adapter derives from its base
  settings; see [04 — Game adapters](04-game-adapters.md#instance-scope). A game that keeps
  mods in more than one place gets one instance per folder — the model tracks folders, not
  installations, and does not assume a game is installed at all.

  Because sync makes a folder match a profile exactly, an instance has **one active profile
  at a time, from one repo**, recorded as a `(RepoId, ProfileId)` pair. Ownership is
  explicit: the persisted instance also records the folder its adapter says it owns, so the
  no-two-instances-own-one-folder check can run across every scope — including for an instance
  whose scope no repo on this machine serves, which cannot hydrate an adapter and still owns its
  folder.
- **`LocalMod` / `ModImage`** — a mod as found on disk, with lazy `Func` accessors for
  the file stream and image bytes. Everything is deferred: at two thousand mods per folder,
  eagerly reading icons would mean unpacking every archive. `ModImage` says nothing about where
  bytes come from, which is what lets a server-backed derivative be the same record with an HTTP
  fetch in its loader.
- **`ModSource`** — somewhere to look for mods: an instance's mod folder, the system Downloads
  folder, or a folder added for the session. See
  [09 — Mod catalog](09-mod-catalog.md#mod-sources).
