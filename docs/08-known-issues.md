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

### A rewritten store blob is caught after the fact, not prevented

`FarmingSimulatorBaseModAdapter.SupportsHardlinks` is `true` — the in-game updater was tested and
renames a new file over the old one — so a mod folder file and a store blob are frequently one file.
Blobs are left writable, deliberately: read-only ones would break the updater's rename-over, since
on Windows the attribute blocks unlinking a name as well as writing.

`StoreIntegrityService` therefore detects instead. It rides on the drift check, compares file
identities to tell an in-place rewrite from a rename-over, re-hashes only what fails that, and drops
a blob that no longer matches its address. **Verify store** in the settings does the exhaustive
version on demand, which is what covers a blob damaged by something that never went near a mod
folder.

The limits are the ones detection always has. **There is a window** between the rewrite and the next
drift check in which the wrong bytes are installable into other instances on that volume. Nothing
verifies a store on a schedule, because a pass reads every byte in it — so the exhaustive answer
exists only when somebody asks for it. And **dropping a blob does not repair a mod folder**: where
the entry was hardlinked, the folder still holds the same wrong bytes under the same name, and only
re-applying that profile replaces them. The verification report names the folders that need it,
which is the most it can do from here.

The consequence is bounded by the store's central property: everything in it is registered in some
repo and re-downloadable, so the cost of a caught blob is a download. See
[07](07-mod-sync-design.md#detecting-a-rewritten-blob).

### An activation whose apply failed is recorded, and then never mentioned again

Applying a profile records the standing intent even when the folder could not be put right —
`ProfileApplyOutcome.RecordsIntent` is false only for `Declined` and `Refused`, so `Unavailable` and
`Failed` both leave `ActiveProfile` pointing at the new profile. That is correct: the instance is
still meant to follow it, and being left drifted is what the notice is for.

The notice does not report it. `ModSyncService` writes the manifest **only on success**, so a failed
apply leaves one describing the *previous* profile, and `InstanceDriftService.Check` early-returns
`NeverSynced` for a manifest whose `ProfileId` is not the active one — *"a manifest describing
another profile says nothing about this one"*, which is sound reasoning for the comparison it is
about to skip and the wrong conclusion to hand a caller. `InstanceDrift.IsDrifted` is
`Status is Drifted || HasSavegameDrift`, so `NeverSynced` never enters `InstanceDriftMonitor.Drifted`
and never reaches the app-level notice. The instance's own page says *"This profile has not been
applied to this instance yet"*, but only to somebody who goes and looks.

Two failure shapes land there, and the second is the one the word is wrong for. A **fetch** failure
stops before the destructive phase deliberately, so the folder is untouched and genuinely still on
the old profile. A **remove or install** failure leaves it half-applied — on neither profile — and
`NeverSynced` describes a folder nothing has written to.

A savegame held on that instance is still reported: `savegameDrift` is attached to every answer
including the early returns. It is the mod folder that goes quiet.

**Nothing is corrupt, and nothing needs repairing.** A later re-apply produces the right plan:
reconciliation works from the folder's contents, and `ModSyncPlanner` reads the manifest purely as a
filename-size-time to hash cache, which is profile-independent. The defect is that nobody is prompted
to run one.

Reaching it needs an activation that fails, which is uncommon today — every activation targets one
instance, and most of them succeed.
[Phase 10](PLAN.md#an-intent-that-was-not-carried-out-is-drift) both makes it ordinary, by giving one
game several targets that can fail independently, and fixes it: `NeverSynced` splits so that *no
manifest at all* stays quiet while *a manifest for a different profile* becomes drift.

### `IsTrusted` has no write path

`User.IsTrusted` has a private setter and nothing sets it to `true`. Repo creation is
therefore impossible for every user until someone runs an `UPDATE` against Postgres. This is
the accepted process for now — see [PLAN.md](PLAN.md#deliberately-not-planned) — but it is
undocumented in the app. `AuthorizationResult.NotTrusted` deliberately carries nothing, because
unlike a membership level there is no threshold to report and nothing the user can do about it
from inside the app; the consequence is that the refusal says only that it was refused.

## Unbuilt, and known to be

### A drift notice can outlive the account that could act on it

Instances and their active profiles are machine state and survive a **Switch user** — correctly,
since the mod folders on disk did not change. But `InstanceDrift` names a repo and a profile, and
the new user may be in neither. `DriftNotificationViewModel` degrades rather than breaks: **Review
and import** reports that the profile could not be opened, and **Re-apply now** finds no matching
`Repo` and quietly does nothing. So the notice is still shown, still correct about the drift, and
both of its buttons are dead. Nothing narrows the drift set to repos the signed-in user can
actually reach. See [05](05-client.md#authentication) for what a switch does clear.

### Savegames have a server and an adapter, and nothing that uses them

The server side of [Phase 8](PLAN.md#phase-8--savegames) is built — entities, migration, endpoints,
blob storage, the reclamation sweep — and `IInstanceSavegameAdapter` now enumerates slots, names
them from the save's own data, and says what belongs in a packed save.

What does not exist yet is everything between: no client-side pack/unpack, no checkout binding in
`LocalState`, no slot safety checks, no UI, and `CanSupportSavegames` is still read by nothing. So a
savegame can be created, checked in and out through the API, and no part of the app offers to.

The Farming Simulator slot reader is also **written against the observed layout and never run
against the real game** — twenty `savegameN` folders, `careerSavegame.xml`, `settings/savegameName`
and `settings/playTime`. It degrades rather than throws where that is wrong (a slot it cannot read
is occupied and unnamed, never empty), but the names and the playtimes it produces are unverified.

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

### Ordering by a value object works; comparing on one does not, and neither fails at build time

`RevisionNumber`, `ModId` and the rest are value-converted, and a provider's tolerance for them is
uneven in a way nothing catches until the query runs:

| | |
| --- | --- |
| `OrderBy(x => x.Number)` on an **entity** | Translates — it is the stored column |
| `Contains` over a list of them | Translates |
| `x.Number > cursor` | **Does not** — which is why every listing here windows by offset |
| `OrderBy` after projecting into a **constructor-bound record** | **Does not** — the provider cannot map a record member back to a column |

The last one is the trap, because the query reads as if it should work and the failure is an
exception on a page rather than a build error. Order and window the *entities*, then project the
page. `ProfileRevisionExtensions.GetHistoryAsync` is written that way and says so, and it shipped
broken the other way round first.

The defence is where the queries live: `Persistence/Extensions/EntityExtensions/`, because the
persistence suite is the only thing in the tree that runs one against a real PostgreSQL. A query
written next to its endpoint is a query nothing can cover.

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
- `POST repos/create` is RPC-shaped among otherwise RESTful routes; `POST repos` would be the
  consistent form.
- `CreateRepoV1Endpoint` calls `.RequireAuthorization()` redundantly — the whole group already
  requires it.
- `Profile.Created` and `ProfileRevision.Created` are `DateTime` while
  `ModVersion.Created`/`Updated` are `DateTimeOffset`.
  `ITimeService.Now()` returns `DateTime` and the mod timestamps go through an implicit
  conversion. This is correct today only because `TimeService` returns `DateTime.UtcNow`,
  whose `Kind` is `Utc`; changing it to `DateTime.Now` would silently reinterpret every mod
  timestamp as local — including the ones the mod list's delta form is keyed on. Returning
  `DateTimeOffset` would remove the trap.
