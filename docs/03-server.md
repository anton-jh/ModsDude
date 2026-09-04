# Server

## Project layout

```
ModsDude.Server.Api          ASP.NET Core host — endpoints, DTOs, middleware, problem details
ModsDude.Server.Application  Authorization primitives, ITimeService, IUnitOfWork, the storage abstractions
ModsDude.Server.Domain       Entities and invariants. No framework references
ModsDude.Server.Persistence  EF Core / PostgreSQL — DbContext, entity configuration, migrations
ModsDude.Server.Storage      Azure Blob Storage — SAS issuance, image blobs

ModsDude.Server.Domain.Tests       xUnit over the domain. No infrastructure
ModsDude.Server.Persistence.Tests  xUnit over a real PostgreSQL. See Tests, below
```

The dependency direction is Api → Application → Domain, with Persistence and Storage
implementing Application's abstractions. In practice **the API talks to `ApplicationDbContext`
directly** rather than going through Application — endpoints take the DbContext as a
parameter and query it inline. This is deliberate for a system this size: there is no
repository layer to maintain, and the query lives next to the endpoint that needs it. The
abstractions in `Application/Dependencies` exist for the things that genuinely need
substituting (`IModStorageService`) or that express a transaction boundary (`IUnitOfWork`).

## The endpoint pattern

Every endpoint is a class implementing `IEndpoint`:

```csharp
public class GetModsV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
        => builder.MapGet("repos/{repoId:guid}/mods", GetAll).WithTags("Mods");

    public async Task<Results<Ok<GetModsResponse>,
                              BadRequest<CustomProblemDetails>,
                              Forbidden<CustomProblemDetails>>> GetAll(...)
}
```

`MapAllEndpointsFromAssembly` reflects over the assembly, instantiates every `IEndpoint`,
calls `Map`, and names the route by stripping the `Endpoint` suffix from the type name.
That generated name is what NSwag turns into the client method name — so
`GetModsV1Endpoint` becomes `GetModsV1Async` on `IModsClient`. **Renaming an endpoint class
renames the generated client method.**

Request and response records are nested inside the endpoint class that uses them
(`RegisterModRequest`, `CreateModUploadLinkResponse`). Shared shapes live in `Api/Dtos` with
static `FromModel` / `ToModel` mappers.

All endpoints are mapped into a single group in `Program.cs`:

```csharp
app.MapGroup("api/v{v:apiVersion}")
   .WithApiVersionSet(apiVersionSet)
   .RequireAuthorization()
   .WithMetadata(new ProducesResponseTypeMetadata(
       StatusCodes.Status401Unauthorized, typeof(CustomProblemDetails), ["application/json"]))
   .MapAllEndpointsFromAssembly(typeof(Program).Assembly);
```

so **authentication is on by default** for everything. Versioning is by URL segment via
`Asp.Versioning`, currently only v1.

The 401 is declared once on the group rather than in every endpoint's `Results<...>` union,
because the endpoints that can produce it include the ones returning a bare `Ok<T>` with no union
to put it in — see [Two statuses](#two-statuses-and-which-is-which).

## Request pipeline

```
HTTPS redirect
  └─ Swagger UI (Development only)
      └─ NotAuthenticatedMiddleware
          └─ Authentication  (JWT bearer, Microsoft.Identity.Web)
              └─ Authorization
                  └─ UserLoadingMiddleware
                      └─ api/v1/... endpoints
```

Migrations are applied at startup, after the pipeline is built, by resolving
`ApplicationDbContext` in a scope and calling `Database.Migrate()`.

A `BlobReclamationService` hosted service runs alongside; see [Storage](#storage).

### Authentication

Configured from the `EntraExternalId` configuration section. Two details worth knowing:

- `MapInboundClaims = false` — claims keep their original JWT names, so the code reads
  `sub` and `name` rather than the long WS-Federation URIs.
- `NameClaimType = "name"`, which is what `UserLoadingMiddleware` uses as the display name.

The Swagger UI in Development is wired for the authorization-code + PKCE flow against the
same tenant, using a separate `SwaggerAuthentication:ClientId`.

### User provisioning

`Api/Middleware/UserLoading/UserLoadingMiddleware.cs` runs after authorization on every
request:

1. No authenticated identity or no `sub` claim → pass through untouched.
2. User row exists → re-read the `name` claim, and write if it changed or if `LastSeen` is
   more than an hour stale.
3. User row does not exist → provision it from `sub` + the `name` claim.

There is no signup endpoint; **first authenticated request is the signup**.

The display name is stored verbatim, with `"Unnamed user"` standing in for a missing or blank
claim. Nothing resolves it against other users, because nothing needs it to be unique: the
identity is `sub`, and the only lookup anybody does is by invite code. That is also why it is
re-read on every request rather than frozen at provisioning — a rename at the identity provider
propagates here, and there is no other user's name it could be in the way of.

Rows still carrying a `" (2)"` suffix from the era when the name *was* unique repair themselves
on their owner's next request. The migration deliberately does not rewrite them: it could not
tell a resolved collision from somebody whose name genuinely ends that way.

Nothing here can reach another user's row: the insert carries this subject as its key, and a
subject that turns out to have been provisioned by a concurrent request is detached rather than
inserted twice.

## Authorization

`Application/Authorization/`, and it is the only mechanism. Scope policies asserting a `scope`
claim used to exist unreferenced beside it; they were deleted rather than wired up, because no
token anywhere carries those scopes and activating them would have denied every request.

Every endpoint that touches a repo starts the same way:

```csharp
var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
    .CheckIsAllowedTo(x => x
        .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
    .MapToForbidden();
if (authResult is not null)
{
    return authResult;
}
```

`CheckIsAllowedTo` loads the user (whose memberships are auto-included), runs the checks,
and returns `null` on success or an `AuthorizationResult` on the first failure. The builder
short-circuits: once `Result` is set, later checks are no-ops, so the caller sees the first
thing that went wrong.

Four checks exist:

| Check | Meaning |
| --- | --- |
| `AccessRepoAtLevel(repoId, level)` | Caller's membership in that repo is at least `level` |
| `CreateRepo()` | Caller carries `User.IsTrusted`, the manually granted flag |
| `GrantAccessToRepo(repoId, level)` | Caller may hand out `level` — currently identical logic: you must hold at least the level you are granting |
| `ChangeOthersMembership(subjectMembership)` | Modifying a Guest needs Member; modifying a Member or an Admin needs Admin |

`ChangeOthersMembership` is the interesting one — the level you need depends on the level of
the person you are acting on, which is what stops a Member from kicking an Admin. It also cannot
run first, since it needs the subject's level to know what it needs; the two membership
endpoints therefore authorize to the floor that check can never fall below *before* loading
anything, so a non-member learns nothing from the responses.

`CreateRepo()` exists so that repo creation refuses in the same shape, at the same status, with
the same problem body as everything else. `POST repos/check-name-taken` is gated on it too:
repo names are unique system-wide, so answering it for anyone signed in would be an existence
oracle over every repo name there is, and its only purpose is naming a repo you are about to
create.

### Two statuses, and which is which

**Authorization failures are `403`**, mapped by `MapToForbidden`. Always 403, never 401: every
endpoint group requires authentication, so a request that reaches a handler has already
established who it is and an `AuthorizationResult` can only mean it may not do this.

The caller the server cannot *identify* — a token with no usable `sub`, or a subject with no
user row — is a `401`, and it is produced centrally by `NotAuthenticatedMiddleware` catching
`NotAuthenticatedException`. Centrally, because it is thrown from inside `CheckIsAllowedTo` and
`GetUserId`, below the handler where there is no result to return, and because the endpoints
most able to raise it (`GET users`, `GET repos`) return a bare `Ok<T>` with no `Results<...>`
union to put it in. The 401 is declared once on the endpoint group for the same reason. Until
the middleware existed, every one of those cases answered 500.

### Error responses

`Api/ErrorHandling/Problems.cs` is a catalogue of RFC 7807-shaped problems. Each has a
`ProblemType` enum member carrying a stable URI
(`https://server.modsdude.com/api/problems/name-taken` and friends), so a client can switch
on the type rather than parse prose. `Problems.NotFound.With(x => x.Detail = "...")` lets an
endpoint specialise the detail without a new catalogue entry.

**Every member carries that URI twice, and both attributes are load-bearing:**

```csharp
[EnumMember(Value = _typeBaseUri + "name-taken")]        // NJsonSchema → OpenAPI → generated client
[JsonStringEnumMemberName(_typeBaseUri + "name-taken")]  // System.Text.Json → the wire
NameTaken,
```

`System.Text.Json` does not honour `[EnumMember]` — only `[JsonStringEnumMemberName]`. With
just the first, the OpenAPI document advertises URIs while the server sends bare member names,
and no generated client can match the two. Adding a problem type means adding both attributes.

The client branches on `CustomProblemDetails.Type`, so a newly added problem type stays
invisible to it until `Generated.cs` is regenerated — which is what the checked-in OpenAPI
document and its CI diff exist to notice. See [Regenerating the client](#regenerating-the-client).

## Persistence

`ApplicationDbContext` exposes `Users`, `Repos`, `RepoMemberships`, `RepoInvites`, `Profiles`,
`ProfileRevisions` and `ModVersions`, and implements `IUnitOfWork` (`CommitAsync` → `SaveChangesAsync`).

Notable configuration:

- **Composite keys everywhere.** `ModVersion` is `(RepoId, ModId, Id)`; `Profile` is
  `(RepoId, ProfileId)`; `ProfileRevision` is `(RepoId, ProfileId, Number)`; `RepoMembership` is
  `(UserId, RepoId)`. Repo scoping is baked into the primary key rather than being a filter
  you can forget.
- **`ModVersion` carries two indexes that are load-bearing rather than decorative.** The unique
  one on `(RepoId, ModId, SequenceNumber)` is what keeps ordering contiguous — and what makes a
  move a two-write operation, since a rotation cannot pass through it in any row order; see
  [02 — Domain model](02-domain-model.md#a-move-is-a-rotation-and-a-rotation-cannot-be-renumbered-in-place).
  The other, `(RepoId, Updated, ModId, Id)`, backs the mod list's delta form, which orders by
  `Updated` inside a repo and resumes from a timestamp.
- **`ModVersion.Attributes` and `ModVersion.Images` are owned collections**, so they are
  materialised whenever a `ModVersion` entity is.
- **`ModDependency` is an owned collection of `ProfileRevision`**, keyed
  `(RepoId, ProfileId, RevisionNumber, ModId, ModVersionId)`, with an FK to `ModVersion`. The FK
  is **`Restrict`**, not the cascade EF would infer: deleting a version a revision pins would
  otherwise rewrite history behind everyone's back, which the delete endpoints refuse. Restrict
  makes the database enforce the same rule, so a dependency added between an endpoint's check and
  its commit fails loudly rather than being swept away. See
  [02 — Domain model](02-domain-model.md#a-pinned-version-cannot-be-deleted-any-more) for what
  that costs now that history holds every version a profile has ever pinned.

  Three indexes on it, all load-bearing. The unique one on
  `(RepoId, ProfileId, RevisionNumber, ModId)` backs the one-version-per-mod rule — **per
  revision**, which is what lets a profile pin a version one of its earlier revisions already
  used, i.e. every rollback. `(RepoId, ModId, ModVersionId)` is the FK's own index, and answers
  "does any revision anywhere in this repo still pin this version?" without scanning profiles
  times revisions times thousands of mods.

  Two consequences worth knowing before touching anything that loads a revision:

  - `ModDependency.ModVersion` is **not** auto-included, and building a revision reads the mod's
    identity off it. `ProfileRevisionWrites.ResolveAsync` is the one place versions are loaded
    as entities, once per save.
  - Because the collection is *owned*, it is materialised whenever a `ProfileRevision` entity is,
    wanted or not — thousands of rows per revision, times however many revisions were loaded.
    **Nothing but a save materialises one.** `ProfileRevisionExtensions` and
    `ProfileRevisionReads` project, and `Profile` has no navigation to its revisions at all, so a
    profile load cannot drag a history in with it.
- **`ProfileRevision` is keyed `(RepoId, ProfileId, Number)`**, with a Cascade FK to `Profile`
  so a deleted profile takes its history with it. Its `Changes` is an EF complex property, three
  int columns on the revision's own row; `Origin` is stored as its name rather than an ordinal.
  The primary key is also the concurrency control: two saves based on the same head compute the
  same next number and exactly one commits.
- **`Repo._memberships` is mapped through the private backing field**, with a runtime guard
  that throws at model-build time if the field is renamed, so EF cannot silently fall back
  to a shadow property.
- `Repo.AdapterData` is an EF **complex property**, flattened into the repo row.
- Auto-included navigations: `Repo._memberships`, `User.RepoMemberships`.
  These make the authorization pattern above a single round trip, at the cost of always
  paying for them.
- Entity extension methods in `Persistence/Extensions/EntityExtensions/` provide the small
  query vocabulary the endpoints use — `GetAsync`, `GetVersionsOfModAsync`, `GetVersionsAsync`,
  `GetLatestVersionOfEachAsync`, `GetPinsAsync`, `GetDependencyRowsAsync`, `GetHistoryAsync`,
  `GetRowAsync`, `GetModUsageAsync`, `GetDisplayNamesAsync`, `CheckNameIsTaken`, `GetByCodeAsync`.

  **Every query lives here, including the ones only one endpoint issues.** A LINQ expression a
  provider cannot translate is a runtime failure on a page rather than a build error, and the
  persistence suite is the only thing in the tree that runs one against a real PostgreSQL — so a
  query written next to its endpoint instead is a query nothing can cover.

Six migrations: `20250717173302_MoveToPostgres`, which squashes the pre-Postgres history, then
`FlattenModModel`, `ModImageReferencesAndModListDelta`, `ModImageRendition`,
`DisplayNamesAndRepoInvites` and `ProfileRevisions`.

`ProfileRevisions` is the one migration here that carries hand-written SQL. EF's generated form
adds the columns with zero defaults, which would leave every existing dependency row pointing at
a revision that does not exist and the new foreign key refusing it; the added statements give
each existing profile a revision 1 holding exactly what it holds now. Their author is recorded as
`unknown` rather than invented — those lists were assembled before anything recorded who was
assembling them, and no user id would be true.

## Tests

`ModsDude.Server.Domain.Tests` is plain xUnit over the entities — version sequencing, membership
transitions, the revision rules and what a save counts as a change — with named regressions for
the sequencing bugs listed in
[PLAN.md](PLAN.md#phase-0--unblock).

`ModsDude.Server.Persistence.Tests` runs against a **real PostgreSQL**, migrated from the same
migrations the API runs, because it covers behaviour the database decides rather than the model:
that the shift-on-insert renumber is collision-free only because EF orders those updates from
the unique index declared in the model, that a move cannot be a single renumber, and that a
profile revision answers with what it pinned rather than with what the profile pins now. An
in-memory or SQLite substitute would answer for itself instead of for PostgreSQL, which is the
whole point.

`DatabaseFixture` **drops and recreates** whatever database it is pointed at before every run,
so it targets one of its own. Point it elsewhere with `MODSDUDE_TEST_DATABASE`; the default is
`Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=modsdude-tests`.

## Storage

`ModsDude.Server.Storage` registers a `BlobServiceClient` for
`https://{account}.blob.core.windows.net` using `Azure.Identity` default credentials — no
connection string, no account key.

Mod files live in the `mods` container at:

```
{repoId}/{modId}/{versionId}
```

`ModStorageService`:

- `CheckIfModExists` — a blob existence check.
- `GetUploadLink` — mints a **user-delegation SAS** with `Create | Write` permission, valid
  for 30 minutes, scoped to that one blob. `Write` is what lets the client stamp the content
  hash into blob metadata as it uploads.
- `GetDownloadLink` — the same with `Read`. Guest-level, because a Guest who can see a profile
  must be able to apply it.
- `GetRecordedContentHash` — reads back the `sha256` metadata entry the upload wrote. Azure's
  built-in content hash is MD5, so the SHA-256 has to be recorded explicitly; without it,
  adopting an orphaned blob would register a digest describing bytes nobody has, which no
  download can satisfy and no upload link can repair, since the blob's existence means no link
  can be minted for it again.
- `DeleteMod`, `ListStoredMods`, `DeleteStoredBlob` — for the delete endpoints and the
  reclamation sweep.

The user-delegation key is derived from the server's own managed identity, so the SAS
inherits the server's permissions and can be revoked centrally. **The API never handles mod
bytes.** The client uploads and downloads straight to blob storage.

### Image blobs

`ModImageStorageService` holds derivative images in a second container, `mod-images`, at
`{hash[0..2]}/{hash}`. The address carries no repo and cannot: content addressing is what makes
dedupe across versions, mods and repos work at all. These the API *does* handle bytes for —
they are small and fetched in bulk, which inverts the trade-off that sends mod files over a SAS.
See [09 — Mod catalog](09-mod-catalog.md#serving-them-back).

Blob storage has no batch existence call, so `CheckWhichExist` is a bounded parallel fan-out; the
batch is a batch to the *client*, which is where the round trips that matter are.

`Program.cs` calls `EnsureContainerExists` once at startup, beside the migration. Unlike the
migration it is **not** fatal — the API serves every metadata route without it, and a storage
account that is briefly unreachable is no reason to refuse to start — but it is logged as an error,
because uploads fail until it succeeds and [the client absorbs those
failures](05-client.md#absorbed-is-not-hidden) by design. Without it, a fresh storage account
presents as imagery that silently never appears. The `mods` container is not created this way:
mod files go over a SAS, and the container predates all of this.

### Savegame blobs

`SavegameStorageService` holds packed savegames in a third container, `savegames`, at:

```
{repoId}/{savegameId}/{contentHash}
```

Mechanically the same as `ModStorageService` — user-delegation SAS, 30-minute lifetime, `sha256`
stamped into metadata as the client uploads, and the API never touching the bytes. Upload is
Member-level and download is Guest-level, because a Guest is offered *Take a copy*.

**The one deliberate difference is the address.** A savegame's blob is named by its content rather
than by its version number, and that is what makes concurrent check-ins safe: numbering the blob
would have two people mint upload links for the same name, so whichever wrote second would replace
the other's bytes — and the stale-base check that decides who takes the head runs after that, by
which point the loser's save is gone. Content addressing also makes a restore a metadata operation
with no blob copy, and a duplicate check-in free.

Two consequences follow. A blob already at the requested address holds *the bytes being offered*,
so `createSavegameUploadLink` reports it as `AlreadyStored` and the client skips to checking in —
where the mod path has to refuse the same situation as an identity collision. And **several
versions can share one blob**, so the reclamation sweep asks whether an address is still referred
to, not whether a version still exists.

### Blob reclamation

`BlobReclamationService` is a hosted service sweeping orphaned blobs — import orphans, and the
residue of deleted versions and repos. Two rules make it safe:

- **List blobs before reading registrations, never the reverse.** A registration written between
  the two reads is then already covered by a blob the listing had.
- **Ignore anything younger than a grace period** well past the upload SAS lifetime. An import
  uploads and then registers; a sweep that did not wait would delete the bytes in between.

A blob name that does not parse is reported, never deleted.

## Endpoint reference

All routes are prefixed `api/v1`. All require authentication. "Level" is the repo membership
level required.

### Users

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `users` | — | Every user who shares at least one repo with the caller, **excluding the caller** |
| GET | `users/me` | — | The caller's own `CurrentUserDto` — `UserDto` plus `IsTrusted`. The only route that returns either: `users` deliberately leaves the caller out, the client cannot derive its `Tag` from the token, and whether somebody may create repos is not their teammates' business |

There is **no user search**. Looking somebody up by name would make every guessable name
reachable by a stranger and let a person be added to a repo without agreeing to it; joining goes
through an invite instead.

### Repos

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `repos` | — | The caller's repos with their membership level, ordered by name |
| POST | `repos/create` | — | Requires `User.IsTrusted`. Creator becomes Admin |
| POST | `repos/check-name-taken` | — | No repo-scoped check; any authenticated user can probe any name |
| GET | `repo/{repoId}` | Member | Repo details including the member list |
| PUT | `repo/{repoId}` | Admin | Rename and/or replace adapter configuration |
| DELETE | `repo/{repoId}` | Admin | Refuses with `repo-not-empty` while the repo has mods |

Note the inconsistency: the collection is `repos`, the single resource is `repo`.

### Members

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| PUT | `repos/{repoId}/members/{userId}` | `ChangeOthersMembership` + may grant the new level | |
| DELETE | `repos/{repoId}/members/{userId}` | `ChangeOthersMembership` | Refuses the last Admin |

There is no route that adds a member. A membership is created by its own owner, by redeeming an
invite.

### Invites

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `repos/{repoId}/invites` | Member | Every invite the repo has ever had, each with its `Status` and join count |
| POST | `repos/{repoId}/invites` | Member + may grant the requested level | `{ membershipLevel, maximumUses?, expiresAt? }`. Both limits optional and independent |
| DELETE | `repos/{repoId}/invites/{inviteId}` | Member | Revokes for good. An invite belonging to another repo is reported as absent |
| POST | `invites/redeem` | — | `{ code }`. Joins the caller to whichever repo the code belongs to, and returns that `RepoMembershipDto` |

`invites/redeem` takes the code in the body rather than the path, because a path is written down
by every proxy and access log between the client and here. Redeeming a code for a repo the caller
is already in is not an error and does not spend a use — the membership they already had is
returned. Anything else that is not `Active` comes back as `invite-not-usable`, and a race with
another redemption as `invite-redemption-conflict`.

Any Member may revoke any of the repo's invites, including one an Admin made: revoking only ever
takes access away, and a loose code wants stopping by whoever notices it.

### Profiles

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `repos/{repoId}/profiles` | Guest | Each carries `HeadRevision` |
| GET | `repos/{repoId}/profile/{profileId}` | Guest | |
| POST | `repos/{repoId}/profiles` | Member | `CopyFrom` branches a revision of another profile off into this one |
| PUT | `repos/{repoId}/profiles/{profileId}` | Member | Rename |
| DELETE | `repos/{repoId}/profiles/{profileId}` | Member | Takes the whole history with it |
| GET | `repos/{repoId}/profiles/{profileId}/revisions` | Guest | The history, newest first, windowed by `skip`/`limit` |
| PUT | `repos/{repoId}/profiles/{profileId}/revisions` | Member | **Saves the mod list.** The whole list, based on a revision number |
| POST | `repos/{repoId}/profiles/{profileId}/revisions/{number}/restore` | Member | Copies an older revision forward as a new one |

Same singular/plural inconsistency on the single-profile GET.

**The save is a `PUT` of the whole list, not a patch.** A revision is a snapshot, so the request
carries every pin and the server records exactly that; anything absent is removed. The client
already has the whole list in hand — it is the thing on screen — and one request of two thousand
pins beats two thousand requests by a margin that needs no arguing.

`BasedOn` names the revision the list was built from. A save whose `BasedOn` is no longer the
head is refused with `profile-revision-stale`, carrying what the head is now, so a member
editing a stale copy is told rather than silently overwriting somebody. **A save that changes
nothing mints nothing** and answers with the head — opening a profile, looking at it and
pressing Save is not an event, and a history that recorded it would bury the events that are.

There is **no route that names a revision to write to**. Restore is the only thing that reaches
an old one, and it reads: it produces a new revision at the front rather than reopening
anything.

Restore is Member, like any other save. It discards nothing, and the history makes it visible
and reversible — which is a better guarantee than a permission level. Reading a history is Guest,
because somebody who syncs a profile without curating it is exactly the person who needs to know
what changed under them.

### Mod dependencies

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `repos/{repoId}/profiles/{profileId}/modDependencies` | Guest | `?revision=N` for an older one, omitted for the current list. Each dependency carries `ContentHash`, so sync never has to pull the mod list to resolve it |

**There is only one, and it reads.** A profile's mod list is written through
`PUT repos/{repoId}/profiles/{profileId}/revisions`, which addresses the profile and always
means its head — see [Profiles](#profiles) above. That is what makes an old revision read-only
without a flag anybody has to check: nothing can address one to write to it.

The response says which revision answered, and whether that is the head. A client saving
afterwards has to name what it was working from, and taking that number out of the same response
it read the list from is the only form of it that cannot already be stale by the time it is
used.

Four routes are **gone** — `POST .../modDependencies`, `PUT` and `DELETE` on
`.../modDependencies/{modId}`, and `POST .../modDependencies/upgrade`. With them, the server
could not see a save at all: it saw a stream of per-mod writes, so every toggled lock would have
been a revision of its own. The batch upgrade went with them — the client already computes what
an update would be, and a whole-list save expresses "and these are now the newer versions"
without a second endpoint that can only express one shape of change.

### Mods

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `repos/{repoId}/mods` | Guest | Paginated **and** delta. See below |
| GET | `repos/{repoId}/mods/usage` | Guest | Which registered versions the repo's profiles pin, and how many. Paginated |
| GET | `repos/{repoId}/mods/{modId}/versions` | Guest | One mod's versions, oldest first. Unpaged deliberately — bounded by how many releases one mod has had, not by the repo |
| POST | `repos/{repoId}/mods` | Member | Register a version. Verifies the blob exists first, and asserts the placement |
| PUT | `repos/{repoId}/mods/{modId}/versions/{versionId}/placement` | Member | Move an already-registered version. Returns the resulting order |
| PUT | `repos/{repoId}/mods/{modId}/versions/{versionId}/images` | Member | Replace a version's image references |
| DELETE | `repos/{repoId}/mods/{modId}/versions/{versionId}` | Member | Deletes the blob too. Refuses the last version, and one a profile pins |
| DELETE | `repos/{repoId}/mods/{modId}` | Member | The whole mod, blobs included. Refuses if a profile pins any of its versions |

`GET repos/{repoId}/mods` returns **one entry per version, with no parent** — nesting would only
make the client re-group on receipt. It takes `updatedAfter`, `cursor` and `limit` (default 100,
maximum 500) and answers with a `NextCursor` that is `null` once the listing is exhausted.

Two properties of it are worth knowing before relying on it:

- **The cursor is a timestamp plus a count**, not a keyset tuple, because the ids are value
  objects and a provider cannot translate a comparison on one. Ordering by `Updated` also gives
  the delta the property it needs: a row written during a listing gets a newer `Updated` and
  moves ahead of the cursor, so it may be seen twice and can never be skipped.
- **A delta reports what changed, never what was deleted.** A client that has to notice removals
  refetches without `updatedAfter`.

`GET repos/{repoId}/mods/usage` is a resource of its own rather than a field on `ModDto`, and
the reason is the delta form above: usage changes when a *profile* is edited, not when a version
is. Carrying it on the version would mean either serving stale usage to every client that syncs
incrementally, or restamping `Updated` on every version a profile save touches — two thousand
rows a save, and a delta the size of a full listing. Two facts with different lifetimes, so two
resources. The response is sparse: a version that does not appear is unused, but **only once the
whole listing has been read**, so a client must exhaust the cursor before treating an absence as
an answer. It is advisory; the delete endpoints re-ask the database when it matters.

### Savegames

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `repos/{repoId}/savegames` | Guest | Each carries its head version and its open claim inline. Four queries flat, not one per row |
| POST | `repos/{repoId}/savegames` | Member | **Publish.** Creates the savegame, its version 1, and a claim for the publisher |
| PUT | `repos/{repoId}/savegames/{savegameId}` | Member | Rename, or move to another profile |
| DELETE | `repos/{repoId}/savegames/{savegameId}` | Member | Takes its versions and its claims with it |
| GET | `repos/{repoId}/savegames/{savegameId}/versions` | Guest | The history, newest first, windowed by `skip`/`limit` |
| PUT | `repos/{repoId}/savegames/{savegameId}/versions` | Member | **Check in.** Based on a version number, forcible |
| POST | `.../versions/{number}/restore` | Member | Copies an older version forward as a new one |
| GET | `repos/{repoId}/savegames/{savegameId}/checkouts` | Guest | The claim log, newest first, windowed |
| POST | `repos/{repoId}/savegames/{savegameId}/checkouts` | Member | Take the claim, or renew your own. Answers with who it was taken from |
| DELETE | `.../checkouts/current` | Member | **Discard** — give it back unplayed. Mints no version |

**The client mints the savegame id**, as it does a repo id. The blob lives at
`{repoId}/{savegameId}/{contentHash}`, so a server-minted id would name a blob nobody could have
uploaded to: mint a GUID, upload, publish with it.

`BasedOn` names the version the check-in was built on, and a stale one is refused with
`savegame-version-stale` carrying the head. **Forcing past it is allowed** and records the fork as
`Origin = Forced` with the version actually played, rather than hiding it — the claim is the social
guard and this is the mechanical one. **A check-in whose hash equals the head's mints nothing** and
answers with the head; a night that changed nothing is not an event. It still ends the caller's
claim, because they pressed check in and should not be left holding a save they handed back.

Taking a claim somebody else holds is Member, deliberately: the design's whole position on conflict
is that a claim is advisory and a take-over is recorded rather than prevented. **Discard is
holder-only** — `Discarded` means "the holder gave it back unplayed", and letting a third party
write that would put a sentence in the log its subject never said. Taking a save is what the
check-out route is for, and it records `TakenOver`.

Checking in and restoring both prune the history afterwards, in a separate commit so a failed prune
cannot cost somebody their play. Pruning deletes **rows only**; the blobs fall out on the next
reclamation pass, which is what makes it safe when two versions name one address.

Reading is Guest throughout, including the claim log: somebody who plays a shared save without
curating it is exactly the person who needs to see who has had it.

### Files

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| POST | `files/createModUploadLink` | Member | 30-minute `Create\|Write` SAS, plus the metadata key to write the SHA-256 into. Refuses with `already-registered` or `file-already-present` |
| POST | `files/createModDownloadLink` | Guest | 30-minute `Read` SAS |
| POST | `files/createSavegameUploadLink` | Member | The same, addressed by content hash. Answers `AlreadyStored` instead of refusing |
| POST | `files/createSavegameDownloadLink` | Guest | 30-minute `Read` SAS. Refuses with `file-not-found` |

**A savegame upload link reports an occupied address as a success**, where the mod one refuses it.
A mod blob is addressed by the version it belongs to, so a blob already there holds *somebody
else's* bytes under this id — a collision to report before anything registers over it. A savegame
blob is addressed by its own content, so a blob already there holds precisely the bytes being
offered, and there is nothing left to do with them. That is what makes a night that changed
nothing, and a restore, cost no upload at all. Both savegame routes refuse a `ContentHash` that is
not a lowercase hex SHA-256, because it becomes a blob path segment and there is no global
exception handler to turn the storage layer's own refusal into anything but a 500.

The two mod upload-link refusals are **distinct problem types on purpose.** There is nothing left to
do for a registered version, while an unregistered blob is the orphan a failed import left behind
and is finished by registering without re-uploading. Answering both with one problem made a
failed import unretryable. `file-already-present` carries the blob's recorded hash — matching it
means this is the client's own orphan, differing means an id/version collision to report rather
than register over, and `null` means the blob predates the metadata and nothing has been
established.

### Images

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `images/{hash}` | Authenticated | Streams the blob, `immutable` and cacheable for a year, with the hash as its entity tag |
| POST | `images/{hash}` | Authenticated | One derivative, as a form file. **Refused unless the bytes hash to the address** |
| POST | `images/checkExisting` | Authenticated | "Which of these do you already have?", up to 1,000 hashes |

**"Authenticated" is not an oversight.** The route carries no `repoId` and cannot — content
addressing is what makes the dedupe work, and it leaves no repo in the address to scope against.
It is a real widening compared to the rest of the server, stated rather than hidden behind a
Guest label that would imply a scoping the route does not have. What is behind an address is mod
store art, already public on the sites the mods come from, and it reveals nothing about who is in
which repo. See [09 — Mod catalog](09-mod-catalog.md#what-authorized-means-for-a-global-address).

## Configuration

`appsettings.json` on the server:

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Database` | PostgreSQL connection string |
| `Storage:StorageAccountName` | Azure Storage account name; the URL is derived |
| `EntraExternalId:*` | Instance, Domain, ClientId, Audience, Authority, and the token/authorization endpoints used by the Swagger UI |
| `SwaggerAuthentication:ClientId` | Separate app registration for the Swagger UI |
| `BlobReclamation:*` | `Enabled`, `Interval`, and `MinimumBlobAge` — the grace period an unreferenced blob must survive before the sweep may delete it |

## Running locally

```bash
dotnet run --project ModsDude.Server/ModsDude.Server.Api
```

Requires a PostgreSQL instance matching `appsettings.Development.json`
(`localhost:5432`, database `modsdude-dev`) and credentials with access to the
`modsdudedev` storage account. Migrations apply on startup. Swagger UI is served in
Development only.

### Regenerating the client

The typed client is generated from the **running** API:

1. Start the API (it must be reachable at `http://localhost:5267`).
2. Run the NSwag configuration at `ModsDude.Client/ModsDude.Client.Core/nswag-config.nswag`.

Output goes to `ModsDude.Client.Core/ModsDudeServer/Generated.cs`. The generated clients
derive from `ModsDudeClientBase`, which attaches the bearer token by calling
`IAccessTokenAccessor.Get` for every request. Each generated client also hardcodes a localhost
`BaseUrl`; since the file is regenerated wholesale, the configured one is applied in
`AddModsDudeClient` instead of being edited in.

**Then update the checked-in OpenAPI document.** `openapi/v1.json` exists so that a server change
the generated client has not caught up with shows as a diff rather than as nothing at all:

```bash
pwsh scripts/openapi.ps1 -Update     # rewrite it
pwsh scripts/openapi.ps1             # verify it — what CI runs
```

The script builds and starts the API itself (it migrates a database at startup, so it needs a
connection string), fetches the document, and rewrites it into a canonical form — keys in ordinal
order, two-space indentation, LF, no BOM — so the file records what the API says rather than
which machine asked it. Commit it alongside the change.

Note the limit of this check: it fails when the *document* is behind the server, which is the
only warning anyone gets that `Generated.cs` is behind too. Nothing compares the generated client
against the document.
