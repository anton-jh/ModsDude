# Known issues

Findings from reading the tree. The solution **builds clean** — 0 warnings, 0 errors — so
none of these are compile-time problems.

Ordered by severity within each section. Headings are deliberately unnumbered so that fixing
one does not renumber the rest and break every link into this page.

> **Recently fixed.**
>
> - **Every mod-dependency endpoint threw a `NullReferenceException`.**
>   `ModDependency.ModVersion` is a plain reference navigation with no auto-include, and none
>   of the four endpoints included it — while every domain operation on a dependency
>   (`AddDependency`, `DeleteDependency`, `HasDependencyOn`, `ChangeVersion`) navigates
>   through it. `ProfileExtensions.GetWithModDependenciesAsync` now loads the graph for the
>   write endpoints; the read endpoint projects instead of materialising.
> - **Problem types did not survive the wire.** The server serialises `ProblemType` with
>   `System.Text.Json`, which ignores `[EnumMember]`, so the value sent was the bare member
>   name — while the generated client, built from an OpenAPI document that *did* carry the
>   `[EnumMember]` URIs, expected the URI. Every problem body failed to deserialise. Each
>   member now carries `[JsonStringEnumMemberName]` alongside `[EnumMember]`.
> - `RepoRepository` and `ProfileService` now branch on `CustomProblemDetails.Type` rather
>   than HTTP 409, which the server never returns.
> - **`DELETE repo/{repoId}` failed at the database** for any repo with mods, because the
>   `Mod` → `Repo` foreign key is `Restrict`. It now refuses with a typed `repo-not-empty`
>   problem instead of a 500.
> - **`GET repos/{id}/profiles` and `GET repos/{id}/profile/{id}` read every profile's whole
>   dependency set.** `ModDependencies` is an owned collection, so EF always materialised it
>   for a `ProfileDto` that does not carry it. Both project now.
> - `Store<T>.Save` wrote in place with `File.WriteAllText`, so an interrupted write produced
>   exactly the corrupt file `Get` recovers from by discarding the user's instances. It writes
>   through a temp file and moves it into place, as the image cache already did.
> - `Store<T>` gained a compatibility predicate and `LocalState.CurrentVersion`, so the
>   planned schema bump actually discards old state instead of deserialising it into the new
>   shape.
> - `Mod.RemoveVersion` had the same lazy-query-over-mutating-loop shape that was fixed in
>   `InsertVersion`. Safe by accident; now materialised.
> - `Mod.GetNextSequenceNumberForVersion` returned `max` instead of `max + 1`, so every
>   version after the first collided on the unique index and mod versioning could not work.
> - `Mod.RemoveVersion` validated the "cannot remove the only version" rule after mutating
>   the set rather than before.
> - `Mod.InsertVersion` left the shift query lazy while the loop body mutated the value its
>   predicate read, over a `HashSet` with unspecified iteration order. Now captured and
>   materialised.
> - `IModsClient` and `IFilesClient` are registered in `AddModsDudeClient`. The client can
>   reach the mod and file endpoints, which unblocks the upload half of import.

## Correctness

### The generated client is stale

`Generated.cs` predates `ProblemType.RepoNotEmpty`, so its `ProblemType` enum has no member for
it and Newtonsoft's `StringEnumConverter` cannot parse the value. A refused repo delete
therefore surfaces as a raw `ApiException` rather than the typed problem, until the client is
regenerated against a running API — see [03 — Server](03-server.md#regenerating-the-client).

More generally, nothing checks that `Generated.cs` matches the server it was generated from.
Every problem type, DTO field and route added on the server is invisible to the client until
somebody remembers to regenerate, and there is no build step or CI check that would notice.

### A second user with the same display name breaks signup

`Api/Middleware/UserLoading/UserLoadingMiddleware.cs`

```csharp
if (await dbContext.Users.AnyAsync(x => x.Username == username))
{
    throw new Exception("Username of new user is not unique");
}
```

`Username` comes from the Entra `name` claim, which is a display name and **not unique**. Two
users called "Anton" means the second one can never complete their first request — every call
throws, with no recovery path in the app and an unhandled exception on the server. A missing
`name` claim throws the same way.

The identity is `sub`, not the name. Username should either be made non-unique, or
disambiguated on collision, or collected separately from the display name.

### Authorization failures return 400

`AuthorizationResultExtensions.MapToBadRequest` maps every authorization result to
`TypedResults.BadRequest`. Insufficient permission is a `403`, and an unauthenticated caller is
a `401`. The problem-details body carries the real meaning, so this is survivable, but it
misleads anything that reasons about status codes — including generic HTTP clients, logging,
and metrics.

### Mod ids are case-sensitive in blob storage and case-preserving on disk

`ModStorageService.BuildModFilename` interpolates `ModId` straight into the blob path:

```csharp
return $"{repoId.Value}/{modId.Value}/{versionId.Value}";
```

`ModId` originates from `Path.GetFileNameWithoutExtension`, which returns whatever casing the
file on disk happens to have. Windows treats `FS22_MyMod.zip` and `FS22_mymod.zip` as the same
name; **Azure blob names are case-sensitive**, and so is the `(RepoId, ModId)` primary key.
Two members whose archives differ only in casing register two different mods pointing at two
different blobs.

Normalize at the adapter boundary — where `LocalMod` is constructed — and carry the result in a
key type rather than a bare string, so no path can bypass it. See
[09 — Mod catalog](09-mod-catalog.md#the-casing-trap).

### The two upload-link rejections are indistinguishable

`CreateModUploadLinkV1Endpoint` guards twice and returns the same `ProblemType.AlreadyExists`
for both: the version is already registered, and the blob already exists but is unregistered.
They need opposite client responses — the second is an orphan from a failed import, which is
recoverable by registering without re-uploading — and the client cannot tell them apart. As it
stands, **a mod whose import failed after upload can never be retried**, because the link
request rejects it forever. See
[09 — Mod catalog](09-mod-catalog.md#retry-is-impossible-without-splitting-the-problem-type).

### Nothing ever reclaims blob storage

There is no code path anywhere that deletes a blob. Deleting a repo orphans every mod file
under its `{repoId}/` prefix permanently, and the delete endpoints planned for mods and
versions would do the same at a smaller scale. Combined with the import orphans described in
[09 — Mod catalog](09-mod-catalog.md#retry-is-impossible-without-splitting-the-problem-type),
storage only ever grows.

This is why `DELETE repo/{repoId}` now refuses a repo that still has mods rather than cascading
— it keeps the amount of unreachable data bounded until a reclamation sweep exists. The
side-effect is that a repo cannot be deleted at all until the mod delete endpoints land, since
there is currently no way to empty one.

### Membership endpoints authorize after loading

`KickMemberV1Endpoint` and `UpdateMembershipV1Endpoint` load the repo and look up the subject's
membership *before* running `CheckIsAllowedTo`. The responses differ depending on whether the
repo and member exist, so any authenticated user can probe for repo ids and membership. Minor,
and only exploitable by someone already signed in, but the other endpoints all authorize first.

`CheckNameTakenV1Endpoint` has the same shape more openly: any authenticated user can test
whether any repo name exists anywhere in the system.

## Unwired and dead code

### Scope policies are defined and never applied

`Api/Authorization/AuthorizationOptionsExtensions.AddApplicationPolicies` and
`Scopes.Repo.Create` are complete and unreferenced. `Program.cs` calls plain
`AddAuthorization()`. Repo creation is gated on `User.IsTrusted` instead. Either wire the
policy up or delete both — right now the file implies a mechanism the system does not use.

### `IsTrusted` has no write path

`User.IsTrusted` has a private setter and nothing sets it to `true`. Repo creation is
therefore impossible for every user until someone runs an `UPDATE` against Postgres. This is
the accepted process for now, but it is undocumented in the app and a new user gets an
unexplained "Not authorized".

### MediatR is registered and never used

`Program.cs` calls `AddMediatR(config => config.RegisterServicesFromAssemblyContaining<ApplicationAssemblyMarker>())`,
and there is not a single handler, request or `ISender` injection in the solution. It implies a
mediator-based application layer that the codebase deliberately does not have — see the note in
[03 — Server](03-server.md#project-layout) about endpoints querying the DbContext directly.
Either the package reference goes, or the intent should be written down.

### Empty and duplicate projects

- `ModsDude.Server.Services` contains only `UserService` with an empty `Register()` method — and
  is the one project still on `net7.0` while everything else targets `net10.0`, so deleting it
  also removes the odd framework out.
- `ModsDude.Server.Common` contains `DomainValidationException` and
  `InvalidPasswordException`; `DomainValidationException` also exists in
  `ModsDude.Server.Domain/Exceptions/`. Only the Domain one is referenced.
- `ModsDude.Client/ModsDude.Client.Cli/` is an empty directory with `bin`/`obj` and no project
  file.
- `ModsDude.slnLaunch.user` references `ModsDude.Client.WinForms`, which does not exist.

### The client drops `Description` from server mod versions

`ModVersionDto` carries `Description`, but `Models/Mod.cs:18` constructs `Mod.Version` without
it:

```csharp
.Select(x => new Version(this, x.VersionId, x.DisplayName, x.SequenceNumber, x.Created))
```

Harmless today because nothing renders a server-only mod. It stops being harmless the moment
the mod details modal can be opened on one, which it would show blank.

### No delete endpoint for mods or versions

Nothing removes a registered mod or version. `Mod.RemoveVersion` exists on the domain and is
unreachable, and it refuses the last remaining version, so "delete this mod entirely" needs its
own path rather than a loop over version deletes. Blocks the management page's cleanup
actions.

### `MenuItemViewModel.Dispose` is never called

`InstanceItemViewModel` subscribes to `LocalInstance.PropertyChanged` through the base
constructor's title-tracking. `ObservableCollectionSynchronizer` removes items from the target
collection and unsubscribes its *own* handler, but never disposes the item view models, so
that subscription outlives the menu entry. Small — a `LocalInstance` outlives the UI anyway —
but it means renaming instances accumulates dead handlers over a session.

## Scaling

### `GET repos/{repoId}/mods` returns everything, unpaged

```csharp
var mods = await dbContext.Mods.Where(x => x.RepoId == new RepoId(repoId)).ToListAsync(ct);
```

With `Mod.Versions` auto-included, this materialises every mod, every version, and every
version's attributes in one response. (The flattening in
[02](02-domain-model.md#flattening) removes the auto-include, which helps but does not fix
this — an unpaged query over a flat table is still unpaged.) Against the stated target of thousands of registered
versions per repo, this is a multi-megabyte payload on every mods page load, plus the
allocation cost on both ends.

It needs **both** pagination and a delta form — they solve different problems and neither
substitutes for the other. Pagination bounds any single response, which is what keeps a
first-time load from timing out. A delta form — "what changed since this timestamp", using the
existing `Mod.Updated` — bounds the *steady state*, which is what makes repeated syncs cheap.
Paginate the delta as well; a first sync against an established repo returns everything.

### Full-list refresh after every mutation

`ProfileService` and `RepoRepository` call `Refresh*` after every create, update and delete,
which clears and refills the observable collection. That discards and rebuilds every view
model, losing selection and scroll position, and costs a full round trip for a one-field
change. Fine at ten profiles; not at scale, and it is why the `*OfInterestChanged` event has
to exist to restore selection afterwards.

### Owned collections are always materialised

Not a single bug so much as a trap the model sets. `Profile.ModDependencies` is an owned
collection, so **any** query that materialises `Profile` entities reads every dependency row
with it — thousands per profile at the stated volumes — whether or not the caller wants them.
The two profile read endpoints hit this and now project instead; anything new that loads
profiles has to make the same choice deliberately. The same applies to `ModVersion.Attributes`
under `Mod`.

### No unique index backing the one-version-per-mod rule

`ProfileEntityTypeConfiguration` carries the `TODO` itself: the unique index on
`(RepoId, ProfileId, ModId)` is missing. The rule is enforced only in `Profile.AddDependency`,
so a concurrent double-add, or any future code path that bypasses the aggregate, can produce a
profile that pins one mod at two versions — which the sync engine would have no way to
resolve. The same configuration also notes the key column order should put `RepoId` first.

## Project hygiene

### The client's server URL is hardcoded to localhost

`Generated.cs` sets `BaseUrl = "http://localhost:5267"` in every client class. Since the file
is regenerated wholesale, this cannot be fixed by editing it — the base URL needs to come from
`appsettings.json` and be applied to each `HttpClient` in `AddModsDudeClient`. The WPF
`appsettings.json` is currently an empty `{}`, and `App.xaml.cs` builds an `IConfiguration`
from it that is then never used.

Related: the API is reached over plain HTTP on localhost while `Program.cs` calls
`UseHttpsRedirection()`.

### No tests

There is no test project of any kind. The domain entities — version sequencing, membership
level transitions, dependency rules — are pure, dependency-free, and the highest-value place
to start. Every domain defect listed as recently fixed at the top of this page would have
been caught by a single test.

### No CI

`.github/workflows/` exists and is empty.

### No deployment artifacts

No Dockerfile, no Bicep/Terraform, no publish profile, no client installer configuration.
Storage and identity are configured for real Azure resources (`modsdudedev`, a live CIAM
tenant), so the deployment exists somewhere but is not described in the repository.

## API surface inconsistencies

Cosmetic, but they leak into the generated client and are cheapest to fix before anything
depends on them:

- Collection routes are plural (`repos`, `repos/{id}/profiles`) while single-resource routes
  are singular (`repo/{id}`, `repos/{id}/profile/{id}`).
- `POST repos/create` and `POST repos/check-name-taken` are RPC-shaped among otherwise RESTful
  routes; `POST repos` would be the consistent form.
- `CheckNameTakenV1Endpoint` and `CreateRepoV1Endpoint` call `.RequireAuthorization()`
  redundantly — the whole group already requires it.
- `Profile.Created` is a `DateTime` while `Mod.Created`/`Updated` are `DateTimeOffset`.
  `ITimeService.Now()` returns `DateTime` and the mod timestamps go through an implicit
  conversion. This is correct today only because `TimeService` returns `DateTime.UtcNow`,
  whose `Kind` is `Utc`; changing it to `DateTime.Now` would silently reinterpret every mod
  timestamp as local. Returning `DateTimeOffset` would remove the trap.
