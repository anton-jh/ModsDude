# Flows

End-to-end walkthroughs of what the system does today. Flows that are designed but not
built live in [07 — Mod sync design](07-mod-sync-design.md).

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
   the `sub` claim and creates one from `sub` + `name`. There is no signup step — the first
   authenticated call *is* the signup.

## Creating a repo

1. Sidebar → **Create repo**. `CreateRepoPageViewModel` lists adapters from
   `IGameAdapterIndex.GetAllLatest()` — latest compatibility version per adapter id.
2. Picking an adapter yields `GetBaseSettingsTemplate()`, a `DynamicForm` rendered by
   `DynamicFormEditor`. Editing it raises `Modified`, which takes the navigation lock, so
   navigating away now prompts.
3. Save validates the name and the form, then `RepoRepository.CreateRepo` serializes the
   settings and calls `POST api/v1/repos/create`.
4. Server: rejects unless `User.IsTrusted`; rejects a taken name with the `name-taken`
   problem; otherwise creates the repo with `AdapterData = (adapterId, serializedConfig)` and
   makes the caller Admin.
5. Client refreshes the repo list and raises `RepoOfInterestChanged`, which `MainPage` uses to
   select the new repo — you land on its page.

**Gate:** a new user cannot do this. `IsTrusted` defaults to `false` and nothing sets it —
someone flips it in the database.

## Connecting a game (creating an instance)

1. Open a repo. If it has no instances, `RepoPageViewModel` **auto-selects "Connect game"** —
   the one thing you must do before the repo is useful.
2. `CreateLocalInstancePageViewModel` builds `repo.Adapter.GetInstanceSettingsTemplate()`.
   For Farming Simulator the constructor has already probed
   `My Documents\My Games\FarmingSimulator2025` and the space-separated spelling, so the path
   is usually pre-filled. The name defaults to "Game" for a first instance.
3. Validation covers the name (non-empty, not already used in this repo) and the form's own
   `PerformValidation` — for FS, that the folder actually exists.
4. Save calls `repo.CreateLocalInstance(name, settings)`, which adds a `LocalInstance` to the
   repo's observable collection. The collection synchronizer writes the persisted model
   through, and `LocalInstanceRepository`'s `CollectionChanged` handler saves `state.json`.

**Nothing is sent to the server.** Instances are per-machine by design; two members of the
same repo have entirely separate instance lists.

> **Changing.** Instances are currently scoped to a repo, so joining three Farming Simulator
> repos means configuring the same installation three times, with three instances silently
> believing they own the same folder. They are moving to adapter scope — configured once,
> listed under every repo using that adapter, with an explicit active profile. See
> [PLAN.md](PLAN.md#settled-architecture-decisions).

## Importing mods from an installed game

Repo → **Mods → Import**. This is the most performance-sensitive path in the app.

1. `RepoModsImportPageViewModel` resolves `IBaseModAdapter` from the repo's adapter, throwing
   a user-friendly error if the game does not support mods.
2. `InitAsync` waits **150 ms** before touching the disk. Dragging the mouse down the sidebar
   builds and discards a page per item it passes; the delay means a page nobody stopped on
   never opens a file.
3. For each `LocalInstance` in the repo, the adapter is bound to that instance's settings and
   `GetInstalledMods` scans its mod folder — every `.zip` opened in parallel, capped at
   `ProcessorCount`, reading `modDesc.xml` out of each.
4. Results are deduped on `(Id, Version)`. A `sources` dictionary records which instances each
   mod was found in, and instance names are only shown on the rows when the repo has more than
   one instance — otherwise every row would name the same one.
5. Rows are sorted by name and wrapped in `ModListItemViewModel`. **No icons are read yet.**
6. `OnInitCompleted` (UI thread) builds the `ICollectionView` with a filter over the search
   box, publishes the counts, and clears the loading flag. It recomputes the visible count
   against whatever was typed *while* the scan was running.
7. As rows scroll into view, `LazyLoad` calls `LoadAsync`, which pulls the icon through
   `ModImageProvider` — memory cache, then disk cache, then decode from the archive.
8. Select rows individually or via Select all / Select none, which operate on the *visible*
   (filtered) set. Selection counting is suspended during bulk operations and recounted once.
9. **Import does nothing.** `ImportAsync` collects the selected mods and hits a `TODO`.

Navigating away cancels the scan through the page's `CancellationTokenSource`, and
`OnInitFailed` swallows the resulting `OperationCanceledException`.

## Registering a mod version (the intended upload path)

The server supports this; the client does not call it yet.

```
Client                          Server                         Blob storage
  │                               │                                 │
  ├─ POST files/createModUploadLink                                 │
  │    { repoId, modId, versionId }                                 │
  │                               ├─ auth: Member                   │
  │                               ├─ reject if version registered   │
  │                               ├─ reject if blob already exists  │
  │                               ├─ mint user-delegation SAS ──────┤
  │◀── { link }  (30 min, Create|Write, one blob) ──────────────────┤
  │                                                                 │
  ├─ PUT the mod archive straight to the SAS URL ───────────────────▶
  │                                                                 │
  ├─ POST repos/{repoId}/mods                                       │
  │    { modId, versionId, displayName, description, attributes }   │
  │                               ├─ auth: Member                   │
  │                               ├─ CheckIfModExists ──────────────▶
  │                               ├─ new Mod, or AddVersion         │
  │                               └─ commit                         │
  │◀── ModDto                                                       │
```

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
the orphan set to a single blob. Several mods can be in flight at once — 4–6, network-bound —
without weakening the invariant, which is about ordering within one mod. See
[09 — Mod catalog](09-mod-catalog.md#import-on-save) for the retry protocol, which the current
endpoints cannot support unchanged.

`ModId` and `ModVersionId` come from the game, not from us — for Farming Simulator, the
archive filename and the `<version>` element.

## Managing profiles

Create, rename and delete all go through `ProfileService`, which after every mutation
refreshes the whole profile list and raises `ProfileOfInterestChanged`. `RepoPageViewModel`
listens and selects the affected profile, so you land on what you just created.

Name conflicts return the `name-taken` problem from the server, which the client matches on
`CustomProblemDetails.Type`.

Editing a profile's **contents** — its mod dependencies — has server-side support
(`GET/POST/PUT/DELETE .../modDependencies`) and a generated client, but
`ProfileModsEditorPage` is still a stub showing hardcoded strings. Note that these endpoints
were unusable until recently — none of them loaded `ModDependency.ModVersion`, so every one
threw on a profile that had any dependencies. They are untested beyond that fix.

## Managing members

1. Find the user: `GET users/search?username=` for an exact match, or `GET users` for
   everyone who already shares a repo with you.
2. `POST repos/{repoId}/members` with a level. The caller must be at least Member **and** hold
   at least the level being granted — you cannot mint an Admin unless you are one.
3. `PUT .../members/{userId}` changes a level. Authorization runs `ChangeOthersMembership`
   (Member to touch a Guest, Admin to touch a Member or Admin) *and* the grant check on the
   new level.
4. `DELETE .../members/{userId}` kicks, refusing the last Admin at both the endpoint and the
   entity.

There is no UI for any of this — Repo → Members is a placeholder page.

## Applying a profile to an instance

**Not implemented, and not currently implementable** — there is no download-link endpoint.
This is the system's central feature. The design is in
[07 — Mod sync design](07-mod-sync-design.md).
