# Server

## Project layout

```
ModsDude.Server.Api          ASP.NET Core host — endpoints, DTOs, middleware, problem details
ModsDude.Server.Application  Authorization primitives, ITimeService, IUnitOfWork, the storage abstractions
ModsDude.Server.Domain       Entities and invariants. No framework references
ModsDude.Server.Persistence  EF Core / PostgreSQL — DbContext, entity configuration, migrations
ModsDude.Server.Storage      Azure Blob Storage — SAS issuance, image blobs
ModsDude.Server.ModHub       Reading Farming Simulator's ModHub website — the paced client and its parser

ModsDude.Server.Domain.Tests       xUnit over the domain. No infrastructure
ModsDude.Server.Persistence.Tests  xUnit over a real PostgreSQL. See Tests, below
ModsDude.Server.ModHub.Tests       xUnit over pages saved from ModHub. Never reaches the site
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

Every background job runs in [Hangfire](https://www.hangfire.io/) and is registered after the
migration: the two retention jobs (see [Retention](#retention)), blob reclamation (see
[Blob reclamation](#blob-reclamation)) and the ModHub crawl (see [The ModHub crawler](#the-modhub-crawler)).
Registering is idempotent, and a job that is disabled in configuration is removed rather than left
behind. The PostgreSQL storage uses a sliding invisibility timeout, because a ModHub backfill runs for
hours and a fixed timeout would hand it to a second worker after thirty minutes.

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
the same problem body as everything else. It gates one endpoint now: `POST repos/check-name-taken`
is gone along with the uniqueness it answered for, which also retires the existence oracle it was
— an authenticated stranger could probe any repo name on the server, and the gate was the mitigation.

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
- `GetModSize` — the stored blob's length, or `null` when there is no blob. Read by registration,
  which is what keeps `ModVersion.SizeBytes` a fact about the bytes rather than a claim about them.
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

`Program.cs` ensures **every** container it writes to exists at startup, beside the migration - `mods`, `mod-images` and `savegames`, each independently so one failure does not stop the others. Unlike the
migration it is **not** fatal — the API serves every metadata route without it, and a storage
account that is briefly unreachable is no reason to refuse to start — but it is logged as an error,
because uploads fail until it succeeds and [the client absorbs those
failures](05-client.md#absorbed-is-not-hidden) by design. Without it, a fresh storage account
presents as a feature that silently never works — which is exactly how the `savegames` container
first announced itself. Going over a SAS is no exemption: the client cannot create what it is
handed a link into, so every container this server addresses is ensured here.

### Savegame blobs

`SavegameStorageService` holds packed savegames in a third container, `savegames`, at:

```
{repoId}/{savegameId}/{contentHash}
```

Mechanically the same as `ModStorageService` — user-delegation SAS, 30-minute lifetime, `sha256`
stamped into metadata as the client uploads, and the API never touching the bytes. Upload is
Member-level and download is Guest-level, because a Guest is offered *Take a copy*.

**The one deliberate difference is the address.** A savegame's blob is named by its content rather
than by its snapshot number, and that is what makes concurrent check-ins safe: numbering the blob
would have two people mint upload links for the same name, so whichever wrote second would replace
the other's bytes — and the stale-base check that decides who takes the head runs after that, by
which point the loser's save is gone. Content addressing also makes a restore a metadata operation
with no blob copy, and a duplicate check-in free.

Two consequences follow. A blob already at the requested address holds *the bytes being offered*,
so `createSavegameUploadLink` reports it as `AlreadyStored` and the client skips to checking in —
where the mod path has to refuse the same situation as an identity collision. And **several
snapshots can share one blob**, so the reclamation sweep asks whether an address is still referred
to, not whether a snapshot still exists.

### Blob reclamation

`BlobReclamationJob` is a daily Hangfire job (`BlobReclamation:Cron`, 04:00 UTC) sweeping orphaned
blobs — import orphans, and the residue of deleted versions and repos. Not retried, and **a missed
occurrence is skipped rather than caught up at startup**: the sweep only runs at its scheduled time,
so a crash loop cannot become a delete loop. Four rules make it safe:

- **List blobs before reading registrations, never the reverse.** A registration written between
  the two reads is then already covered by a blob the listing had.
- **Ignore anything younger than a grace period** well past the upload SAS lifetime. An import
  uploads and then registers; a sweep that did not wait would delete the bytes in between.
- **A blob name that does not parse is reported, never deleted.**
- **A sweep that would empty most of a container deletes nothing anywhere.** Measured against a
  database that is not the one the storage account belongs to — a reset dev database pointed at the
  shared `modsdudedev` account, say — every blob reads as an orphan. So all three containers are
  planned before any is touched, and if one would lose more than `MaxReclaimableShare` (half) of
  its blobs, and at least 20 of them, the job logs an error per container and fails. A genuine
  large clean-up, such as the residue of a deleted repo, goes through by raising the share for one run.

### Retention

Two daily [Hangfire](https://www.hangfire.io/) jobs, storing their state in the application's
database under a `hangfire` schema of their own (outside the EF migrations). What they decide is
`RetentionPolicy`, described in [02 — Retention](02-domain-model.md#retention);
`Persistence/Retention/RetentionSweeper.cs` reads the histories and applies it.

| Job | When (`Europe/Stockholm`) | Does |
| --- | --- | --- |
| `retention-schedule` | 05:00 | Dates every newly eligible row (today + grace), redates a row whose reason changed, clears a row no longer eligible, and leaves the rest alone |
| `retention-delete` | 06:00 | Deletes rows dated today or earlier **that are still eligible for the reason they were dated for** |

- **Deletion asks the policy again.** A schedule is only cleared eventually, and a deletion cannot be
  taken back, so the date alone never decides.
- **Deletion clears stale dates first.** Before deciding what is due, it clears every date whose
  reason no longer holds. Otherwise tonight's deletions could shrink a history back into a stale
  date's reason, and a rerun would act on it. With this, **both jobs are idempotent**, and
  `RetentionUpkeep` only affects what the dates look like - a missed or failed clearing never
  deletes anything.
- **Rows only.** Snapshot and mod-version blobs fall out on the next reclamation pass. A deleted mod
  version closes its gap in `SequenceNumber`, one at a time, exactly as the delete endpoint does.
- **Repo by repo**, and a failure in one - a foreign key refusing a row something started holding
  between the read and the delete, say - is logged and does not stop the rest.
- **Not retried, never concurrent.** A failed run is tomorrow's to finish; retrying an hour later
  would move the time of day a deletion date means.

**Dates disappear as soon as a row stops being eligible.** `RetentionUpkeep` re-evaluates one history
after the writes that can make a row ineligible, and clears stale schedules - it never makes new ones:

| After | Re-evaluates |
| --- | --- |
| Check-in, restore snapshot, publish | The savegame, and its profile (the revision is now held) |
| Delete snapshot | The savegame |
| Save / restore revision | The profile, and the mods the new revision pins that have a version dated |
| Create profile (incl. copy) | The mods its first revision pins that have a version dated |
| Prune revisions | The profile |
| Register, move or delete a mod version | The mod |

A mod version's schedule moves its `Updated`, so the delta form of `GET .../mods` carries the date to
clients that already hold the version.

The dashboard is at `/hangfire`, behind basic auth from `HangfireDashboard:Username` and `:Password`.
It is not mapped at all while either is empty, and the committed settings leave the password empty -
set it through user secrets or `HangfireDashboard__Password`.

### Mod sizes

`ModVersion.SizeBytes` is what lets a client say what an apply will download before it starts, and
what the Mods page adds up into a repo's size. It is read from the blob's properties by
`RegisterModV1Endpoint` - the API never sees the bytes, so nothing the client says about them can be
checked - and it travels on `ModDto` and on `ModDependencyDto`, the latter because sync reads a
profile's dependencies and nothing else.

It is required. Versions registered before it existed were filled in by a one-off backfill, removed once
it had run; the migration that made the column `NOT NULL` has no default, so a version still without a
size stops it rather than turning into a zero that reads as an empty file.

### The ModHub crawler

**The one place the server knows about a particular game.** Everything else about games lives in client
adapters; this exists so a client can ask "is there a newer version of this file on ModHub" without asking
ModHub itself, and so every member asking costs ModHub nothing. It is kept to its own project
(`ModsDude.Server.ModHub`), its own tables (`ModHubMods`, `ModHubCrawlStates`), `Api/ModHub/` and one
endpoint.

`ModHubCrawlJob` is a Hangfire recurring job on `ModHub:PollCron` (hourly, UTC), removed when the crawler is
disabled or has no games. A run that finds another one still processing - a backfill takes hours - skips
rather than waits: `DisableConcurrentExecution` cannot guard it, because the PostgreSQL storage gives up a
distributed lock after ten minutes. A shutdown mid-run is Hangfire's to requeue, and the requeued run resumes
from the persisted sweep page. Each run does up to three things per configured game:

- **Sweep** — read every page of ModHub's "latest" listing and fetch each mod page not yet stored. The first
  sweep is the backfill (about 280 listing pages and 6,700 mod pages for FS25, two hours at one request a
  second); after that one runs every `SweepInterval` to catch what polls missed. The next page is persisted
  after each one, so a restart resumes.
- **Poll** — "latest" is ordered by last activity, with a new or updated mod put back on top, so a poll reads
  down until the listing resumes the previous poll's order and fetches everything above that point.
  `ModHubListingChanges` in the domain decides where that is.
- **Refresh** — re-read the `RefreshPerPoll` mods read longest ago. This catches the one thing order cannot
  show (a mod updated again while already first) and is **how a removed mod is found**: ModHub answers a
  missing mod with a 200 and an error heading, and its row is deleted rather than marked.

**A page the parser does not recognise stops the run** and is logged as an error; nothing is concluded from
it. A redesigned site must show up in the log, never as a listing that ended early or every mod removed. A
single unreadable mod page is skipped with a warning, five in a row stop the run. Every request goes through
one paced singleton (`RequestDelay`, one second) with the `UserAgent` from configuration.

The site was tested at up to ~27 requests a second without any sign of throttling; the pace is politeness.

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
| GET | `repos` | — | The caller's live repos with their membership level, ordered by name |
| GET | `repos/archived` | — | The archived ones. A repo is archived for every member at once |
| POST | `repos/create` | — | Requires `User.IsTrusted`. Creator becomes Admin |
| GET | `repo/{repoId}` | Member | Repo details including the member list |
| PUT | `repo/{repoId}` | Admin | Rename and/or replace adapter configuration |
| POST | `repos/{repoId}/archive` | Admin | Puts it away. The only way a repo goes away |
| POST | `repos/{repoId}/restore` | Admin | Brings it back under its own name. No body: repo names are not unique, so nothing can have taken it |
| DELETE | `repo/{repoId}` | Admin | Permanent, and refused unless archived. Takes the whole catalog, every profile's history and every savegame with it |

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
| GET | `repos/{repoId}/invites` | Member | The repo's invite list - everything not dismissed - each with its `Status` and join count |
| POST | `repos/{repoId}/invites` | Member + may grant the requested level | `{ membershipLevel, maximumUses?, expiresAt? }`. Both limits optional and independent |
| DELETE | `repos/{repoId}/invites/{inviteId}` | Member | Takes it off the list, revoking it first if it still works. An invite belonging to another repo is reported as absent |
| POST | `invites/redeem` | — | `{ code }`. Joins the caller to whichever repo the code belongs to, and returns that `RepoMembershipDto` |

`invites/redeem` takes the code in the body rather than the path, because a path is written down
by every proxy and access log between the client and here. Redeeming a code for a repo the caller
is already in is not an error and does not spend a use — the membership they already had is
returned. Anything else that is not `Active` comes back as `invite-not-usable`, and a race with
another redemption as `invite-redemption-conflict`.

Any Member may revoke any of the repo's invites, including one an Admin made: revoking only ever
takes access away, and a loose code wants stopping by whoever notices it.

**One route, two gestures.** `DELETE` means "this should stop being on my screen", and which act
that needs is decided by the invite rather than by the caller: an `Active` one is revoked, which
dismisses it in the same stroke, and a dead one is only dismissed — so an exhausted code is not
recorded as something somebody chose to retire. `RepoInvite.Dismiss` refuses an active invite
outright, because hiding a code that still works would leave it live, in the world, and off the
only screen from which it could be revoked.

Nothing is deleted. The row keeps the count of who came in through the code and keeps the code
unusable; `GetForRepoAsync` filters on `DismissedAt` so nothing can accidentally serve one.
Dismissal is repo-level, like every other fact about an invite — the list is the same list for
everybody.

### Profiles

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `repos/{repoId}/profiles` | Guest | Each carries `HeadRevision` |
| GET | `repos/{repoId}/profile/{profileId}` | Guest | |
| POST | `repos/{repoId}/profiles` | Member | `CopyFrom` branches a revision of another profile off into this one |
| PUT | `repos/{repoId}/profiles/{profileId}` | Member | Rename |
| POST | `repos/{repoId}/profiles/{profileId}/archive` | Member | Puts it away. The only way a profile goes away |
| POST | `repos/{repoId}/profiles/{profileId}/restore` | Member | Brings it back. `{ name? }` resolves a clash |
| GET | `repos/{repoId}/profiles/archived` | Guest | What the repo's Archive page lists |
| DELETE | `repos/{repoId}/profiles/{profileId}` | **Admin** | Permanent, and refused unless archived. Takes the whole history with it |
| GET | `repos/{repoId}/profiles/{profileId}/revisions` | Guest | The history, newest first, windowed by `skip`/`limit` |
| PUT | `repos/{repoId}/profiles/{profileId}/revisions` | Member | **Saves the mod list.** The whole list, based on a revision number |
| POST | `repos/{repoId}/profiles/{profileId}/revisions/{number}/restore` | Member | Copies an older revision forward as a new one |
| POST | `repos/{repoId}/profiles/{profileId}/revisions/prune` | **Admin** | Deletes old revisions. Refuses the head and any revision a savegame was played on, naming which |
| GET | `repos/{repoId}/profiles/{profileId}/ignoredMods` | Guest | The mods the profile ignores. Not part of a revision - see [02 — Ignored mods](02-domain-model.md#ignored-mods) |
| PUT | `repos/{repoId}/profiles/{profileId}/ignoredMods` | Member | Replaces the ignored list (`{ modIds }`). Refused with `ignored-mod-pinned` where it overlaps what the head pins. Written after a save, not with it |

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
| GET | `repos/{repoId}/profiles/{profileId}/modDependencies` | Guest | `?revision=N` for an older one, omitted for the current list. Each dependency carries `ContentHash` and `SizeBytes`, so sync never has to pull the mod list to resolve it, and can say what an apply will download, and `Added` - when the mod arrived at that version - which the editor sorts by |

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
| GET | `repos/{repoId}/mods/usage` | Guest | Which registered versions the repo's profiles pin, and how many - profiles whose newest revision pins it, and profiles with an older revision that does. Paginated |
| GET | `repos/{repoId}/mods/{modId}/dependents` | Guest | Which profiles and revisions pin any version of a mod. Read after a delete is refused |
| GET | `repos/{repoId}/mods/{modId}/versions/{versionId}/dependents` | Guest | The same for one version |
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
| GET | `repos/{repoId}/savegames` | Guest | Each carries its head snapshot and its open claim inline. Four queries flat, not one per row. Each also carries `snapshotCount` and `totalSizeBytes` - how many snapshots it has and how many bytes storage holds for them (counted per blob: snapshots with the same content hash are one) - which the Saves page sums into a line under its title |
| POST | `repos/{repoId}/savegames` | Member | **Publish.** Creates the savegame, its snapshot 1, and a claim for the publisher. The profile is optional, and publishing to one supersedes whatever savegame it was following |
| PUT | `repos/{repoId}/savegames/{savegameId}` | Member | Rename. Nothing moves a savegame to another profile — see below |
| POST | `repos/{repoId}/savegames/{savegameId}/makeCurrent` | Member | Points the profile back at this past savegame, superseding whatever held the slot. Answers with both |
| POST | `repos/{repoId}/savegames/{savegameId}/archive` | Member | Puts it away, keeping its snapshots and its claim log |
| POST | `repos/{repoId}/savegames/{savegameId}/unarchive` | Member | Brings it back. Not "restore" - a savegame already has one, and it means putting an old *snapshot* back |
| GET | `repos/{repoId}/savegames/archived` | Guest | The archived half of the same list |
| DELETE | `repos/{repoId}/savegames/{savegameId}` | **Admin** | Permanent, and refused unless archived |
| DELETE | `repos/{repoId}/savegames/{savegameId}/snapshots/{number}` | **Admin** | Deletes one snapshot. Refuses the head. Rows only - the blobs go to the reclamation sweep |
| GET | `repos/{repoId}/savegames/{savegameId}/snapshots` | Guest | The history, newest first, windowed by `skip`/`limit` |
| PUT | `repos/{repoId}/savegames/{savegameId}/snapshots` | Member | **Check in.** Based on a snapshot number, forcible |
| POST | `.../snapshots/{number}/restore` | Member | Copies an older snapshot forward as a new one |
| GET | `repos/{repoId}/savegames/{savegameId}/checkouts` | Guest | The claim log, newest first, windowed |
| POST | `repos/{repoId}/savegames/{savegameId}/checkouts` | Member | Take the claim. Taking your own again changes nothing. Answers with who it was taken from |
| DELETE | `.../checkouts/current` | Member | **Discard** — give it back unplayed. Mints no snapshot |

**The client mints the savegame id**, as it does a repo id. The blob lives at
`{repoId}/{savegameId}/{contentHash}`, so a server-minted id would name a blob nobody could have
uploaded to: mint a GUID, upload, publish with it.

**A profile has at most one current savegame**, enforced by a filtered unique index on
`(RepoId, ProfileId) WHERE "SupersededAt" IS NULL`. Publish and `makeCurrent` are the same swap from
either end, and both write it as two commits inside one transaction: the incumbent leaves the slot
before anything takes it, or the index refuses the instant where two rows claim it. Nothing
supersedes a savegame on its own. The index is deliberately **not** filtered on `ArchivedAt` — an
archived savegame still holds its profile's slot — and savegames with no profile fall out of it,
since nulls in a unique index are distinct.

**The profile is chosen at publish and never after**, which is why `PUT` is only a rename. Moving a
savegame would put its row and its snapshots in disagreement, and two profiles' revision numbers are
not comparable. Republishing the savegame is the route, and it is three operations the client already
has. `ProfileId` and `ProfileRevision` are nullable and paired — both set or both null, by check
constraint — so a savegame that follows no mod list records no revision and takes no part in any of
the above. See [10 — Savegames and profile revisions](10-savegame-profile-binding.md).

`BasedOn` names the snapshot the check-in was built on, and a stale one is refused with
`savegame-snapshot-stale` carrying the head. **Forcing past it is allowed** and records the fork as
`Origin = Forced` with the snapshot actually played, rather than hiding it — the claim is the social
guard and this is the mechanical one. **A check-in whose hash equals the head's mints nothing** and
answers with the head; a night that changed nothing is not an event. It still ends the caller's
claim, because they pressed check in and should not be left holding a save they handed back.

Taking a claim somebody else holds is Member, deliberately: the design's whole position on conflict
is that a claim is advisory and a take-over is recorded rather than prevented. **Discard is
holder-only** — `Discarded` means "the holder gave it back unplayed", and letting a third party
write that would put a sentence in the log its subject never said. Taking a save is what the
check-out route is for, and it records `TakenOver`.

Checking in, restoring and publishing no longer prune anything. What goes is the daily retention
jobs' decision - see [Retention](#retention); these only clear the schedules they have just made
wrong, after their own commit, so a failure there cannot cost somebody their play.

Reading is Guest throughout, including the claim log: somebody who plays a shared save without
curating it is exactly the person who needs to see who has had it.

### A refusal has to say what is holding it

`GET .../mods/{modId}/dependents` and `.../versions/{versionId}/dependents` name the profiles and
the exact revisions pinning a mod. They are read **after** a delete has been refused, never before
it is offered: the refusal is what `CheckIfVersionIsDependedOn` and the foreign key decide, and
paying for the dependency graph before every delete would mean querying it to tell somebody nothing
was wrong.

Named **per revision**, because that is what the foreign key enforces — a version pinned once, three
hundred revisions ago, is as undeletable as one pinned now, so naming only the profile sends
somebody to a history page with nothing to look for. The response says whether the listing hit its
bound rather than quietly stopping, and flags a profile whose *head* pins it: that one cannot be
pruned at all, so it is a different job (edit the profile and save) rather than a bigger version of
the same one.

### Pruning revisions, and what blocks it

`POST .../profiles/{profileId}/revisions/prune` is how the versions an old revision pins stop being
undeletable. **Admin**, deliberately: keeping history is what makes an old revision reproducible, so
throwing it away is not part of running a repo — it is reclaiming space, which belongs to whoever is
responsible for the repo rather than to whoever is editing a profile today. Every other destructive
action that is not required for normal operation sits at the same level.

**Numbers are not renumbered.** Pruning leaves the gap where a revision was, exactly as
`SavegameSnapshotNumber` already does, and for the same reason: a number exists to be said out loud,
and renumbering would make yesterday's sentence point at a different mod list.

**The head is always refused**, and so is any revision a `SavegameSnapshot` records having been
played on — the foreign key is `Restrict` and the endpoint asks first, once for the whole batch. It
**deletes what it can and names what it cannot**, because a batch that refused wholesale over one
blocked revision would make pruning a hundred of them an exercise in bisection. Each refusal carries
the savegame snapshots holding it, so the next step is a link rather than a guess.

That link needs somewhere to go, which is why
`DELETE .../savegames/{savegameId}/snapshots/{number}` exists. Without it, "played on save X snapshot
3" would be an obstacle the user could see and never move. Admin again, and the **head snapshot is
refused**: it is what a check-out hands people, and a savegame whose current snapshot is missing is
one nobody can play. Rows only — several snapshots legitimately share one content-addressed blob, so
the bytes stay with the reclamation sweep, which asks the one question that makes deleting them

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

### ModHub

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| POST | `modhub/{game}/lookup` | Authenticated | Which of these file names ModHub has, at what version, with the page and CDN links. Up to 5,000 names, matched ignoring case and `.zip`. `{game}` is ModHub's own code (`fs2025`); one not in `ModHub:Games` is a `400` |

Answered from what the crawler stored; nothing here reaches ModHub. **Names rather than a profile**,
because a client asks about whatever it is looking at, and folders hold files the repo has never
registered. `currentAsOf` is null until the first sweep has finished and been followed by a poll — until
then the answer is missing most mods, and a client says so rather than presenting it as "nothing newer".
Authenticated for the same reason as images: it is public data and says nothing about any repo.

## Configuration

`appsettings.json` on the server:

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Database` | PostgreSQL connection string |
| `Storage:StorageAccountName` | Azure Storage account name; the URL is derived |
| `EntraExternalId:*` | Instance, Domain, ClientId, Audience, Authority, and the token/authorization endpoints used by the Swagger UI |
| `SwaggerAuthentication:ClientId` | Separate app registration for the Swagger UI |
| `BlobReclamation:*` | `Enabled`, `Cron` (UTC), `MinimumBlobAge` — the grace period an unreferenced blob must survive before the sweep may delete it — and `MaxReclaimableShare`, past which a sweep refuses to delete anything. See [Blob reclamation](#blob-reclamation) |
| `Retention:*` | `Enabled`, `TimeZone` (IANA, default `Europe/Stockholm`), `ScheduleCron`, `DeleteCron`. See [Retention](#retention) |
| `HangfireDashboard:*` | `Path`, `Username`, `Password`. The dashboard is not mapped without both credentials |
| `ModHub:*` | `Enabled`, `Games` (ModHub's codes; **set here, not defaulted in code** — the binder appends to a list default, which crawled every game twice), `UserAgent`, `RequestDelay`, `PollCron` (UTC), `SweepInterval`, `PollPageLimit`, `RefreshPerPoll`. See [The ModHub crawler](#the-modhub-crawler) |

## Running locally

```bash
dotnet run --project ModsDude.Server/ModsDude.Server.Api
```

Requires a PostgreSQL instance matching `appsettings.Development.json`
(`localhost:5432`, database `modsdude-dev`) and credentials with access to the
`modsdudedev` storage account. Migrations apply on startup. Swagger UI is served in
Development only.

**A local API crawls ModHub too**, into whatever database it points at — the first run of a fresh one
starts the two-hour backfill, resumable across restarts. Set `ModHub__Enabled=false` to stop it.

### Regenerating the client

Nothing has to be running. After changing anything the API describes:

```bash
pwsh scripts/openapi.ps1 -Update     # rewrite openapi/v1.json
pwsh scripts/openapi.ps1             # verify it — what CI runs
```

then run the NSwag configuration at `ModsDude.Client/ModsDude.Client.Core/nswag-config.nswag`
(`nswag run nswag-config.nswag` in that folder), which reads the checked-in document. Commit both.
`openapi/v1.json` exists so that a server change the generated client has not caught up with shows as
a diff rather than as nothing at all.

**The document is written by the build, not fetched from a running API.** The script builds the API
into a directory of its own (so an API already running does not lock it) with
`Microsoft.Extensions.ApiDescription.Server` switched on, which runs `Program`'s entry point against a
server that never listens. `Program.cs` recognises that (`isDescribingOnly`) and skips everything that
reaches outside the process: no migration, no storage containers, no Hangfire. Describing the API
needs no database and cannot touch real data. The document is then rewritten into a canonical form —
keys in ordinal order, two-space indentation, LF, no BOM — so the file records what the API says
rather than which machine asked it.

Its one server is `/`, relative, since a build has no request to take a host from; the generated
clients default `BaseUrl` to that, and `AddModsDudeClient` sets the configured one on every client.
The generated clients derive from `ModsDudeClientBase`, which attaches the bearer token by calling
`IAccessTokenAccessor.Get` for every request.

Note the limit of this check: it fails when the *document* is behind the server, which is the
only warning anyone gets that `Generated.cs` is behind too. Nothing compares the generated client
against the document.
