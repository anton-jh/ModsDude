# Known issues

What is still wrong or unbuilt in the current tree. None of it is a compile-time problem; CI
builds both halves of the solution and runs all three test projects on every push.

Ordered by severity within each section. Headings are deliberately unnumbered so that fixing
one does not renumber the rest and break every link into this page.

> This page used to be long. Nearly everything on it — the mod-dependency endpoints throwing, the
> problem types not surviving the wire, mod ids being case-sensitive in blob storage, the
> indistinguishable upload-link rejections, blob storage never being reclaimed, authorization
> answering 400, the duplicate-display-name crash, the missing delete endpoints, the missing
> unique index, the unpaged mod list, the full-list refresh after every mutation, the hardcoded
> localhost base URL, the absent tests and CI — has been fixed. What follows is what genuinely
> remains.

## Correctness

### Nothing checks that the generated client matches the server

`openapi/v1.json` is checked in and CI diffs it against the running API, which fails when the
*document* is behind the server. That is the only warning anyone gets that
`ModsDude.Client.Core/ModsDudeServer/Generated.cs` is behind too — **nothing compares the
generated client against the document.** Regenerating the client is still a step somebody has to
remember; the check tells you when it is needed, not whether it was done.

Until it is regenerated, a newly added problem type cannot be matched by the client at all, since
it branches on `CustomProblemDetails.Type`. See
[03 — Server](03-server.md#regenerating-the-client).

### `IsTrusted` has no write path

`User.IsTrusted` has a private setter and nothing sets it to `true`. Repo creation is
therefore impossible for every user until someone runs an `UPDATE` against Postgres. This is
the accepted process for now — see [PLAN.md](PLAN.md#deliberately-not-planned) — but it is
undocumented in the app. `AuthorizationResult.NotTrusted` deliberately carries nothing, because
unlike a membership level there is no threshold to report and nothing the user can do about it
from inside the app; the consequence is that the refusal says only that it was refused.

## Unbuilt, and known to be

### Hardlinking is switched off, so every sync copies

`FarmingSimulatorBaseModAdapter.SupportsHardlinks` is `false`, and store blobs are left writable,
because nobody has tested whether Farming Simulator's in-game updater rewrites a mod file **in
place** or renames a new one over it. In-place through a hardlink would corrupt a store blob
shared with every repo on the volume; rename-over breaks the link harmlessly.

The consequence is real and measurable: materialising a 2,000-mod profile is a full copy of tens
of gigabytes rather than seconds of directory operations, on every install and replace. That is
the right way round — a slow sync is visible and recoverable, a corrupted store is neither — but
it is a cost being paid for an unanswered question, and the question needs the actual game to
answer. See [07](07-mod-sync-design.md#hardlink-support-is-an-adapter-property).

### Import leaves the content store cold

`ModImportService` does not write the files it uploads into a local content store. Importing an
existing 2,000-mod install therefore leaves the store empty, and the first sync re-downloads
bytes that were on the machine the whole time. The design calls this out as one of four ingestion
paths; it is the one that is missing. See
[07](07-mod-sync-design.md#ingestion).

### A drift notice can outlive the account that could act on it

Instances and their active profiles are machine state and survive a **Switch user** — correctly,
since the mod folders on disk did not change. But `InstanceDrift` names a repo and a profile, and
the new user may be in neither. `DriftNotificationViewModel` degrades rather than breaks: **Review
and import** reports that the profile could not be opened, and **Re-apply now** finds no matching
`Repo` and quietly does nothing. So the notice is still shown, still correct about the drift, and
both of its buttons are dead. Nothing narrows the drift set to repos the signed-in user can
actually reach. See [05](05-client.md#authentication) for what a switch does clear.

### Savegames are placeholder interfaces

`IBaseSavegameAdapter` and `IInstanceSavegameAdapter` have no members, and
`CanSupportSavegames` is read by nothing. Deliberate — [Phase 8](PLAN.md#phase-8--savegames).

## Traps in the model

### Owned collections are always materialised

Not a bug so much as a trap the model sets. `ProfileRevision.ModDependencies` is an owned
collection, so **any** query that materialises `ProfileRevision` entities reads every dependency
row with it — thousands per revision at the stated volumes — whether or not the caller wants
them. History multiplies it: a page of fifty revisions of a two-thousand-mod profile is a hundred
thousand rows to render fifty summary lines.

Everything that reads therefore projects — `ProfileRevisionExtensions`, `ProfileRevisionReads` —
and the only revision ever materialised is a new one on its way in. `Profile` has no navigation
to its revisions at all, which is the structural half of the same defence: a profile load cannot
drag a history in with it even by accident. Anything new that touches revisions has to make the
same choice deliberately. `ModVersion.Attributes` and `ModVersion.Images` are the same shape one
entity over.

### A mod version that has ever been pinned cannot be deleted

The dependency foreign key onto `ModVersion` is `Restrict`, and dependencies live on revisions
that are never rewritten — so a version any revision of any profile has ever pinned holds that
version in place forever. In practice, a mod version that has been used is a mod version that
cannot be deleted.

This is a deliberate consequence of keeping history rather than an oversight, and the reasoning
is in [02](02-domain-model.md#a-pinned-version-cannot-be-deleted-any-more): an old revision that
is not reproducible is not worth keeping. It is listed here because it is the thing about
revisions most likely to surprise somebody who came looking for a delete that used to work.
Blobs are shared by content hash, so the storage cost is bounded by distinct files rather than by
pins; if it ever does bite, the release valve is pruning old revisions on a policy, which
[PLAN.md](PLAN.md#phase-45--profile-revisions) leaves unbuilt on purpose.

### The usage cursor is an offset, and shifts under concurrent edits

`GET repos/{repoId}/mods/usage` paginates by offset rather than by a key, because the ids are
value objects and a provider cannot translate a comparison on one. A page can therefore repeat or
miss a row while somebody else is saving a profile.

That is acceptable **here and only here**: the answer is advisory, and the delete endpoints
re-ask the database the moment it matters. The mod listing works around the same constraint
differently, with a timestamp-plus-count cursor that can repeat a row but never skip one.

### A delta never reports deletions

`GET repos/{repoId}/mods?updatedAfter=` returns what changed, and a deleted row does not change —
it is gone. A client that has to notice removals has to refetch without `updatedAfter`.

`ModCatalog` has both forms and keeps them distinct: `RefreshRegisteredMods` takes the delta,
`ReloadRegisteredMods` drops everything and refetches. **The trap is that only a delete performed
*on this machine* triggers the reload.** A version a teammate deletes stays in an open catalog
until something reloads it, and nothing polls. Not harmful — the delete endpoints and the
foreign key both refuse the cases that matter — but a stale row can be offered for a profile it
can no longer be pinned to.

## Scope

### Image routes are authenticated-user, not repo-scoped

`GET images/{hash}`, `POST images/{hash}` and `POST images/checkExisting` check only that the
caller is authenticated. The route carries no `repoId` and cannot — content addressing is what
makes cross-repo dedupe work, and it leaves no repo in the address.

This is a decision rather than an oversight, and it is argued in
[09](09-mod-catalog.md#what-authorized-means-for-a-global-address): what is behind an address is
mod store art, already public on the sites the mods come from, and it reveals nothing about who
is in which repo. It is listed here because it is the one place where the server's
repo-scoped-by-primary-key posture does not hold, and anyone reasoning about access control
should know that rather than discover it.

The batch existence check is also an existence oracle over every image in the system, for any
signed-in user.

## Project hygiene

### No deployment artifacts

No Dockerfile, no Bicep/Terraform, no publish profile, no client installer configuration.
Storage and identity are configured for real Azure resources (`modsdudedev`, a live CIAM
tenant), so the deployment exists somewhere but is not described in the repository.

The `mod-images` container is the one piece that does not depend on this: the API creates it at
startup if it is missing. The `mods` container does not, and a storage account provisioned from
nothing would still need it created by hand.

### The API is reached over plain HTTP on localhost

`Program.cs` calls `UseHttpsRedirection()` while the client's configured base URL is
`http://localhost:5267`, which is also what `scripts/openapi.ps1` drives.

### Invite codes are stored in the clear

`RepoInvite.Code` is a plain column, not a hash. It has to be: the admin page shows the code and
offers to copy it, which is impossible if the server cannot read it back. Sixty bits of entropy,
a revoke button and optional caps are what stand in for hashing. Anybody with read access to the
database can join any repo — but anybody with that access can insert a membership row directly
anyway, so the code is not what is guarding it.

### Two users with one name can also share a tag

`UserTag` is four digits, so two people called Anton collide once in ten thousand. The member
list would then show two identical rows, distinguishable only by their avatar colour, which is
derived from the same four digits and so is identical too. Widening the tag for a group whose
members share a name is the fix if it ever happens; nothing detects it today.

### `GET users` has no caller

It returns everyone who shares a repo with you, and nothing in the client asks for it. It existed
to feed the add-a-member flow, which invites replaced. It is harmless — every user it returns is
already visible in a member list — but it is unused surface.

## API surface inconsistencies

Cosmetic, but they leak into the generated client and are cheapest to fix before anything
depends on them:

- Collection routes are plural (`repos`, `repos/{id}/profiles`) while single-resource routes
  are singular (`repo/{id}`, `repos/{id}/profile/{id}`).
- `POST repos/create` and `POST repos/check-name-taken` are RPC-shaped among otherwise RESTful
  routes; `POST repos` would be the consistent form.
- `CheckNameTakenV1Endpoint` and `CreateRepoV1Endpoint` call `.RequireAuthorization()`
  redundantly — the whole group already requires it.
- `Profile.Created` and `ProfileRevision.Created` are `DateTime` while
  `ModVersion.Created`/`Updated` are `DateTimeOffset`.
  `ITimeService.Now()` returns `DateTime` and the mod timestamps go through an implicit
  conversion. This is correct today only because `TimeService` returns `DateTime.UtcNow`,
  whose `Kind` is `Utc`; changing it to `DateTime.Now` would silently reinterpret every mod
  timestamp as local — including the ones the mod list's delta form is keyed on. Returning
  `DateTimeOffset` would remove the trap.
