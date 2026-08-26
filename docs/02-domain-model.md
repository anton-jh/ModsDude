# Domain model

All server entities live in `ModsDude.Server.Domain` and have no framework dependencies.
Identity is expressed with strongly-typed record structs (`RepoId`, `ModId`,
`ModVersionId`, `ProfileId`, `UserId`, `Username`, `ProfileName`) so that a `Guid` from one
aggregate cannot be passed where another is expected. EF maps these through a convention
registered in `ModelConfigurationBuilderExtensions.ConfigureValueObjectConversionsFromAssembly`,
and ASP.NET binds them through `StronglyTypedIdModelBinder`.

## User

`ModsDude.Server.Domain/Users/User.cs`

| Field | Notes |
| --- | --- |
| `Id` | `UserId(string)` — the `sub` claim from Entra. Not a Guid we mint |
| `Username` | The `name` claim. **Unique across the system** |
| `Created`, `LastSeen`, `ProfileLastUpdated` | `LastSeen` is refreshed at most once an hour |
| `IsTrusted` | Gates repo creation. Private setter — **there is no code path that sets it to true** |

Users are **auto-provisioned**. There is no registration endpoint; `UserLoadingMiddleware`
creates the row on the user's first authenticated request. See
[03 — Server](03-server.md#user-provisioning).

`IsTrusted` is deliberately flipped by hand in the database. For a group of this size that
is the intended process, not an oversight — but it means a brand new user cannot create a
repo until someone runs an `UPDATE`.

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

## Mod and ModVersion

`ModsDude.Server.Domain/Mods/`

A `Mod` is identified by the composite key `(RepoId, ModId)`. **`ModId` is supplied by the
game, not minted by us** — for Farming Simulator it is the archive filename without its
extension, which is how the game itself identifies a mod. This means the same conceptual
mod in two different repos is two different rows, and `Mod.RepoId` carries a `TODO` asking
whether it should exist at all.

A `Mod` owns an ordered set of `ModVersion`:

| Field | Notes |
| --- | --- |
| `Id` | `ModVersionId(string)` — again game-supplied; the version string from the mod |
| `SequenceNumber` | Position in the upgrade order. Unique per `(RepoId, ModId)` |
| `DisplayName`, `Description` | Per-version, because mods rename themselves between releases |
| `Attributes` | An owned collection of free-form `(Key, Value?)` pairs |
| `Created` | |

`Attributes` is for **tags and categories** — free-form labels an adapter attaches for
searching and filtering. Nothing populates it beyond what the registering client sends.

**The system must never depend on an attribute.** Attributes are opaque, optional, and written
by whichever client registered the version; anything the system needs in order to behave
correctly is a real property with a real column. If a piece of data changes what the software
*does* rather than what it *displays*, it does not belong here — see `ContentHash` and `Locked`
below, both of which were briefly and wrongly proposed as attributes.

### Version ordering

`SequenceNumber` is the source of truth for "which version is newer". `GetLatestVersion()`
returns the highest. The entity exposes three mutations:

- `AddVersion` — appends at the end.
- `InsertVersion(..., before:)` — back-fills an older release that was uploaded late,
  shifting everything from `before` onwards up by one.
- `RemoveVersion` — deletes and closes the gap by shifting later versions down.

**This is a curated ordering, and it is going to change.** The intended model is that order
derives from the mod's own version string, compared by a comparer the adapter supplies —
Farming Simulator's `1.2.3.4` and another game's `v2-beta` do not compare the same way.

The comparer is best-effort and allowed to **abstain**: `modDesc/version` is free text, and
cases like `v1` against `1.0` are genuinely undecidable from the strings alone. Where it
abstains, the user arbitrates in a single batched dialog and the answer is persisted — ordering
is a repo-level fact every member shares. So `SequenceNumber` survives as the stored ordering,
with the comparer filling it in automatically wherever it can, and `InsertVersion` becomes the
mechanism arbitration writes through. See [PLAN.md](PLAN.md).

`InsertVersion` captures the target position into a local and materialises the shift query
before mutating anything. That is deliberate: the predicate reads a sequence number the loop
body changes, over a `HashSet` whose iteration order is unspecified, so leaving the query
lazy made the result depend on enumeration order.

### Content hash

`ModVersion` needs a **`ContentHash`** property — a SHA-256 of the mod file — which does not
exist yet. It is what makes the local content store safe to share between repos, and it is a
first-class property rather than a `ModAttribute`, because the system depends on it for
correctness rather than merely storing it on an adapter's behalf. See
[07 — Mod sync design](07-mod-sync-design.md#content-hashing).

## Profile and ModDependency

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
   dependency on that `Mod` already exists. This is what makes a profile unambiguous — it
   is not a set of constraints to be solved, it is a pinned list.
2. **`Locked` decides whether the pin may move.** `Upgrade()` jumps the dependency to the
   mod's latest version; `CanBeUpgraded()` reports whether a newer one exists. A locked
   dependency is one the group has decided to hold, typically because a newer release broke
   something.

> `LockVersion` is the current name in the code. It becomes `Locked` to match the property of
> the same meaning on `Mod`, below.

### Locking, in two places

*Planned — `Mod.Locked` does not exist yet.*

Locking stops version-sensitive mods being bumped by accident. The motivating case is a
Farming Simulator map: changing map versions partway through a save can corrupt it, and the
damage shows up long after the change that caused it.

`Locked` is a real property in **two** places, and they mean different things:

| Where | Scope | Set by |
| --- | --- | --- |
| `Mod.Locked` | Repo-wide — this mod is version-sensitive, in every profile | The **adapter** at registration, and the user afterwards |
| `ModDependency.Locked` | This profile only | The **user**, from within the profile |

The effective answer is the disjunction — a mod is treated as locked in a profile's mod list
when **either** is true:

```csharp
public bool IsEffectivelyLocked => Locked || ModVersion.Mod.Locked;
```

Keeping that on `ModDependency` puts the rule in one place, since the domain can already reach
the `Mod` through `ModVersion`.

**The adapter sets `Mod.Locked` only, and only when a completely new mod is registered** — not
when a new version is added to a mod that already exists. That maps onto the branch
`RegisterModV1Endpoint` already has: the adapter's determination is an argument to the `Mod`
constructor and is untouched by `AddVersion`. There is no prompt at import; the adapter's answer
is simply the starting value, and the user can change it later from the relevant views.

An adapter can never set `ModDependency.Locked`. Profile-level locking is a human decision about
a human's profile.

`AddDependency` also refuses a `ModVersion` whose `Mod.RepoId` differs from the profile's —
mods do not cross repo boundaries.

Note that `Upgrade()` and `CanBeUpgraded()` are not reachable from any endpoint yet; the
API only offers `ChangeVersion` via `PUT .../modDependencies/{modId}`.

## Client-side models

The client does not reuse the server's entities. It has its own, in
`ModsDude.Client.Core/Models/`:

- **`Repo`** — wraps `RepoMembershipDto`, resolves the game adapter from `AdapterId` and
  hydrates it with the stored base settings, and owns the machine's `LocalInstance` list for
  that repo. Disposable, because it holds a collection synchronizer.
- **`Mod` / `Mod.Version`** — a view over `ModDto` that pre-splits latest from older
  versions.
- **`LocalInstance`** — **one mod folder** on this machine: a sync target. Holds the
  deserialized `DynamicForm` instance settings and the adapter instance built from them.
  **Never sent to the server**; persisted in `state.json` (see [05 — Client](05-client.md)).

  An instance is scoped to a **game adapter**, not to a repo. That matters as soon as
  someone joins two repos for the same game: they have one installation, and it should be
  configured once and offered under both. A game that keeps mods in more than one place gets
  one instance per folder — the model tracks folders, not installations, and does not assume
  a game is installed at all.

  Because sync makes a folder match a profile exactly, an instance has **one active profile
  at a time, from one repo**, recorded as a `(RepoId, ProfileId)` pair. Ownership is
  therefore explicit; the current repo-scoped model has several instances silently believing
  they own the same directory.

  > This is a change from the code as it stands, where `PersistedLocalInstance` carries a
  > `RepoId`. See [PLAN.md](PLAN.md#settled-architecture-decisions).
- **`LocalMod` / `LocalModImage`** — a mod as found on disk, with lazy `Func` accessors for
  the file stream and image bytes. Everything is deferred: at two thousand mods per folder,
  eagerly reading icons would mean unpacking every archive.
