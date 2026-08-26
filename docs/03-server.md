# Server

## Project layout

```
ModsDude.Server.Api          ASP.NET Core host — endpoints, DTOs, middleware, problem details
ModsDude.Server.Application  Authorization primitives, ITimeService, IUnitOfWork, IModStorageService
ModsDude.Server.Domain       Entities and invariants. No framework references
ModsDude.Server.Persistence  EF Core / PostgreSQL — DbContext, entity configuration, migrations
ModsDude.Server.Storage      Azure Blob Storage — SAS issuance
ModsDude.Server.Services     Empty. Contains only a stub UserService
ModsDude.Server.Common       Empty. Duplicates two exception types from Domain
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

    public async Task<Results<Ok<IEnumerable<ModDto>>, BadRequest<CustomProblemDetails>>> GetAll(...)
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
   .MapAllEndpointsFromAssembly(typeof(Program).Assembly);
```

so **authentication is on by default** for everything. Versioning is by URL segment via
`Asp.Versioning`, currently only v1.

## Request pipeline

```
HTTPS redirect
  └─ Swagger UI (Development only)
      └─ Authentication  (JWT bearer, Microsoft.Identity.Web)
          └─ Authorization
              └─ UserLoadingMiddleware
                  └─ api/v1/... endpoints
```

Migrations are applied at startup, after the pipeline is built, by resolving
`ApplicationDbContext` in a scope and calling `Database.Migrate()`.

### Authentication

Configured from the `EntraExternalId` configuration section. Two details worth knowing:

- `MapInboundClaims = false` — claims keep their original JWT names, so the code reads
  `sub` and `name` rather than the long WS-Federation URIs.
- `NameClaimType = "name"`, which is what `UserLoadingMiddleware` uses as the username.

The Swagger UI in Development is wired for the authorization-code + PKCE flow against the
same tenant, using a separate `SwaggerAuthentication:ClientId`.

### User provisioning

`Api/Middleware/UserLoading/UserLoadingMiddleware.cs` runs after authorization on every
request:

1. No authenticated identity or no `sub` claim → pass through untouched.
2. User row exists → refresh `LastSeen` if it is more than an hour stale, then continue.
3. User row does not exist → create it from `sub` + `name` and save.

There is no signup endpoint; **first authenticated request is the signup**. The middleware
throws a bare `Exception` if the `name` claim is missing or already taken by another user —
see [08 — Known issues](08-known-issues.md), because Entra display names are not unique.

## Authorization

Two independent mechanisms.

### ASP.NET policies — defined but unused

`Api/Authorization/AuthorizationOptionsExtensions.cs` builds policies that assert a scope is
present in the token's `scope` claim, and `Scopes.Repo.Create` names one. **Neither is wired
up** — `Program.cs` calls plain `AddAuthorization()` and never calls
`AddApplicationPolicies()`. Repo creation is instead gated on `User.IsTrusted`.

### The fluent authorization builder — the real mechanism

`Application/Authorization/`. Every endpoint that touches a repo starts the same way:

```csharp
var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
    .CheckIsAllowedTo(x => x
        .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
    .MapToBadRequest();
if (authResult is not null)
{
    return authResult;
}
```

`CheckIsAllowedTo` loads the user (whose memberships are auto-included), runs the checks,
and returns `null` on success or an `AuthorizationResult` on the first failure. The builder
short-circuits: once `Result` is set, later checks are no-ops, so the caller sees the first
thing that went wrong.

Three checks exist:

| Check | Meaning |
| --- | --- |
| `AccessRepoAtLevel(repoId, level)` | Caller's membership in that repo is at least `level` |
| `GrantAccessToRepo(repoId, level)` | Caller may hand out `level` — currently identical logic: you must hold at least the level you are granting |
| `ChangeOthersMembership(subjectMembership)` | Modifying a Guest needs Member; modifying a Member or an Admin needs Admin |

`ChangeOthersMembership` is the interesting one — the level you need depends on the level of
the person you are acting on, which is what stops a Member from kicking an Admin.

Failures are mapped to a `400` carrying `CustomProblemDetails`. **Note that authorization
failures return 400, not 401/403.**

### Error responses

`Api/ErrorHandling/Problems.cs` is a catalogue of RFC 7807-shaped problems. Each has a
`ProblemType` enum member carrying a stable URI in an `[EnumMember]` attribute
(`https://server.modsdude.com/api/problems/name-taken` and friends), so a client can switch
on the type rather than parse prose. `Problems.NotFound.With(x => x.Detail = "...")` lets an
endpoint specialise the detail without a new catalogue entry.

The client, however, currently branches on HTTP status codes (`ex.StatusCode == 409`) rather
than problem type — and the server returns `400` for these cases, so that handling does not
fire. See [08 — Known issues](08-known-issues.md).

## Persistence

`ApplicationDbContext` exposes `Users`, `Repos`, `RepoMemberships`, `Profiles`, `Mods` and
implements `IUnitOfWork` (`CommitAsync` → `SaveChangesAsync`).

Notable configuration:

- **Composite keys everywhere.** `Mod` is `(RepoId, ModId)`; `Profile` is `(RepoId, ProfileId)`;
  `RepoMembership` is `(UserId, RepoId)`. Repo scoping is baked into the primary key rather
  than being a filter you can forget.
- **`ModVersion` uses shadow FK properties** (`RepoId`, `ModId`) and is keyed
  `(RepoId, ModId, VersionId)`, with a unique index on `(RepoId, ModId, SequenceNumber)`.
- **`ModDependency` is an owned collection** of `Profile`, with an FK to `ModVersion`. Its
  key order carries a `TODO` about putting `RepoId` first, and the unique index on
  `(RepoId, ProfileId, ModId)` that would enforce "one version per mod per profile" at the
  database level is **missing** — the rule is only enforced in the domain.
- **`Repo._memberships` is mapped through the private backing field**, with a runtime guard
  that throws at model-build time if the field is renamed, so EF cannot silently fall back
  to a shadow property.
- `Repo.AdapterData` is an EF **complex property**, flattened into the repo row.
- Auto-included navigations: `Mod.Versions`, `Repo._memberships`, `User.RepoMemberships`.
  These make the authorization pattern above a single round trip, at the cost of always
  paying for them.
- Entity extension methods in `Persistence/Extensions/EntityExtensions/` provide the small
  query vocabulary the endpoints use — `GetAsync`, `CheckNameIsTaken`, `GetByUsernameAsync`.

There is exactly one migration, `20250717173302_MoveToPostgres`, which squashes the
pre-Postgres history.

## Storage

`ModsDude.Server.Storage` registers a `BlobServiceClient` for
`https://{account}.blob.core.windows.net` using `Azure.Identity` default credentials — no
connection string, no account key.

Mod files live in the `mods` container at:

```
{repoId}/{modId}/{versionId}
```

`ModStorageService` offers exactly two operations:

- `CheckIfModExists` — a blob existence check.
- `GetUploadLink` — mints a **user-delegation SAS** with `Create | Write` permission, valid
  for 30 minutes, scoped to that one blob.

The user-delegation key is derived from the server's own managed identity, so the SAS
inherits the server's permissions and can be revoked centrally. **The API never handles mod
bytes.** The client uploads straight to blob storage.

**There is no download link operation.** This is the single missing piece that blocks profile
sync — see [07 — Mod sync design](07-mod-sync-design.md).

## Endpoint reference

All routes are prefixed `api/v1`. All require authentication. "Level" is the repo membership
level required.

### Users

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `users` | — | Every user who shares at least one repo with the caller |
| GET | `users/search?username=` | — | Exact username match, returns `{ user: null }` if absent |

### Repos

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `repos` | — | The caller's repos with their membership level, ordered by name |
| POST | `repos/create` | — | Requires `User.IsTrusted`. Creator becomes Admin |
| POST | `repos/check-name-taken` | — | No repo-scoped check; any authenticated user can probe any name |
| GET | `repo/{repoId}` | Member | Repo details including the member list |
| PUT | `repo/{repoId}` | Admin | Rename and/or replace adapter configuration |
| DELETE | `repo/{repoId}` | Admin | |

Note the inconsistency: the collection is `repos`, the single resource is `repo`.

### Members

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| POST | `repos/{repoId}/members` | Member + may grant the requested level | |
| PUT | `repos/{repoId}/members/{userId}` | `ChangeOthersMembership` + may grant the new level | |
| DELETE | `repos/{repoId}/members/{userId}` | `ChangeOthersMembership` | Refuses the last Admin |

### Profiles

| Method | Route | Level |
| --- | --- | --- |
| GET | `repos/{repoId}/profiles` | Guest |
| GET | `repos/{repoId}/profile/{profileId}` | Guest |
| POST | `repos/{repoId}/profiles` | Member |
| PUT | `repos/{repoId}/profiles/{profileId}` | Member |
| DELETE | `repos/{repoId}/profiles/{profileId}` | Member |

Same singular/plural inconsistency on the single-profile GET.

### Mod dependencies

| Method | Route | Level |
| --- | --- | --- |
| GET | `repos/{repoId}/profiles/{profileId}/modDependencies` | Guest |
| POST | `repos/{repoId}/profiles/{profileId}/modDependencies` | Member |
| PUT | `repos/{repoId}/profiles/{profileId}/modDependencies/{modId}` | Member |
| DELETE | `repos/{repoId}/profiles/{profileId}/modDependencies/{modId}` | Member |

The dependency is addressed by `modId`, not by a dependency id — a direct consequence of the
one-version-per-mod rule.

### Mods

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| GET | `repos/{repoId}/mods` | Guest | **Every mod with every version, unpaged** |
| POST | `repos/{repoId}/mods` | Member | Register a version. Verifies the blob exists first |

### Files

| Method | Route | Level | Notes |
| --- | --- | --- | --- |
| POST | `files/createModUploadLink` | Member | 30-minute write SAS. Refuses if the version is already registered or the blob already exists |

## Configuration

`appsettings.json` on the server:

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Database` | PostgreSQL connection string |
| `Storage:StorageAccountName` | Azure Storage account name; the URL is derived |
| `EntraExternalId:*` | Instance, Domain, ClientId, Audience, Authority, and the token/authorization endpoints used by the Swagger UI |
| `SwaggerAuthentication:ClientId` | Separate app registration for the Swagger UI |

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
`IAccessTokenAccessor.Get` for every request. The generated `BaseUrl` is hardcoded to
`http://localhost:5267` — see [08 — Known issues](08-known-issues.md).
