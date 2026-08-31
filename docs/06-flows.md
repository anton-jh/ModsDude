# Flows

End-to-end walkthroughs of what the system does today. The reasoning behind the sync flow — the
content store, the uninstall rules, drift — lives in
[07 — Mod sync design](07-mod-sync-design.md); this page is the sequence of steps.

## First launch and sign-in

1. `App.OnStartup` builds the container, shows `MainWindow` with `MainWindowViewModel`, and
   *then* awaits `AuthenticationService.Get()`. The window is up before authentication
   starts.
2. MSAL configures its encrypted token cache at `{AppData}/msal_cache.dat` on first call.
3. If a cached account exists, `AcquireTokenSilent`; on `MsalUiRequiredException` it falls
   back to `AcquireTokenInteractive`, which opens a browser against the CIAM `susi_1` user
   flow. With no cached account it goes straight to interactive.
4. `IsLoggedIn` flips, `LoggedInChanged` fires, and `MainWindowViewModel` swaps
   `LoginPageViewModel` for a `MainPageViewModel`.
5. `MainPage.Init` runs `LoadReposCommand` → `GET api/v1/repos`.
6. **Server side, on that first request:** `UserLoadingMiddleware` finds no `User` row for
   the `sub` claim and provisions one from `sub` plus a username resolved from the `name` claim.
   There is no signup step — the first authenticated call *is* the signup. Display names are not
   unique and there is no form on which to report a collision, so a taken name is
   disambiguated with a numeric suffix rather than refused; see
   [03 — Server](03-server.md#user-provisioning).

## Creating a repo

1. Sidebar → **Create repo**. `CreateRepoPageViewModel` lists adapters from
   `IGameAdapterIndex.GetAllLatest()` — latest compatibility version per adapter id.
2. Picking an adapter yields `GetBaseSettingsTemplate()`, a `DynamicForm` rendered by
   `DynamicFormEditor`. Editing it raises `Modified`, which takes the navigation lock, so
   navigating away now prompts.
3. Save validates the name and the form, then `RepoRepository.CreateRepo` serializes the
   settings and calls `POST api/v1/repos/create`.
4. Server: refuses with a `403` unless `User.IsTrusted`; rejects a taken name with the
   `name-taken` problem; otherwise creates the repo with
   `AdapterData = (adapterId, serializedConfig)` and makes the caller Admin.
5. Client adds the returned repo to its live collection and raises `RepoCreated`, which
   `MainPage` uses to select it — you land on its page.

**Gate:** a new user cannot do this. `IsTrusted` defaults to `false` and nothing sets it —
someone flips it in the database.

## Connecting a game (creating an instance)

1. Open a repo. If it has no instances, `RepoPageViewModel` **auto-selects "Connect game"** —
   the one thing you must do before the repo is useful.
2. `CreateLocalInstancePageViewModel` builds `repo.Adapter.GetInstanceSettingsTemplate()`.
   For Farming Simulator the template has already probed `My Documents\My Games\` for the year
   the repo's `GameVersion` names, in both spellings the installer has used, so the path
   is usually pre-filled. The name defaults to "Game" for a first instance.
3. Validation covers the name (non-empty, not already used in this scope), the form's own
   `PerformValidation` — for FS, that the folder actually exists — and that **no other instance
   already owns that folder**, checked across every scope rather than within this one.
4. Save adds the instance to `LocalInstanceRepository`, which writes `state.json`.

**Nothing is sent to the server.** Instances are per-machine by design; two members of the
same repo have entirely separate instance lists.

An instance is scoped to a **game**, not a repo, so one installation is configured once and
appears under every repo targeting that game — and it carries an explicit **active profile**,
the `(RepoId, ProfileId)` pair sync reconciles against. The scope is not the adapter id alone,
because one adapter serves both FS22 and FS25; see
[04 — Game adapters](04-game-adapters.md#instance-scope).

## Importing mods from an installed game

Repo → **Mods**. This is the most performance-sensitive path in the app.

1. `RepoModsPageViewModel` builds a repo-scoped `ModCatalog`, which resolves `IBaseModAdapter`
   from the repo's adapter and throws a user-friendly error if the game does not support mods.
2. The catalog waits **150 ms** before touching the disk. A page nobody stopped on never opens a
   file.
3. Its **sources** are every instance's mod folder in this scope, the system Downloads folder,
   and anything the user adds for the session. Each is scanned by
   `GetModsFromFolder` — every `.zip` opened in parallel, capped at `ProcessorCount`, reading
   `modDesc.xml` out of each. Scans are cached **per source** and the merged view composed on
   demand, so toggling a source's checkbox recomposes from memory and adding one scans only the
   new folder. A source that fails is marked bad rather than failing the catalog.
4. In parallel, the catalog walks `GET repos/{repoId}/mods` a page at a time and folds the
   registered versions in, joining on `(ModId, VersionId)` — the join key is exact, because it
   is the same pair the adapter produced and registration stored.
5. Results are deduped on `(ModId, VersionId)`, with `FoundIn` recording every source a version
   turned up in; source names appear on a row only when more than one source is enabled. **The
   same mod found in two sources with different bytes is kept as both occurrences and withheld
   from import** rather than silently resolved.
6. Rows are wrapped in `ModListItemViewModel` and filtered by the search box and the presence
   chips. **No icons are read yet.**
7. As rows scroll into view, `LazyLoad` calls `LoadAsync`, which pulls the icon through
   `ModImagerySource` — the server's derivatives for a registered version, the archive for an
   unregistered candidate — and then `ModImageProvider`'s memory and disk caches.
8. Select rows individually or via Select all / Select none, which operate on the *visible*
   (filtered) set.
9. Import runs `ModImportService` over the selection, with per-row phase and progress.

Navigating away cancels the scan, and `OnInitFailed` swallows the resulting
`OperationCanceledException`.

## Registering a mod version

```
Client                          Server                         Blob storage
  │                               │                                 │
  ├─ POST files/createModUploadLink                                 │
  │    { repoId, modId, versionId }                                 │
  │                               ├─ auth: Member                   │
  │                               ├─ already-registered?            │
  │                               ├─ file-already-present + hash?   │
  │                               ├─ mint user-delegation SAS ──────┤
  │◀── { link, contentHashMetadataKey }  (30 min, Create|Write) ────┤
  │                                                                 │
  ├─ PUT the archive in blocks, SHA-256 off the same buffer ────────▶
  │    committing with the hash as blob metadata                    │
  │                                                                 │
  ├─ POST repos/{repoId}/mods                                       │
  │    { modId, versionId, displayName, description,                │
  │      contentHash, locked, placement, attributes }               │
  │                               ├─ auth: Member                   │
  │                               ├─ CheckIfModExists ──────────────▶
  │                               ├─ assert placement's neighbours  │
  │                               ├─ MakeRoomAt, one insert         │
  │                               └─ commit                         │
  │◀── ModDto                                                       │
  │                                                                 │
  ├─ POST images/checkExisting, then POST images/{hash} for the gaps│
  ├─ PUT repos/{repoId}/mods/{modId}/versions/{versionId}/images    │
  │    best-effort, never blocking the registration above           │
```

The hash is computed off the same buffer the upload blocks are cut from, so the file is read
once, and stamped as blob metadata on commit — which is what lets the server hand it back later
when somebody hits an orphan.

The **placement** is *insert between A and B*, computed client-side by the adapter's comparer,
and the server asserts both neighbours. Relative placement against a single neighbour stops
collisions but still permits a silently wrong order when two members insert against a state
neither has seen the other change — which offers a downgrade as an update. A rejection means
refetch, recompute, retry. See
[09 — Mod catalog](09-mod-catalog.md#importing-several-versions-of-one-mod-at-once).

Five mods are in flight at once, and **one concurrency slot is held for a whole mod**, so
several new versions of the same mod register strictly sequentially in ascending order while
distinct mods overlap. Per-mod ordering is what protects the invariant below; concurrency across
mods does not weaken it.

Three properties of this design are worth keeping:

- **Mod bytes never pass through the API.** The server's role is authorization and
  bookkeeping; bandwidth is blob storage's problem.
- **Registration verifies the blob.** `RegisterMod` calls `CheckIfModExists` before writing
  metadata, so a failed or abandoned upload cannot leave a `ModVersion` row pointing at
  nothing.
- **A mod is therefore never registered before its file exists.** The residue of a failed
  import is orphaned blobs, never dangling registrations — which is the right way round, since
  an orphaned blob is invisible and reclaimable while a dangling registration would break
  every sync that tried to use it.

Run this loop **per mod** rather than as a batch upload followed by a batch register: it bounds
the orphan set to a single blob. See
[09 — Mod catalog](09-mod-catalog.md#retry-is-impossible-without-splitting-the-problem-type) for
the retry protocol the two distinct upload-link refusals make possible — an orphan whose hash
matches ours is adopted by registering without re-uploading, one whose hash differs is refused,
and an already-registered version counts as success.

`ModId` and `ModVersionId` come from the game, not from us — for Farming Simulator, the
archive filename (normalized through `ModKey`) and the `<version>` element.

## Managing profiles

Create, rename and delete all go through `ProfileService`, which applies the returned DTO to the
live collection rather than refetching the list, and raises `ProfileCreated` or `ProfileUpdated`
carrying the id. `RepoPageViewModel` listens and selects the affected profile, so you land on
what you just created.

Name conflicts return the `name-taken` problem from the server, which the client matches on
`CustomProblemDetails.Type`.

## Editing a profile's mod list

Profile → **Mods**. Two lists: available on the left, pinned on the right.

1. The left list is the union of registered mods and local candidates from the same `ModCatalog`
   the management page uses, minus whatever is already pinned — so a mod can be added to the
   profile *and* imported without a detour to another page. It carries the same source list and
   checkboxes.
2. **Updates render on the right**, on the row of the mod that has one, plus an "N updates
   available" batch action. Putting them on the left would place the same mod on both sides at
   once.
3. Right-hand rows carry a version selector and a `Locked` toggle, since the list is keyed by
   `ModId` and moving a mod rightward means choosing a version. The row shows the *effective*
   lock and which level it came from — the adapter's flag on the mod, or this profile's own —
   because those are different situations with different fixes.
4. The batch update calls `POST .../modDependencies/upgrade`, which **skips locked mods entirely**
   and reports what it skipped. The skipped count opens a dialog listing them with an unchecked
   checkbox each, reached deliberately rather than fired at every save.
5. A local-only mod moved right is **pending**, not uploaded. Save imports the pending mods
   through `ModImportService` and then writes the dependencies. Uploading on drag would make
   Cancel meaningless and litter the repo with mods nobody kept.

## Managing members

1. Find the user: `GET users/search?username=` for an exact match, or `GET users` for
   everyone who already shares a repo with you.
2. `POST repos/{repoId}/members` with a level. The caller must be at least Member **and** hold
   at least the level being granted — you cannot mint an Admin unless you are one.
3. `PUT .../members/{userId}` changes a level. Authorization runs `ChangeOthersMembership`
   (Member to touch a Guest, Admin to touch a Member or Admin) *and* the grant check on the
   new level.
4. `DELETE .../members/{userId}` kicks, refusing the last Admin at both the endpoint and the
   entity. **Demotion is refused too** — kicking the last Admin was always refused, but demoting
   them reached the membership directly and bypassed the entity, so a repo could be left with
   nobody able to administer it and no way back.

Repo → **Members** is the UI. Both membership endpoints authorize to the floor
`ChangeOthersMembership` can never fall below *before* loading anything, so a non-member learns
nothing about which repos or memberships exist.

## Applying a profile to an instance

Repo → an instance → **Sync**. This is the system's central feature; the design and its
reasoning are in [07 — Mod sync design](07-mod-sync-design.md).

1. `SyncPageViewModel` reads the instance's `ActiveProfile` and pulls that profile's
   dependencies — each of which carries its `ContentHash`, so nothing has to fetch the repo's
   mod list to find out what bytes are wanted.
2. `ModSyncPlanner` classifies every mod: Keep, Install, Replace, uninstall-recoverable, or
   quarantine. **The comparison is on bytes**, from the manifest's recorded hash, rehashing only
   files whose size or mtime have moved.
3. The plan is shown before anything is touched, and anything unrecognised — a file the repo
   cannot reproduce — is named in a confirmation that says where it is going.
4. Execution populates the serving store first, from another disk's store where possible and the
   network otherwise, verifying every ingest against its address. Only then does the destructive
   phase run, so it never runs against an incomplete store.
5. On success, and only on success, the manifest is written atomically.

Drift — the folder no longer matching what was applied, which is what an in-game update-all
leaves behind — is reported on this page. Making it visible from everywhere else is
[Phase 4](PLAN.md#phase-4--make-drift-unmissable).
