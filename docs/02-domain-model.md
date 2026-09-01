# Domain model

All server entities live in `ModsDude.Server.Domain` and have no framework dependencies.
Identity is expressed with strongly-typed record structs (`RepoId`, `ModId`,
`ModVersionId`, `ProfileId`, `UserId`, `DisplayName`, `ProfileName`, `InviteCode`) so that a `Guid` from one
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
| `Name` | `RepoName(string)`, **globally unique across all repos**, not per-user |
| `AdapterData` | `(AdapterIdentifier Id, AdapterConfiguration Configuration)` — an EF complex property |
| `Created` | |
| `_memberships` | Private set, mapped by EF through the backing field and auto-included |

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
reached the sibling versions through `ModVersion.Mod.Versions`. They now take the candidate set
as a parameter, supplied by the endpoint that has to query for it anyway. That was the real cost
of flattening, and it is small.

It was done **early**, in one migration against an empty database — the same argument as
normalising mod-id casing.

## Profile

`ModsDude.Server.Domain/Profiles/`

A `Profile` is a named mod list inside a repo, keyed `(RepoId, ProfileId)` with a unique
index on `(RepoId, Name)`.

`ModDependency` is the interesting part:

```csharp
public class ModDependency
{
    public required ModVersion ModVersion { get; set; }
    public required bool Locked { get; set; }
}
```

Two rules make this work as a coordination mechanism:

1. **A profile may depend on a mod at exactly one version.** `AddDependency` throws if a
   dependency on that `ModId` already exists, and the unique index on
   `(RepoId, ProfileId, ModId)` enforces the same rule underneath it. This is what makes a
   profile unambiguous — it is not a set of constraints to be solved, it is a pinned list.
2. **`Locked` decides whether the pin may move.** `Upgrade()` jumps the dependency to the
   latest of the sibling versions it is handed; `CanBeUpgraded()` reports whether a newer one
   exists among them. A locked
   dependency is one the group has decided to hold, typically because a newer release broke
   something.

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
adapter is wrong unlocks on the `ModDependency` instead, which is per-profile and survives
version changes, since `ChangeVersion` does not touch it. "Unlock" therefore means "in my
profile" rather than "in this repo" — acceptable for a group this size, and the price of
flattening `Mod` away.

An adapter can never set `ModDependency.Locked`. Profile-level locking is a human decision about
a human's profile.

`AddDependency` also refuses a `ModVersion` whose `RepoId` differs from the profile's —
mods do not cross repo boundaries.

**Every one of these operations reads the mod's identity off `ModVersion`**, which is what makes
the navigation mandatory rather than optional. A profile loaded
without it has dependencies whose `ModVersion` is `null`, and `AddDependency`,
`DeleteDependency`, `HasDependencyOn` and `ChangeVersion` all throw. See
[03 — Server](03-server.md#persistence).

[Flattening](#flattening) shortened that chain rather than removing the requirement: with no
`Mod` to hop to, the operations read `(RepoId, ModId)` straight off the version and the include
is one level instead of two. `ModDependency.ModVersion` still has to be loaded, and loading
it is still not automatic.

`Upgrade()` and `CanBeUpgraded()` are reachable through
`POST repos/{repoId}/profiles/{profileId}/modDependencies/upgrade`, which is a **batch**: a
profile holds one to two thousand mods, and one round trip per mod is the wrong shape. It skips
locked dependencies entirely and reports each one as skipped, distinguishing the profile's lock
from the mod's, so the client can say why without asking again.

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
