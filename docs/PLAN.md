# Plan

## Where this is going

ModsDude is a personal project for a small group of players, built so that adding another
game is a matter of writing an adapter rather than reworking the system. It is not a
product, and the plan below does not pretend otherwise: no multi-tenancy, no billing, no
public onboarding. What it *does* have to do is work reliably at real data volumes —
1,000–2,000 mods in a profile, thousands of registered versions per repo — because that is
what a Farming Simulator install actually looks like.

The organising principle: **finish one vertical slice end to end before widening.** The
system had a broad, shallow surface — most screens existed, few of them did anything. The
value arrived the first time someone could point a game install at a profile and have it come
out right; that slice is now closed end to end.

## Where it stands

| Area | State |
| --- | --- |
| Identity, users, memberships | Working, with a members UI |
| Repos, adapters, base settings | Working |
| Local instances | Working, scoped to a game and carrying an active profile. [Phase 10](#phase-10--one-game-many-targets) splits the folder from the policy it carries and retires the word |
| Profiles (create/rename/delete) | Working. Creating one can branch off a revision of another |
| Mod dependencies | Server and the profile mod list editor both work. A save is one revision |
| Profile history | Working — every save is a revision, readable, restorable, branchable |
| Mod catalog, import, imagery | Working — sources, merged catalog, real import, server-side derivatives |
| Mod upload / download | Working, both directions, straight to blob storage |
| Profile → instance sync | Working — content store, plan, execute, manifest |
| Drift | Detected at startup and on window activation, surfaced app-wide, re-appliable in one click |
| Savegames | Working end to end — publish, check out, check in, force, keep playing, take a copy, discard. Packing, the checkout binding and slot safety on the client; savegame drift folded into the app-wide notice; repo and instance pages with their dialogs. [Phase 9](#phase-9--one-current-savegame-per-profile) reworks which revision a save runs on |
| Tests | Three projects: server domain, server persistence (needs PostgreSQL), client core |
| CI | Two jobs — Linux for the server and the OpenAPI diff, Windows for the client |
| Deployment | None |

---

## Settled architecture decisions

Decisions taken after the initial write-up. All were prerequisites for Phase 3, and all have
since been implemented; they are kept here as the record of why the shape is what it is.

### Instances are scoped to a game, and an instance is one mod folder

`LocalInstance` was scoped to a **repo**, which breaks as soon as someone joins more
than one repo for the same game: a member of three Farming Simulator repos has one
installation but must configure it three times, and all three instances believe they own the
same mods folder.

An instance is scoped to the **game** instead — configured once, listed under every repo
targeting that game. It also gains an explicit **active profile**, a `(RepoId, ProfileId)`
pair, since sync makes a folder match a profile exactly and only one repo can own a folder at
a time.

**The scope is not the adapter id.** One adapter serves both Farming Simulator 22 and 25, and a
generic scripted adapter would serve a dozen games under one id — so keying instances on
`GameAdapterId` would offer an FS22 folder to an FS25 repo. `IBaseGameAdapter` produces an
`InstanceScope` instead: the adapter id, plus a discriminator its **base settings** decide, and
a repo offers the instances whose scope equals its own. Farming Simulator's base settings gain a
`GameVersion` to feed it. Full reasoning, and the two rules a discriminator has to obey, in
[04 — Game adapters](04-game-adapters.md#game-identity).

**An instance is one mod folder, not one installation.** Games keeping mods in several places
get several instances. BeamNG.drive with BeamMP needs three — singleplayer, MP client, and a
dedicated server all read from different directories, even where two of them belong to the
same install. The model tracks folders and does not assume there is a game installed at all,
which keeps it far simpler than modelling installations with child targets.

**Superseded by [Phase 10](#phase-10--one-game-many-targets).** The half above it — instances
scoped to a game rather than a repo — stands and is what Phase 10 builds on. This half does not:
child targets are simpler *only* while the folder also holds the policy, and moving the active
profile and the savegame hold up to the game is precisely what makes one-per-machine with a list of
targets the smaller model. BeamNG still needs three folders; it no longer needs three of everything
else.

### The content store is per volume and configured machine-wide

Settled as per-volume rather than per-repo. Content addressing makes sharing safe, and the
store is not adapter-scoped either — it holds hashes and has no notion of which game a file
belongs to.

Each disk holding mod folders gets a **store assignment**, chosen by the user: its own store,
materialising by hardlink, or a store on another disk, materialising by copy. The second
trades sync time for space on the constrained disk, and is a legitimate choice rather than a
fallback — the drive with the game on it is frequently not the drive with room on it.

The assignment, store path and maximum size therefore live in a **machine-wide settings bag on
`LocalState`**, keyed by volume, and stay out of both instance and repo settings. See
[07 — Mod sync design](07-mod-sync-design.md#where-the-store-lives).

### No migration

The system has no users. `LocalState.Version` was bumped to 2 and old state is discarded rather
than migrated. `Store<T>` takes a compatibility predicate so that the bump actually
discards rather than deserializing old JSON into the new shape.

### One schema pass, before any real data exists

Four separate items below each carry a "doing this later means a backfill" warning:
`ModVersion.ContentHash`, mod id casing normalization, `ModVersion`'s image references, and
`Mod.Locked`. They are the same warning, and while the database is empty they are also the same
afternoon.

So treat them as **one schema change made up front**, not four spread across three phases. The
phases below still describe the behaviour each one unlocks, but none of the columns should be
added late. The moment somebody bulk-imports two thousand mods, every one of them becomes a
migration with a data-repair step.

That is what happened: the flattening, `ContentHash`, `Locked`, the image references and the
mod-id casing normalization all landed together while the database was empty.

---

## Phase 0 — Unblock

Small, mechanical, and everything downstream depends on it. Nothing here is more than an
afternoon.

- [x] Fix `Mod.GetNextSequenceNumberForVersion` — the missing `+ 1`. Mod versioning was
      unusable without it.
- [x] Fix `Mod.RemoveVersion` — validate before mutating.
- [x] Fix `Mod.InsertVersion` — capture the target position and materialise the shift query,
      so the result no longer depends on `HashSet` iteration order.
- [x] Register `IModsClient` and `IFilesClient` in `AddModsDudeClient`.
- [x] Load `ModDependency.ModVersion` where the domain needs it. Every mod-dependency endpoint
      threw a `NullReferenceException` on any profile that had dependencies, because the
      navigation is not auto-included and every domain operation reaches the `Mod` through it.
      The read endpoint projects; the write endpoints use `GetWithModDependenciesAsync`.
- [x] Make `ProblemType` survive the wire. `System.Text.Json` ignores `[EnumMember]`, so the
      server sent bare member names while the generated client — built from an OpenAPI document
      carrying the URIs — could not parse them. Both attributes are now present.
- [x] Branch the client on `CustomProblemDetails.Type` instead of HTTP 409.
- [x] Stop `DELETE repo/{repoId}` failing at the database. The `Mod` → `Repo` FK is `Restrict`,
      so deleting a repo with mods was a 500. It refuses with `repo-not-empty` instead.
- [x] Project the two profile read endpoints. `ModDependencies` is owned, so materializing a
      `Profile` read its whole dependency set for a DTO that does not carry it.
- [x] Atomic writes and a compatibility gate in `Store<T>`.
- [x] **Flatten `Mod` and `ModVersion` into one entity** keyed `(RepoId, ModId, VersionId)`. A
      mod record is really *of* a version, not a container of them, and nearly all the data was
      already on the version. Removes the create-or-append branch in registration, the shadow FK
      properties and owned-collection mapping, the `Versions` auto-include that makes
      `GET repos/{id}/mods` so heavy, and the `Mod.RepoId` TODO. Full reasoning in
      [02](02-domain-model.md#flattening).
- [x] **Flatten the wire format with the entity.** `ModDto` nested `ModVersionDto[]`; it is now
      one DTO per version. Leaving the response nested would make
      the client re-group on receipt — exactly the shape the flat client model exists to avoid.
- [x] Keep `SequenceNumber` **contiguous**, with the existing shift-on-insert and close-on-remove
      logic — just moved off the entity into `ModVersionSequencer`, since there is no parent left
      to hang it on. A sparse
      key was considered and rejected: the shift is tens of rows for one mod, mutated in memory
      and written by one atomic `SaveChanges`, so it was never the problem the aggregate was
      solving. Gaps would only add an exhaustion case to reason about in exchange for nothing.
- [x] `ModDependency.CanBeUpgraded()` / `Upgrade()` lose their `ModVersion.Mod.Versions`
      navigation; both now take the sibling versions as a parameter, supplied by the endpoint that
      had to query for them anyway.
- [x] Do the flattening **before** anything registers mods in earnest — one migration and no real
      data made it nearly free, a data migration later. Same argument as the casing fix.
- [x] **Regenerate `Generated.cs`** — it predated `ProblemType.RepoNotEmpty`, so the
      client could not parse that problem. The checked-in `nswag-config.nswag` also had to be
      fixed first: it pinned runtime `Net80` and named no output path, so it failed twice over
      against the .NET 10 toolchain. See [03 — Server](03-server.md#regenerating-the-client).
- [x] Add `ModVersion.ContentHash` (SHA-256, a first-class property — not a `ModAttribute`),
      populate it on registration, and expose it on **both** `ModDto` and
      `ModDependencyDto`. Sync reads the profile's dependencies, not the repo's mod list; without
      the hash there, every sync would have to pull the whole `GET repos/{id}/mods` to resolve
      it. Everything in Phase 3 depends on this, and adding it later means a backfill.
- [x] **Record the uploaded file's hash against the blob** — blob metadata under the key
      `CreateModUploadLink` names in its response, since Azure's built-in content hash is MD5 — so
      that `FileAlreadyPresent` can return it.
      Without it, adopting an orphaned blob registers a hash describing a file nobody has, which
      no download can ever satisfy and no upload link can ever repair. See
      [07](07-mod-sync-design.md#hostile-or-wrong-hashes-have-to-be-unregisterable-not-just-undownloadable).
- [x] Move the server base URL into the WPF `appsettings.json` and apply it in
      `AddModsDudeClient`. Blocked ever running against a deployed server.
- [x] Add a test project for `ModsDude.Server.Domain` and cover version sequencing,
      membership transitions, and the dependency rules, including named regressions for the two
      sequencing bugs above. A second project, `ModsDude.Server.Persistence.Tests`, covers what
      only a real PostgreSQL can answer.

## Phase 1 — The mod catalog and the upload loop

Make the import page actually import, on top of a representation the profile editor can reuse.
Full design in [09 — Mod representation and the catalog](09-mod-catalog.md).

- [x] **Normalize mod id casing at the adapter boundary** and carry it in a key type — `ModKey`,
      whose only representable form is the normalized one, so no path can hand raw casing to blob
      storage. Done before anything registered mods in anger; afterwards it is a data migration.
- [x] `IsLocal` / `IsOnServer` per **version**, not per mod. Derived three-state where a page
      needs it, never stored. `ModStatus` split into these facts plus a per-context
      `ModDisplayStatus`.
- [x] A merged **flat** `CatalogModVersion` in `Client.Core` — one record per version, no
      parent — so one row view model serves local, server and both. Flat all the way through:
      the server entity is flat, `LocalMod` is already one record per file, and a row view model
      wraps one version, so a parent would be a shape invented mid-pipeline. Grouping for the
      version selector and update detection is a `ToLookup(x => x.ModId)` built where needed,
      which beats maintaining a nested model that must be rebuilt every time a source checkbox
      recomposes the set.
- [x] Rename `LocalModImage` → `ModImage`. **Delete** the client-side `Mod` that wraps `ModDto`
      rather than renaming it — nothing referenced it but `ModFakers`, and its only job was the
      latest-versus-older grouping the lookup now does on demand. Both are gone, and the Bogus
      package reference that existed only for the fakers with them.
- [x] **Mod sources.** Scan a set of sources rather than a fixed folder: every instance's mod
      folder, the system Downloads folder, plus folders the user adds for the session with the
      folder browser. List them all, each with an enable/disable checkbox. **Disabled standing
      sources persist**, machine-wide in `LocalState.Settings` keyed by source id — not per repo,
      since "do not look in this folder" is a fact about the folder. Ad-hoc sources stay
      view-scoped. Disabling an instance as a source must not affect syncing to it. Locate
      Downloads via
      `SHGetKnownFolderPath`/`FOLDERID_Downloads` — .NET has no `SpecialFolder` for it and a
      relocated Downloads is common, so `%USERPROFILE%\Downloads` is a fallback, not the first
      try. `GetModsFromFolder` already takes an arbitrary path and already skips non-mods, so
      the adapter contract needs nothing new.
- [x] Extract a repo-scoped `ModCatalog` service: merge the source scans with
      `GET repos/{id}/mods`, **caching per source and composing the merged view on demand** so
      that toggling a checkbox is instant and adding a source scans only that folder. Cache the
      **`Task`** rather than the result so concurrent callers join one scan. Invalidate
      explicitly on import and on instance-settings change, expose Rescan per source and for
      all, and report per-source failure so an unplugged drive marks one source bad rather than
      failing the catalog. Move the 150 ms delay and the cancellation behaviour in unchanged.
- [x] **Split the upload-link problem types** into `FileAlreadyPresent` and `AlreadyRegistered`.
      Without this a mod whose import failed after upload can never be retried.
      `FileAlreadyPresent` carries the existing blob's hash: matching it means this is our own
      orphan and registering is safe, differing means an id/version collision to report rather
      than register over — and a blob predating the metadata answers `null`, which the client
      treats as "nothing established, do not register".
- [x] Implement import: per mod, link → upload → register, five mods concurrently. Per-mod
      ordering protects the never-register-before-upload invariant; concurrency across mods
      does not weaken it. `AlreadyRegistered` counts as success, which makes retry
      idempotent and covers a teammate importing the same version concurrently.
- [x] Handle **several new versions of one mod in a single import** — one from a mod folder, one
      from Downloads. Compute positions against the final intended order, then register in
      ascending order as *insert before the next known version*, so each step is individually
      valid and no batch-placement API is needed. Versions of the same mod must register
      **sequentially**, since each insert depends on the previous; concurrency stays across
      distinct mods. Note this can move an already-registered version's sequence number.
- [x] **The version comparer and the arbitration dialog are prerequisites of this**, not Phase 7
      work — placing several incoming versions needs them. They landed here. See the note on
      Phase 7.
- [x] **Assert both neighbours when placing a version**, not just the one to insert before.
      *Insert v2 between v1 and v4*, rejected if v4 no longer immediately follows v1. Relative
      placement alone stops collisions but still permits a silently wrong order when two members
      insert against a state neither has seen the other change — which offers a downgrade as an
      upgrade. Optimistic concurrency using only what the client already computed, retried
      through the refetch loop import already has.
- [x] Per-row progress and error state — a per-row phase, and a failure distinguished from a
      skip. At two thousand mods a single global spinner cannot
      distinguish a working import from a hung one.
- [x] Compute the SHA-256 while uploading and send it with registration. It comes off the same
      buffer the upload blocks are cut from, so the file is read once.
- [x] **Write imported files into the content store as they go**, so importing an existing install
      leaves the store warm. Written when the store did not exist and closed once it did: a
      registered version is copied into the store serving the repo's mod folders, verified against
      the hash the registration recorded, after the registration and never able to fail it.
      **A file that is already in one of those mod folders is skipped** — sync keeps it where it is,
      so copying it into the store would duplicate tens of gigabytes to save nothing, and it reaches
      the store for free on the uninstall that displaces it. The case this is for is the other one:
      a mod imported from Downloads, which the first sync used to fetch back from the server.
- [x] **Store the icon and every store image server-side**, so a mod nobody has locally still
      renders with its artwork instead of initials and an empty details dialog. Full design in
      [09](09-mod-catalog.md#mod-imagery).
- [x] Two derivatives per image — a 128 px thumbnail and a full at native resolution
      capped at 1024 px, as WebP — generated **client-side at import**, since only the
      client can decode DDS (including the managed BC7 path) and the server has no business
      opening mod files. Store art is only 1.2% of archive bytes
      and tops out at 1024 px, so the full derivative is a **re-encode, not a downscale**; the
      saving is DDS to WebP. The thumbnail is what matters: it turns a cold 540-row list from
      tens of megabytes into a few. Both derivatives came in at roughly half the estimates this
      was written against — the measured figures are in
      [09](09-mod-catalog.md#what-the-volumes-actually-are).
- [x] No separate storage of originals — they are already inside the mod blob. The case against
      shipping them is transfer and decode (roughly an order of magnitude more bytes, plus a
      managed BC7 decode, to render the same 64 px), not storage, which measurement shows would
      have been affordable.
- [x] **Registration decides the imagery source, not local availability.** Registered versions
      always render from the server's derivatives, even when the mod file is on this machine;
      only unregistered import candidates are extracted from their archive. Hunting for the local
      file to gain resolution nobody wants in a 96 px strip costs exactly the work derivatives
      exist to avoid. It also gives stable hash cache keys, uniform presentation across a list,
      and means nothing ever reads images out of the content store.
- [x] **Opportunistic backfill.** Since imagery never blocks registration, a version can exist
      with no derivatives. A client about to render one while holding the mod file should
      generate and upload them rather than fall back locally — closing the gap for everyone, and
      removing the need for a separate backfill sweep.
- [x] Content-address the image blobs in their own container, `mod-images/{hash[0..2]}/{hash}`.
      Versions overwhelmingly reuse artwork between releases, so dedupe collapses ~15,000
      references to ~3,000 blobs for a 3,000-version repo. Server storage is not a constraint
      here; transfer and decode are.
- [x] **A machine-wide client image cache**, configured in `LocalState.Settings` beside the
      stores with its own path, size cap and LRU. One per machine, not per volume — images are
      always copies, so the hardlink constraint that makes stores per-volume does not apply.
      Keep it distinct from the content store, which is what keeps "nothing reads images out of
      the content store" true. Cache server derivatives by their own hash, with no size suffix,
      since they arrive pre-sized.
- [x] `ModVersion` gains an **ordered collection of image references** (hash, kind, **rendition**,
      position, filename) — structural, so not `ModAttribute`s. References, not ownership: a blob is
      collectable once nothing points at it. `Rendition` is not in the list above because the
      original model had no field for it; see
      [09](09-mod-catalog.md#two-sizes) for why one was needed.
- [x] **Imagery must never block registration.** The mod file is verified before metadata is
      written; images get the opposite treatment, uploaded best-effort after the fact and picked
      up by the opportunistic backfill above. An import of 2,000 mods cannot half-fail over a
      timed-out thumbnail.
- [x] A **batch existence check** before uploading — "which of these hashes do you have?" After
      the first import most images are already present, and 2,000 mods x 20 images is 40,000
      uploads that mostly need not happen.
- [x] Serve them through the API — `GET images/{hash}`, redirecting to a SAS or streaming —
      rather than per-image SAS minting, which inverts the mod-file trade-off for files that are
      small and fetched in bulk. Affordable because a content-addressed image is immutable and so
      cacheable forever; the client's existing disk cache then fetches each one once per machine,
      ever. The route has no `repoId` and cannot have one, so the check is **authenticated user**,
      not Guest of a repo — document that widening rather than implying a scoping it lacks.
- [x] **Verify image bytes on ingest**, client-side at minimum. The image blobs are one globally
      shared address space cached permanently by hash, which is exactly the shape
      [07](07-mod-sync-design.md#cache-isolation) says is only safe when every ingest is
      verified. Done on both sides: the upload endpoint refuses bytes that do not hash to the
      address they were sent to, and the client verifies what it downloads before caching it.
- [x] Stop dropping `Description` when mapping the server's mod DTO. The client-side `Mod` that
      dropped it is deleted; `CatalogModVersion` carries the description for local and registered
      versions alike.

## Phase 2 — Profile contents and mod management

> **Superseded in part by [Phase 4.5](#phase-45--profile-revisions).** The mod list editor is
> unchanged; the four per-dependency routes it wrote through are gone, replaced by one save of
> the whole list. Items below that name `POST/PUT/DELETE .../modDependencies` or
> `.../modDependencies/upgrade` describe what was built at the time, not what is there now.

A profile was a name with nothing in it, and Import and Manage were separate pages
showing overlapping data under different rules.

- [x] **Merge Import into Manage.** One list over the catalog, presence filter chips
      (All / In repo / On disk only / Unused), bulk import as a selection mode that reveals the
      footer bar the import page already has.
- [x] Put the source list with its checkboxes on both Manage and the profile editor. Show which
      source a row came from only when more than one is enabled — with a single source it is
      noise on every row.
- [x] Add profile-usage information to `ModDto` or a dedicated endpoint. "Unused" cannot be
      computed client-side safely — deleting on a partial view risks removing a version a
      teammate's profile just picked up. **It got its own endpoint**, `GET repos/{repoId}/mods/usage`:
      usage changes when a *profile* is edited and not when a version is, so folding it into the
      mod list would have meant either serving stale usage to every client syncing incrementally,
      or restamping `Updated` on every version a profile save touches — two thousand rows a save,
      and a delta the size of a full listing.
- [x] **Reorder a mod's versions by hand** from Manage — the backstop for an ordering that is
      wrong for reasons optimistic concurrency cannot catch: a comparer that guessed badly, or an
      arbitration someone regrets. Same operation the arbitration dialog already performs.
- [x] Add delete endpoints for a mod version and for a whole mod. "Remove whole
      mod" needs its own path, since the per-version delete refuses the last one. **They delete the
      blob too** — nothing anywhere reclaimed blob storage, and adding a second way to strand
      bytes before there is a way to reclaim them makes the problem worse rather than deferring
      it. This is also what unblocks `DELETE repo/{repoId}`, which refuses a repo that still
      has mods and therefore could not be used at all until a repo could be emptied. A version a
      profile pins is refused rather than cascaded, and the dependency foreign key is `Restrict` so
      the database enforces the same rule; the database commit precedes the blob delete, because a
      stranded blob is recoverable and a registration whose blob is gone is not.
- [x] Replace the `ProfileModsEditorPage` stub with the two-list editor over
      `GET/POST/PUT/DELETE .../modDependencies`. The left list is the union of registered and
      local mods, so a mod can be added *and imported* without a detour to another page.
- [x] **Updates render on the right, not the left** — an in-profile mod with a newer version
      shows an update affordance on its own row, plus an "N updates available" batch action.
      Putting it on the left would place the same mod on both sides at once.
- [x] Right-hand rows carry a version selector and a `Locked` toggle, since the list is
      keyed by `ModId` and moving a mod rightward means choosing a version.
- [x] **Import-on-save, not import-on-drag.** A local-only mod moving right is marked pending;
      Save uploads and registers, then updates the dependencies last in one request. Uploading
      on drag makes Cancel meaningless and litters the repo with mods nobody kept.
- [x] **`ModVersion.Locked`** — a new domain property and column, alongside the existing
      per-profile flag. Rename `ModDependency.LockVersion` → `Locked` to match, and add
      `IsEffectivelyLocked => Locked || ModVersion.Locked` so the rule lives in one place.
- [x] **The adapter sets `ModVersion.Locked` at every registration**, re-derived from the file
      rather than inherited — a Farming Simulator map mod declares its maps in `modDesc`, which
      the adapter is already parsing, so the answer comes out the same for every version. No
      prompt at import. An adapter can never set `ModDependency.Locked`.
- [x] **`RegisterModRequest` grows a `Locked` field** — the adapter's determination has no way
      to reach the server otherwise.
- [x] Accept the consequence: **no repo-wide user override.** Someone who disagrees with the
      adapter unlocks on the dependency, which is per-profile and survives version changes since
      `ChangeVersion` does not touch it. "Unlock" means "in my profile", not "in this repo" —
      the price of collapsing `Mod` into the version.

      This is also why no mod-mutation endpoint is needed. A `PUT repos/{repoId}/mods/{modId}`
      would be the obvious companion to a repo-wide, user-editable flag, and there is no endpoint
      that mutates a `Mod` today — but with `Locked` on the version and the override living on
      the dependency, `PUT .../modDependencies/{modId}` already carries it.
- [x] **Not a `ModAttribute`.** Attributes are tags and categories and the system must never
      depend on one — `Locked` changes what a batch update is allowed to touch, so it is a real
      property. Same rule that put `ContentHash` in the schema.
- [x] **Decide what "newer" means before shipping the update actions.** Everything below reads
      `SequenceNumber`, which would otherwise be registration order — folder scan order, for a repo
      built by bulk-importing an install. "Apply all updates" would then move mods to whatever
      happened to be registered last, confidently and in bulk, on the exact action the locking
      design exists to make safe. Resolved by pulling Phase 7's comparer forward into Phase 1, so
      `SequenceNumber` is a derived ordering by the time any update action reads it.
- [x] Expose `ModDependency.Upgrade` / `CanBeUpgraded`, which existed on the domain with no
      endpoint. It is a **batch** — `POST .../modDependencies/upgrade` — since a profile holds one
      to two thousand mods and N round trips is the wrong shape.
      **"Apply all updates" skips locked mods entirely** and reports what it skipped
      ("Update 47 mods · 3 locked, skipped"). Save then cannot contain an unintended version
      change and needs no prompt at all. Sweeping locked mods in and prompting at save re-asks a
      question the user already answered, every time — which is how a safety prompt turns into
      noise people learn to dismiss.
- [x] Changing a locked version is a deliberate per-row act with its own confirmation, carrying
      the reason it is locked. For bulk, make the skipped-count a link to a modal listing the
      locked mods with an **unchecked checkbox each** and the consequence spelled out per mod —
      the same dialog, reached deliberately rather than fired at every save.
- [x] Add the unique index on `(RepoId, ProfileId, ModId)`, so the one-version-per-mod rule is
      enforced by the database and not only by `Profile.AddDependency`.

## Phase 2.5 — Rework instances and add global settings

Client-only, no server changes. Small, but it has to land before sync, because sync needs to
know which profile owns a folder and where the store lives.

- [x] Add `InstanceScope` and `IBaseGameAdapter.Scope`, defaulting to the adapter id alone. An
      adapter serving more than one game overrides it from its base settings — see
      [04 — Game adapters](04-game-adapters.md#game-identity).
- [x] Give `FarmingSimulatorBaseSettings` a `GameVersion`, required and not `[CanBeModified]`,
      and read it in both `FarmingSimulatorBaseGameAdapter.Scope` and the game-data-folder probe
      in `FarmingSimulatorInstanceSettings`, which had hardcoded `2025`.
- [x] Move `CanSupportMods` / `CanSupportSavegames` from `IGameAdapter` to `IBaseGameAdapter`.
      Same layering mistake one stage up: for a scripted adapter the answer depends on base
      settings. Nothing reads either flag yet, so it is free now and breaking later.
- [x] Move instances out from under repos in `LocalState`: key by instance id, carry
      `InstanceScope` and `GameAdapterId` instead of `RepoId`. Bump `LocalState.CurrentVersion`;
      `StateStore`'s compatibility predicate already discards anything older.
- [x] `Repo` offers the instances whose `InstanceScope` equals its own, rather than owning a
      list. The sidebar keeps listing them under each repo exactly as it does now.
- [x] Refuse a second instance pointing at a folder another instance already owns, **across all
      scopes**. Adapter scope used to make this impossible; splitting it by game does not. Needs
      the adapter to be able to answer which folder an instance owns.
- [x] Add `ActiveProfile: (RepoId, ProfileId)?` to the persisted instance.
- [x] Add `LocalState.Settings` — the first machine-wide client setting — holding, per volume,
      which store serves it, that store's path, and its maximum size.
- [x] A settings page — reached from the top-level sidebar. There was no such page, so the app had
      nowhere to put a global setting.
- [x] Rework `CreateLocalInstancePage` / `EditLocalInstancePage` accordingly. The name
      uniqueness check moves from per-repo to per-adapter. Phase 4 reworks `EditLocalInstancePage`
      again into the instance's real page — worth doing as one piece of work rather than touching
      the same page twice.

## Phase 3 — Sync

The core feature. Full design in [07 — Mod sync design](07-mod-sync-design.md).

- [x] `POST api/v1/files/createModDownloadLink` — Guest-level, read SAS, mirroring the upload
      endpoint.
- [x] The content store: `{storeRoot}/blobs/{hash[0..2]}/{hash}`, **per volume**, shared by
      every repo and instance it serves, with a configurable location and maximum size.
- [x] **Per-disk store assignment.** For each disk holding mod folders, let the user choose
      between a store on that same disk (hardlink) and a store on another disk (copy). The
      disk with the game on it is often not the disk with room on it: mods on a small `C:`
      served by a store on a roomy `D:` means `C:` holds only the active profile while the
      cache history — the part that grows — lives on `D:`. It costs sync time, since every
      install and replace becomes a cross-volume copy. Present both sides of that trade-off in
      the settings UI, and treat cross-disk as a deliberate choice, not a misconfiguration to
      warn about.
- [x] **Verify on ingest.** Hash every download and compare it against what the server
      declared before storing. This single check is what makes a shared store safe between
      repos; without it the whole isolation argument collapses.
- [x] Materialisation: hardlink where a disk is served by its own store, copy otherwise.
      Warn only when a same-disk assignment silently falls back to copying — exFAT, a network
      path — since that is the case where the user is paying the cost without having chosen
      it.
- [x] **Install looks across stores before downloading.** Serving store first, then any other
      disk's store — copying the blob into the serving store — and only then the network. Safe
      because every store is content-addressed, so a blob at address `H` is content that
      hashes to `H` wherever it sits. Hash it as it streams past during the copy; the bytes
      are already in memory, so verification is nearly free.
- [x] **Uninstall keeps a copy only when nothing else has one.** If any store on the machine
      already holds the hash, delete the mod folder's file outright. Move it into the serving
      store only when no store has it at all — otherwise a mod that lives on `D:` gets
      needlessly duplicated onto `C:` just to be uninstalled.
- [x] LRU eviction of store blobs against the size limit, counting only entries the store
      uniquely holds (link count 1), since anything hardlinked into a live mod folder reclaims
      nothing. Never evict what an active profile needs.
- [x] Extend `IInstanceModAdapter` with the write side: where a mod file belongs, what it
      should be called, install, uninstall. Filesystem operations stay in the sync engine;
      adapters only supply paths. It came out as paths **only** — `GetModFilePath` and
      `GetInstalledModPath`, with no `InstallMod`/`UninstallMod` taking a stream. Materialising is
      a hardlink on one disk and a copy on another, which depends on the store assignment and the
      filesystem rather than on the game, so it belongs in the engine once instead of in every
      adapter.
- [x] **Classify on bytes, not just on version id.** `GetInstalledMods` reads the version out of
      the mod's own metadata, so two builds both calling themselves `1.0.0` look identical to it
      — the case [09](09-mod-catalog.md#same-mod-several-sources) says happens in practice. Use
      the sync manifest's recorded hash, falling back to rehashing only files whose size or mtime
      no longer match it. Without this, content addressing protects the store and does nothing
      for the mod folder.
- [x] `ModSyncService` in `Client.Core`: plan, then execute. Populate the serving store with
      everything **this profile** needs first — not the repo's full mod set — so the
      destructive phase only ever runs against a complete store.
- [x] **Uninstall rules, exactly as designed.** A version registered in the repo is
      recoverable — make sure some store has it, then delete. Anything unrecognised goes to
      the Recycle Bin, never to `delete`. Warn before executing, list what is affected by name,
      and say where it is going. This is the rule that makes the tool trustworthy; it is not
      negotiable for a shortcut.
- [x] A sync page: plan summary (install / replace / uninstall / quarantine), the
      confirmation, per-mod progress, cancellation.
- [x] **Write a sync manifest** on completion — installed files with hash, size and mtime — so
      drift detection is a directory listing rather than opening 2,000 archives. One file per
      instance at `manifests/{instanceId}.json`, beside `state.json`: not inline in `LocalState`,
      which is loaded eagerly and rewritten on every instance change, and not in the game's own
      folder, which an in-game updater rewrites.
- [x] Keep the two records distinct. `ActiveProfile` is a **source of truth** — a folder cannot
      tell you which profile it was meant to be, so losing it loses the intent irrecoverably. The
      manifest is an **optimisation** — reconciliation works without it, straight from folder
      contents against the profile, so losing it costs a scan and nothing more.
- [x] Comparing the manifest's mod set against the profile's current dependencies also catches
      **someone else having edited the shared profile** since this instance synced — no revision
      number on `Profile` required, which is just as well since it has none.
- [x] Handle a **dangling `ActiveProfile`** (profile deleted, or the user removed from the repo):
      say so and offer to pick another, rather than failing or reporting drift against something
      unreachable. And `ActiveProfile` with no manifest — fresh install, discarded state — falls
      back to a full reconcile.
- [x] **Drift detection**, and a Re-apply affordance. In Farming Simulator mods are updated
      *inside the game*, which silently leaves
      the instance not matching its profile; nothing anywhere told the user, and the
      re-apply that protects their save is the step easiest to forget. Detect and offer — never
      revert silently, which would undo updates the user deliberately made. `InstanceDriftService`
      answers from a directory listing against the manifest, and the instance's Sync page shows the
      result with re-apply as the same button that applies. Phase 4 carried it to the repo and
      profile overviews and to the app-level notification.
- [x] **Call out drift on a locked mod specifically.** An unlocked mod at the wrong version is
      untidy; a locked map at the wrong version is a damaged savegame waiting to happen. Name the
      consequence rather than folding it into a count. Delivered with Phase 4's notification,
      which also drove `SyncManifest` to version 2: a v1 manifest records no `Locked` flag and
      would read as "nothing locked", which is the false negative this bullet exists to prevent.
- [x] Let the drift notice double as an import prompt — the drifted files are by definition
      versions the user now has and the repo may not, so "the game updated 6 mods, import them?"
      is the next step of the flow they were about to perform anyway.
- [x] **Test whether the in-game updater rewrites mod files in place or renames over them.**
      In-place rewriting through a hardlink corrupts the shared store blob; rename-over breaks the
      link harmlessly. Answering it needed the real game, which no amount of reading the code
      substitutes for. It renames over, so
      `FarmingSimulatorBaseModAdapter.SupportsHardlinks` is `true` and the main game has its fast
      path: a hardlink wherever a disk is served by its own store.
- [x] **Decide whether store blobs should be read-only. They are not — the exposure is detected
      instead.** Read-only fails loudly instead of corrupting silently, but on Windows the attribute
      also blocks unlinking a name, which is precisely the harmless thing the updater does when it
      renames over a mod file; it would break the one update path the test confirmed, and silently
      break `Evict` and `Clear` besides. An ACL denying writes while leaving delete granted would
      separate the two, and was rejected for putting Windows-only security code into an otherwise
      pure-`System.IO` `ContentStore`, for degrading silently on non-NTFS volumes, and for needing
      another session with the real game to confirm which replace API it calls.
      `StoreIntegrityService` catches the rewrite instead: a rename-over leaves the mod folder a
      *new* file, so comparing file identities — two handle opens, no bytes read — tells the two
      apart, and only a blob that fails that is re-hashed against its address. A confirmed one is
      dropped, which leaves the user's updated file alone and costs a re-download. Findings are
      accumulated on the monitor rather than recomputed, because deleting the bad blob destroys the
      evidence that produced them. See
      [the design](07-mod-sync-design.md#detecting-a-rewritten-blob).

**Done means:** two people on two machines pick the same profile and end up with byte-identical
mod folders, and neither loses a file they cannot get back.

## Phase 4 — Make drift unmissable

> **Done.** Detection was already in place from Phase 3; this phase is the surfacing, the
> re-apply flow and activation. One consequence worth knowing: `SyncManifest.CurrentVersion`
> went to 2, because a v1 manifest records no `Locked` flag and would deserialize as "nothing
> locked" — precisely the false negative the phase exists to prevent. A discarded manifest
> costs one reconcile.

The flow this exists for: the user launches the game themselves, installs mods and runs
update-all from the game's own menus, and comes back. ModsDude was never in the path, so
launching the game from it does not help. What matters is what the user sees on returning.

- [x] **An app-level notification, visible from every view** — not a banner belonging to one
      page. States that installed mods no longer match the applied profile, and persists until
      handled or dismissed. Lives in the shell beside the modal slot `MainWindowViewModel`
      already owns, but is **not** modal: the user must be able to keep working.
- [x] Suppress it in exactly one place: the drifted profile's own mod list editor.
- [x] Two actions — open that profile's mod list editor, or **re-apply the profile directly in
      one click**. Most of the time nothing needs changing and the user only wants their locked
      versions back.
- [x] Dismissal lasts until the drift set changes or the app restarts, never permanently. A
      dismissed warning that never returns is a savegame silently at risk.
- [x] **Save in the mod list editor re-applies by default.** The re-apply is what actually
      reverts an auto-updated locked map; making it a separate deliberate step is exactly how it
      gets forgotten. Applies to whichever instances have that profile active.
- [x] *Save and apply* is the primary button at **one click**; *Save only* costs **at least one
      more**. Prefer a **split button** with the variant in its dropdown over a checkbox that
      retargets the main button: a checkbox is persistent visible state, and someone who ticks it
      once and leaves it ticked has silently turned a per-save decision into a standing mode. If
      a checkbox is used anyway it must reset after every save.
- [x] Word it with the consequence, not just a caution — *"saves the profile but leaves your
      installed mods untouched; your locked mods stay at the versions the game updated them to.
      Only if you know exactly what you are doing."*
- [x] **Derive the apply targets; never ask.** They are exactly the instances whose
      `ActiveProfile` is the one being saved. No checklist, no dropdown, no pre-selection — those
      all come from confusing *re-apply* (target determined) with *activate* (target chosen). A
      drifted instance is already in the derived set by definition.
- [x] Scale the UI with the count: one instance shows nothing at all — the word "instance" never
      appears, which is the common case for most games. Two or more shows *Save and apply to N
      instances* with a read-only disclosure listing them. Zero shows plain *Save*.
- [x] Offer activation as a **follow-up** when nothing is using the profile — *"No instance is
      using this profile. Use it on X?"* — rather than folding a mode change into a save.
- [x] Move activation onto the instance itself. `EditLocalInstancePage` becomes the instance's
      real page: name, settings, active profile as a dropdown, drift status, Re-apply.
- [x] **Activation from the profile side too**, on the profile *shell* so it is present on every
      sub-page rather than just Overview — `ProfilePageViewModel` already owns that
      sub-navigation. A dropdown to pick the instance when the adapter has more than one, and a
      plain button when it has one. Eligibility is by matching `InstanceScope`.
- [x] Label the control for what it will do: *Re-apply* when the instance is already on this
      profile, *Activate* when it is on another or none. Activation re-syncs the folder and
      uninstalls what the previous profile put there, so use the reconciler's plan preview as the
      confirmation rather than a bare "are you sure".
- [x] While the mod list editor holds unsaved changes, disable the shell-level control and point
      at *Save and apply* — otherwise it silently applies the last-saved profile behind the
      user's pending edits.
- [x] An instance that cannot be applied to right now — a dedicated server mid-session, a folder
      held by a running game — is reported and left drifted, which the drift notification already
      covers. That is a "not now", not a "not this one", so it needs no pre-selection.
- [x] **The manifest comparison is the primary mechanism**, at startup and on window activation
      (debounced — `Window.Activated` fires on every alt-tab). It is the only one that works when
      ModsDude is closed while the game runs, which is the normal case. `FileSystemWatcher` is a
      latency optimisation on top, for when the app happens to be open; the design must not
      depend on having been running.
- [x] The manifest is **frozen between syncs** — written on completion, never updated to follow
      the folder. A manifest that tracked the folder could not detect anything, since drift is
      the difference between the two.
- [x] Write it **only on success**, atomically. A half-finished sync leaves the previous manifest
      and the next check reports drift, which is true; a partial manifest would claim a state
      that never existed.
- [x] The cheap check is a **directory listing**, not opening archives: name, size and mtime from
      one non-recursive `EnumerateFiles` catches additions, removals and replacements. Open
      archives only for entries that actually differ. The recorded hashes are not read on this
      path — they are there so an uninstall knows which store blob a file matches.
- [x] An unreachable folder — unplugged drive, offline network path — is **unknown, not
      drifted**. Say so quietly rather than warning about mods that may be fine.

Sequenced right after sync, ahead of the cosmetic work below: it closes the one failure mode
that silently damages savegames.

## Phase 4.5 — Profile revisions

> **Done.** A profile's mod list is now a chain of immutable snapshots. Three things drove it:
> an old list should stay readable, branching off one should be possible, and a rollback should
> not require anybody to reconstruct a list by hand.

The design decisions, and why they went the way they did, are in
[02 — Domain model](02-domain-model.md#profile-revisions). The short version:

- [x] **`ProfileRevision`, keyed `(RepoId, ProfileId, Number)`, holding a snapshot** — not a
      changeset. The mod list *is* the profile, so an event log would make every read of history
      a fold and turn the one-version-per-mod rule from an index into a hope. Rows are the cost:
      two thousand narrow rows per revision, which at these volumes is the cheap half.
- [x] **Read-only by having no address.** No route names a revision to write to; writes address
      the profile and mean its head. No `IsReadOnly` column, and therefore no fifteen places that
      have to check one.
- [x] **One save is one revision**, which required the save to become atomic:
      `PUT .../profiles/{profileId}/revisions` carrying the whole list replaces the four
      per-dependency routes. Those went entirely, the batch upgrade included. It is also a better
      shape on its own merits — one request instead of up to two thousand.
- [x] **`BasedOn` makes concurrent edits safe.** A save names what it was built from and is
      refused if that is no longer the head; the primary key on the revision number is what makes
      that true rather than likely. The editor turns the refusal into a choice, and both answers
      are safe because what is on the server is a revision either way.
- [x] **Restoring copies forward**, never backwards and never by deleting. Revision 3 restored
      onto head 8 becomes revision 9. Moving the head back would strand 4–8 and force a tree the
      moment anybody saved; deleting them would destroy the record of what people ran.
- [x] **Branching is the same primitive**: `POST .../profiles` with `CopyFrom` materializes a
      revision as revision 1 of a new profile.
- [x] The history page — revisions on the left, what the selected one pinned on the right.
      Readable at Guest, actionable at Member.
- [x] The sync manifest records which revision was applied.

The consequence to keep in view: **a mod version that has been used can no longer be deleted**,
because the revision that pinned it still pins it and the foreign key is `Restrict`. That is
accepted rather than worked around — an old revision that is not reproducible is not worth
keeping — but it is the thing most likely to surprise somebody later. See
[02 — Domain model](02-domain-model.md#a-pinned-version-cannot-be-deleted-any-more).

Landed straight after, in the same shape:

- [x] **The drift notice says which revision.** "This folder was made to match revision 6; the
      profile is now at revision 8" — and a profile that moved on is drift in its own right, even
      when the folder is exactly what was installed. It is the one kind of drift no directory
      listing could ever find, and two integers are the whole mechanism.

      The head comes from `IProfileRevisions`, which `ProfileService` answers **from the repo it
      has loaded** and answers `null` for every other. Null is deliberate: fetching it would put a
      network round trip per instance into a check that runs on every window activation and is
      meant to work offline. A manifest written before revisions records none, which reads as "not
      recorded" — so upgrading does not turn every existing instance into drift on the first
      launch.
- [x] **Compare two revisions.** The history says how many changed; the right-hand pane now says
      which, as a second view of the same pane rather than a page of its own — the two questions
      are asked about the same revision seconds apart. It defaults to comparing with the revision
      before, so the pane and that row's own summary counts describe the same thing.

      Computed **client-side**, out of two dependency reads and one walk of the registered mod
      list. A diff endpoint would have bought little: naming the mods needs their registered
      records either way, and the catalog walk dwarfs the dependency rows. It also keeps the
      property that there is exactly one route into a profile's mod list, and that it reads.
- [x] **Name a save from the editor** — an optional *version description*, Fusion 360's wording for
      the same field on the same gesture, shown only while there is something to save. Never
      required: a field the save button refused to work without would be answered with "asdf" by
      the third save, and a history of "asdf" is worse than one of unnamed revisions with honest
      counts.

Still open:

- [ ] **Page the history.** `GET .../revisions` windows by `skip`/`limit`, and the page reads the
      first fifty and says plainly that older ones exist. An offset rather than a keyset because
      `RevisionNumber` is a value object and a provider cannot translate a comparison on one — the
      same constraint the mod usage listing works around.

      The comparison picker inherits the same bound: it offers the revisions that were read, so on
      a profile with hundreds of them the oldest are not yet reachable to compare against.
- [ ] **Prune history on a policy** — keep the last N, anything labelled, and anything an
      instance manifest references. Only worth building if storage ever actually bites; it is the
      release valve for the deletion consequence above, not something to pre-emptively add.

## Phase 5 — Fill in the shell

Everything the sidebar promises and does not deliver. Cheap individually, and worth doing
only once the core works.

- [x] Repo → Members: the member list, add by username search, change level, kick. The server
      is complete.
- [x] Overview pages for repo and profile, which were `ExamplePageViewModel`. What belongs
      here is whatever the sync flow turns out to need at a glance: instance status, drift
      from the profile, last sync.
- [x] Use `LocalState.LastSelectedRepos` / `LastSelectedProfiles`, which were declared and
      never read, to restore where the user was.
- [x] **Stop the sidebar navigating on drag.** A `ListBox` extends selection to whatever the
      pointer passes over while the button is held, and selection drives navigation, so dragging
      through the menu visits every item on the way. Nobody asked for that; it is why the catalog
      keeps its 150 ms scan delay. Worth fixing on its own merits.
- [ ] *Nice to have:* **drag a profile onto an instance in the sidebar to activate it.** Depends
      on the fix above — once a drag passes the threshold and `DragDrop.DoDragDrop` captures the
      mouse, selection stops following, which is exactly what makes the gesture possible. No
      drop-target filtering is needed: both lists belong to `RepoPageViewModel`, so everything
      visible is already scoped to the selected repo and therefore to its scope. One direction
      is enough: "put this profile into that game" reads correctly, the reverse does not.

## Phase 6 — Scale and hygiene

Driven by the stated volumes, not by generic good practice.

- [x] Give `GET repos/{repoId}/mods` **both** pagination and a delta form keyed on
      `ModVersion.Updated`.
      They solve different problems: pagination bounds any single response, the delta bounds
      the steady state. Paginate the delta too — a first sync against an established repo
      returns everything. **Phase 1's `ModCatalog` merges source scans with this endpoint**, so
      it meets the stated volumes long before this phase; it was pulled forward, and the catalog
      walks the listing a page at a time.
- [x] **A blob reclamation sweep** — nothing anywhere deleted a blob. Import orphans,
      deleted versions and deleted repos all stranded bytes permanently. A sweep over the `mods`
      container against registered `(repoId, modId, versionId)` triples is the whole job; the
      same applies to `mod-images` against the reference table. It runs as a hosted service, lists
      blobs *before* reading registrations rather than the reverse, and ignores anything younger
      than a grace period well past the upload SAS lifetime, so an in-flight import cannot have
      its bytes swept between upload and registration. A name it cannot parse is reported, never
      deleted.
- [x] Stop full-list refreshing after every mutation — apply the
      returned DTO to the existing collection instead. Removes the need for the
      `*OfInterestChanged` selection dance.
- [x] Return 401/403 for authorization failures rather than 400. The client already
      branches on `CustomProblemDetails.Type` rather than status code, so this was free on that
      side — but `MapToBadRequest` and every endpoint's `Results<...>` signature named the status,
      so it was a wider change than it looked. `MapToForbidden` replaces it, and the 401 is handled
      centrally by `NotAuthenticatedMiddleware`, because it is thrown below the handlers and the
      endpoints most able to raise it return a bare `Ok<T>` with no union to carry it — those
      previously answered 500.
- [x] Fix the duplicate-username crash. One collision and a real person could not use the app.
      Provisioning is automatic, so there is no form on which to report the collision: the name is
      disambiguated with a numeric suffix the user still recognises as themselves.
- [x] Authorize before loading in the membership endpoints — to the floor the real check can never
      fall below, since `ChangeOthersMembership` needs the subject's level and therefore cannot run
      first. `POST repos/check-name-taken` is gated on exactly what creating a repo is gated on,
      rather than answering an existence oracle over every repo name to anyone signed in.
- [x] Either wire up the scope policies or delete them. **Deleted.** No token anywhere carries
      those scopes, so activating the policies would have denied every request; repo creation is
      gated on `User.IsTrusted`, expressed through the same fluent builder as everything else.
- [x] Delete the empty and duplicate projects — `ModsDude.Server.Services`,
      `ModsDude.Server.Common`, the `ModsDude.Client.Cli` directory — fix the stale `slnLaunch`
      entry, and drop the unused MediatR registration and package references.
- [x] A real README: what it is, what it needs, how to run it.
- [x] CI in the empty `.github/workflows/`: build and test on push. One file — but two jobs, since
      the persistence tests need a PostgreSQL service container, which GitHub only runs on Linux,
      and the WPF client only builds on Windows.
- [x] **Something that notices when `Generated.cs` is stale.** Every problem type, DTO field and
      route added on the server is invisible to the client until somebody remembers to
      regenerate, and nothing warned. The OpenAPI document is checked in at `openapi/v1.json` and
      `scripts/openapi.ps1` regenerates or verifies it; CI runs the verify. Note what this does and
      does not catch: it fails when the *document* is behind the server, which is the only warning
      anyone gets that the generated client is behind too — nothing compares `Generated.cs` against
      the document itself.

## Phase 7 — Version ordering from version strings

> **This was sequenced wrong and most of it belonged in Phase 1, which is where it landed.**
> Import can bring in several
> versions of one mod at once — one from a mod folder, one from Downloads — and placing them
> needs the comparer. Without it Phase 1 could only append, so an out-of-order import would have
> written a wrong ordering from the first day and Phase 7 would have inherited bad data to repair.
> The comparer, the partial-order sort and the arbitration dialog shipped with import; what
> genuinely remained here is backfilling existing rows, which is nothing while there is no real
> data.

A design change, originally sequenced late because it touches the domain and the sync engine
depends on stable ordering.

`ModVersion.SequenceNumber` was a curated position that a human sets, with an insert-before
placement to back-fill an out-of-order upload. The model now shipped is that ordering **derives
from the mod's own version string**, compared by a comparer the game adapter supplies — `1.2.3.4`
and `v2-beta` do not compare the same way, and only the adapter knows which applies.

**Best-effort, with the user as the tie-breaker.** `modDesc/version` is free text and mod
authors write whatever they like in it, so a parser that insists on succeeding will silently
mis-order releases. The comparer should be confident or abstain — never guess.

- [x] A shared `DefaultModVersionComparer` covering common notation: dotted numerics of any
      depth (`1`, `1.2`, `1.2.3.4`), an optional `v`/`V` prefix, zero-padded segments, and
      pre-release suffixes (`-beta`, `-rc1`, `b2`). Compare segment-wise and numerically, so
      `1.10 > 1.9`. Returns a **three-way result**: ordered, equal, or *cannot compare
      confidently*. Abstaining is a first-class outcome, not an exception.
- [x] **Adapters can optionally override it.** A default interface member on `IGameAdapter`
      (`IModVersionComparer VersionComparer => DefaultModVersionComparer.Instance;`) so an
      adapter that says nothing gets the shared parser, and a game using dates or build numbers
      replaces it wholesale. An overriding adapter is still expected to abstain rather than
      guess.
- [x] **Abstain on mixed notation.** `v1` versus `1.0` is the canonical case: they are probably
      the same release, or possibly adjacent ones, and nothing in the strings settles it.
      Likewise a date-like `2024.03` next to a semantic `1.4`. Guessing here produces a wrong
      order that nobody notices until a profile pins the wrong build.
- [x] **Order a set as a partial order, not a sort.** `OrderBy` assumes a total order and
      misbehaves with an abstaining comparer. Build the partial order from pairwise comparisons
      over the union of registered and incoming versions, then topologically sort it — a few
      dozen versions makes all-pairs free. An abstention is only a *question* when nothing
      settles the pair transitively.
- [x] **Resolve ambiguity in one batched dialog, before registering.** Collect every pair left
      genuinely unordered and ask once, showing each mod's version list in the order that was
      derived with the unplaceable ones floating and draggable. One dialog per import, not one
      per mod. Unambiguous mods proceed immediately and never wait on it.
- [x] Cancelling that dialog **skips those mods and continues the import** — one unorderable mod
      is not a reason to lose a two-thousand-mod batch.
- [x] Never register at a provisional position and fix it later: the newest version would be
      wrong in the interim, and a version appended past the real newest would advertise itself as
      an update and offer everyone a downgrade.
- [x] Persist the resolution. Ordering is a **repo-level fact shared by every member**, so the
      answer is written to `SequenceNumber` server-side and nobody is asked again. Rows the
      comparer ordered on its own are written the same way.
- [x] **Send the position with the registration; do not compare on the server.** The server has
      no adapters and cannot parse a version string — `AdapterData.Configuration` is opaque to
      it by design, which is what lets a new game ship without a server deployment. So
      `RegisterModRequest` grows a placement: append, or insert before a named version. The
      client computes it with its own adapter's comparer. The server validates and stores.
      Because ordering is stored rather than recomputed on read, clients on different adapter
      compatibility versions cannot disagree after the fact — the first writer settles it.
- [x] **Keep insert-before placement** as the mechanism the resolution dialog writes through — it
      is exactly "put this version at this position", which is what arbitration produces. Earlier
      drafts of this plan proposed retiring it; it stayed, and grew a companion:
      `PUT repos/{repoId}/mods/{modId}/versions/{versionId}/placement` moves an
      already-registered version, which is the backstop for an order that is wrong for reasons
      concurrency control cannot catch.
- [ ] **Not needed: backfill existing rows**, routing whatever the comparer cannot order into the
      same dialog. There are no existing rows to repair — the comparer shipped with import, so no
      registration was ever made in append-only order. The manual reorder covers the case this
      bullet would have served.

The division of labour: **the comparer proposes, `SequenceNumber` stores, the user arbitrates.**
That keeps automatic ordering for the overwhelming majority of mods without ever inventing an
answer for the ones where the version strings genuinely do not say.

> An earlier design pass argued against parsing version strings at all, for the free-form
> reason above. The abstain-and-ask design is what reconciles the two positions: it parses where
> parsing is safe and refuses to where it is not. Note also that "update available" on the
> import pages never needs this — a local version either has a server counterpart or it does
> not. See [09 — Mod catalog](09-mod-catalog.md#a-note-on-update-available).

## Phase 8 — Savegames

Only once mod sync is solid. `IBaseSavegameAdapter`, `IInstanceSavegameAdapter` and the
`CanSupportSavegames` flag are empty placeholders today, and nothing reads the flag.

Transport is the easy half — the mod upload path pointed at a different container. The hard half
is conflict, and this design refuses the merge rather than attempting one: **one holder at a time,
an explicit hand-back, and a version for every hand-back.**

> **Built, apart from six boxes below.** Publish, check out, check in, force, keep playing, take a
> copy, discard and restore all work end to end. Packing, the checkout binding, the slot picker and
> its safety checks are on the client; savegame drift is folded into the app-wide notice; the repo
> and instance pages exist with their dialogs, and the check-out flow applies the profile with the
> unrecognised-mods dialog behind it.
>
> What is left is listed unchecked: the quarantine fallback for a displaced slot, the two flows
> offering each other, the repo overview's *where was I*, check-in from the drift notice, and the
> instance-side half of reaching a check-out. The implicit profile for mods-less repos is not
> missing but abandoned — see [Phase 9](#phase-9--one-current-savegame-per-profile).

### A savegame belongs to the repo; a version belongs to a revision

A savegame is not owned by a profile. It sits in the repo beside profiles, keyed `(RepoId, Id)` —
the same aggregate placement as `Profile`, for the same reasons.

**Every version records exactly one `ProfileRevision`**, and that is where the dependency lives. A
save moves from revision 6 to revision 7 as the group updates mods, so pinning a profile on the
savegame itself would either forbid that or lie about it.

`Savegame.ProfileId` still exists and means something different: the standing intent that this save
follows that profile. It is the distinction `ActiveProfile` draws against the manifest in
[07 — Mod sync design](07-mod-sync-design.md#what-sync-records-and-why-it-has-to), one aggregate
over. The two may legitimately disagree — branch a profile, move the save onto the branch, and the
old versions still honestly name the old profile's revisions.

### Server model

- [x] `Savegame (RepoId, Id)` — `Name` unique per repo, `ProfileId`, `HeadVersion`, `Created`. No
      navigation to its versions, for the reason `Profile` has none to its revisions.
- [x] `SavegameVersion (RepoId, SavegameId, Number)` — `ProfileId` + `ProfileRevision` (FK,
      `Restrict`), `ContentHash`, `SizeBytes`, `CreatedBy`, `Created`, `Label`,
      `Origin (Created | CheckedIn | Forced | Restored)`, `BaseVersion`, `CheckoutId?`. Numbers
      one-based, so somebody can say one out loud and find it — but, unlike a revision number,
      **not contiguous**: pruning leaves the gap where an old version was, because renumbering would
      make yesterday's sentence point at a different save.
- [x] **A check-in names the version it was built on**, and a stale base is refused with
      `savegame-version-stale` carrying what the head is now. The primary key on
      `(RepoId, SavegameId, Number)` is what makes that true rather than merely likely — the same
      argument as [Phase 4.5](#phase-45--profile-revisions), one aggregate over.
- [x] **Forcing over a stale base copies forward.** The forced check-in becomes the new head,
      stamped `Origin = Forced` with `BaseVersion` naming what was actually played. The fork ends
      up in the record without anybody needing a tree.
- [x] **A check-in whose hash equals the head's mints no version.** Launching the game and quitting
      must not cost a 400 MB blob and a line of history. A save that changes nothing mints nothing,
      exactly as for revisions.
- [x] Authorization: Guest downloads; Member publishes, checks in, forces, and takes a checkout.

The consequence to accept knowingly: **a profile that has been played can no longer be deleted**,
because a version still names one of its revisions and the foreign key is `Restrict`. Same bargain
as a pinned mod version one level up, and it should be reported the way
`ProfileRevisionExtensions.CheckIfVersionIsDependedOn` reports its own rather than surfacing as a
database error.

### The checkout is a log, not a field

- [x] `SavegameCheckout`, keyed on `Id` alone and carrying `(RepoId, SavegameId)` — `UserId`,
      `TakenAt`, `ExpiresAt`, `EndedAt?`,
      `EndedReason (CheckedIn | TakenOver | Discarded)`.
- [x] **Expiry is not an end reason.** An expired claim is still the open row — it just reads as
      stale — because nothing runs to close it and a job that did would be inventing an event nobody
      caused. `GetStatus(now)` folds the two facts into `Held | Stale | Ended`, reporting `Ended`
      ahead of expiry: what actually happened outranks what would have happened.
- [x] **The current holder is the open row.** A filtered unique index on `(RepoId, SavegameId)`
      where `EndedAt is null` permits one, so there is no current-checkout field to keep in step
      with the history sitting beside it.
- [x] `SavegameVersion.CheckoutId` joins the two halves into one timeline — check-ins are already
      history, so only the check-out half needs recording. Null for a publish, and for a forced
      check-in taken without a checkout.
- [x] **The claim expires, and is renewed while it is held.** Somebody who checks out on Friday and
      goes on holiday has to read as stale rather than as holding it: a warning that never clears is
      a warning everybody learns to click past, which is
      [Phase 4](#phase-4--make-drift-unmissable)'s argument seen from the other end.
- [x] **Taking it anyway is allowed.** It closes the previous row as `TakenOver` and warns naming
      who holds it and since when. The checkout is the social half; the base-version check is the
      mechanical one, and only the second is a guarantee.
- [x] **The log is never pruned with the versions.** The rows are tiny and outlive the blobs, so
      history can still say that a version existed and was pruned.

### Storage and retention

- [x] Blobs at **`{repoId}/{savegameId}/{contentHash}`**, through the same SAS mint as mods —
      addressed by content rather than by version number, which is the one place this deliberately
      diverges from `ModStorageService`. Numbering the blob would have two people checking in at the
      same moment mint upload links for the same name, so whichever wrote second would silently
      replace the other's bytes; the stale-base check decides who takes the head, but by then the
      loser's save is already gone. Hashing also makes a restore a metadata operation rather than a
      blob copy, and lets a duplicate check-in cost nothing.
- [x] `BlobReclamation` grows `PlanSavegameSweep` and a third name parser. The hazard is unchanged
      and so is the remedy: a grace period well past the SAS lifetime, and list the blobs **before**
      reading the registrations.
- [x] Keep the last N versions, default 10, configurable per repo. **The head is never pruned, and
      neither is anything carrying a `Label`** — labelling a version is how somebody keeps it.
- [x] Pruning leaves gaps in the numbering. Numbers exist to be said out loud; nothing renumbers.

### Four verbs, and only one of them asks about a slot

| Verb | Direction | Slot chosen | Local copy afterwards |
| --- | --- | --- | --- |
| **Publish** | slot → new savegame | already known | kept, now checked out |
| **Check out** | savegame → slot | **every time** | written |
| **Check in** | slot → new version | no | recycled |
| **Discard** | — | no | recycled, no version minted |

- [x] **Publish is not check-in.** "Upload this new thing" and "upload a new version of that thing"
      have opposite failure modes, and the old MVP made them one button.
- [x] **Check-in asks nothing.** It acts on the slot the open checkout already names. Choosing
      between twenty near-identical folders from memory is where the MVP went wrong, and it is
      precisely the moment where a wrong answer publishes somebody else's slot under this save's
      name and burns a version doing it.
- [x] **Discard ends a checkout without minting a version** — taken by mistake, never played.
      Without it the only ways out are a junk version or waiting to be taken over.

### Slots

- [x] **A slot is occupied by ModsDude only while a save is checked out**; check-in frees it by
      recycling the local copy. That is what removes any need for eviction machinery — the slots in
      use are the saves actually being played, which is one or two, not twenty.
- [x] **The live checkout binding is authoritative and persisted** in `LocalState`: which slot holds
      which savegame at which version, and the hash that was written there. Once somebody has
      played, the bytes match no version on the server, so nothing can re-derive it. Same argument
      as `ActiveProfile`, and the same conclusion.
- [x] **The last-slot hint is a separate, advisory thing**, kept after check-in purely to
      pre-select next time. Never repaired, never trusted, and worth nothing when wrong — the
      `SyncManifest` category.
- [x] **The picker is shown on every check-out.** The hint pre-selects; it never decides.
      Pre-selection order: the slot this savegame used last if it is free → otherwise the first
      free slot, saying plainly that the remembered one is taken → otherwise nothing pre-selected,
      and the list is of occupied slots.
- [x] A savegame is checked out to at most one slot per instance.
- [x] **"No free slot" is still a state**, because the remaining slots can be full of saves
      ModsDude knows nothing about. The answer there is the unrecognised-slot confirmation below,
      not a ranked eviction.

### Slot safety

- [x] A **free** slot is written without a confirmation.
- [x] **Another checked-out savegame** is refused rather than warned about: that slot holds play
      nobody has checked in. Offer checking that one in first, as a single action.
- [ ] An **unrecognised** slot — somebody's own save, never published — needs a confirmation naming
      what the game calls it, and the displaced folder goes to `IRecycleBin`, with the store's
      quarantine as the fallback on a volume that has no bin. Same rule and the same reasoning as
      an unrecognised mod file: see [07 — Mod sync design](07-mod-sync-design.md#uninstall-rules).

      *The confirmation and the bin are done. `RecycleBin.TryRecycle` reports failure and leaves
      quarantining to the caller — `SavegameService.Recycle` ignores that return, so on a volume
      with no bin a displaced slot is neither recycled nor quarantined.*
- [x] **Check-in recycles the local copy only after the upload is verified.**
- [x] Slots are labelled with the game's own name for the save and its playtime — never
      `savegame3`. The folder number is an implementation detail the player has never thought in,
      and a picker that shows it is the memory test again.

### The adapter

- [x] `IBaseSavegameAdapter` returns a slot **list**, not a count, plus whether it can mint new
      ones. A fixed-slot game and one with free-form save names are then the same model, and
      nothing outside the adapter ever learns a number.
- [x] `IInstanceSavegameAdapter` supplies: enumerate the slots, the path of one, what belongs in a
      packed save and what is excluded from it. The engine performs the filesystem work, exactly as
      it does for mods — adapters supply paths.
- [x] **Anything read out of the save file is display-only**: in-game date, money, the mod list it
      believes it needs. The recorded `ProfileRevision` is the truth. Same rule as
      `ModVersion.Attributes` — see [02 — Domain model](02-domain-model.md#modversion).

### Drift, reusing what is already there

- [x] Three states, found by the same startup-and-window-activation check that already runs, and
      surfaced in the same place as mod drift:
      - a checked-out slot holds play newer than its recorded hash → **unchecked-in play**
      - the server head is past the version being held → **somebody took it over and checked in**
      - the version's revision is not the instance's applied revision → **played on a mod list this
        folder no longer runs**
- [x] Each is phrased as the consequence rather than the condition — the third especially, which is
      the case that corrupts saves and the reason locking exists at all.

### Capability decides the shape, and neither half requires the other

`CanSupportMods` and `CanSupportSavegames` both sit on `IBaseGameAdapter`, and either can be false.
Three repo shapes result, and the whole difference is confined to which surfaces exist:

| Repo | Sidebar | Overview leads with |
| --- | --- | --- |
| Mods only — every repo today | Profiles and instances, as now | Instance status and drift. Nothing about saves anywhere |
| Mods and saves | Profiles and instances, plus a Saves item | Where you were, linking into Saves |
| Saves only | Instances | Where you were |

- [ ] **Activating a profile never mentions a savegame**, and checking one out never requires that
      the user has thought about profiles. The two flows *offer* each other and are never steps
      inside each other:
      - after activating a profile that has saves and none checked out here — *"Season 4 has 3
        saves. Check one out?"*, dismissible, and absent where the adapter has none
      - checking out a save derives and applies its profile where the adapter has mods, and is
        simply "write the slot" where it does not

      *The second bullet is done, in `RepoSavegamesPageViewModel.ApplyProfileAsync`. The first is
      not: activating a profile says nothing about saves.*
- [ ] ~~**A repo whose adapter has no mods gets one implicit profile**~~ — **abandoned.** The
      nullable foreign key this was avoiding is the shape
      [Phase 9](#phase-9--one-current-savegame-per-profile) chose instead: the savegame-to-profile
      relationship is optional on both ends, paired by a check constraint so the nullability is one
      rule rather than fifteen checks. A mods-less repo then needs no profile at all, implicit or
      otherwise.

### Saves are a repo-level collection, beside Mods

`Savegame` is keyed `(RepoId, Id)` and `ProfileId` is an attribute rather than a parent, so the
faithful rendering is one repo-level list with a profile column — not a list per profile, and not
two surfaces showing the same rows under different rules, which is the thing merging Import into
Manage removed.

- [x] **One fixed sidebar item, not a list of entries:**

      ```
      RepoPage   Overview │ Admin │ Members │ Mods │ Saves │ Create profile │ Connect game │ ...profiles │ ...instances
      ```

      It is the sibling of Mods, sits next to it, and is absent where the adapter has no savegames —
      exactly as Mods would be absent for an adapter with no mods. Profiles keep their own place:
      they are destinations with three sub-pages each, and the sidebar's profile list is what
      drag-to-activate and "everything visible is compatible by construction" both rest on.
- [x] **`RepoSavegamesPage` is master-detail**, the shape this client already uses three times.
      Saves on the left — name, profile, holder chip, state — with *Check out* as the row action.
      The selected save on the right: versions and checkouts as one timeline, and for the selected
      entry who, when, size, label, and the profile revision it was played on, with a link into the
      revision comparison that already exists.

      **Not an accordion.** A two-pane history does not fit inside a row, and an expander moves the
      list under the pointer — the thing the import list is explicitly ordered to avoid.
- [x] **Restoring copies forward**, exactly as it does for a profile revision: version 4 restored
      while the head is 12 becomes version 13, stamped `Origin = Restored` with `BaseVersion = 4`,
      and no bytes move because the blob is addressed by its hash. So **check-out always takes the
      head** — there is no stale base to reason about at the moment somebody wants to play, and
      looking at an old version without disturbing anybody is what *Take a copy* is for.
- [ ] **The repo overview does not repeat the list.** It answers *where was I* — the instance, its
      active profile, drift, and what you are holding — and links into Saves.

      *Not started. `RepoOverviewPageViewModel` mentions savegames nowhere.*
- [x] **`InstancePage` gains Saves**: the slot list, which is the local half — free, checked out
      with unchecked-in play called out, or unrecognised. **Publish lives here**, because it is
      inherently about a slot, and it asks nothing about the profile: the instance has an active one
      to derive from.

### Checking out, in order

The destructive step is local and comes first, the claim is social and wants to be fast, and the
mod question is last because it is the only one that can be deferred.

- [x] **1. The slot picker and its safety checks**, blocking, up front. Pre-selected per
      [Slots](#slots), and the three states told apart per [Slot safety](#slot-safety).
- [x] **2. Take the claim and write the save into the slot.** Neither depends on the mods being
      right, and a user who wanders off here still holds the save and has it on disk.
- [x] **3. Nothing unrecognised in the mod folder → apply, no dialog.** The ordinary night stays one
      click.
- [x] **4. Otherwise, the drift notice's own two verbs**, because this is that problem found at a
      different moment:

      > **The mod folder has 2 mods that are not in the repo.** `FS25_BigBaler` 1.2, `FS25_Meadow` 3.0
      >
      > **Apply** — puts the folder on Season 4. The 2 mods go to the Recycle Bin.
      > **Review** — opens Season 4's mod list with this folder scanned, to decide there.

- [x] **Never offer to import from that dialog.** Importing on the way past would commit files
      nobody decided to keep, which is the argument that already put import behind Save in the
      editor — see [09 — Mod catalog](09-mod-catalog.md#import-on-save). Import is what Save does
      once somebody has chosen.
- [x] **Review lands on machinery that exists**: `GoToProfileModsAsync` carrying the instance id,
      the folder as the one pre-enabled source, the unrecognised mods on the left as local
      candidates, and **Save and apply** as the single button that imports what was kept and syncs.
      What was not kept is recycled by the ordinary uninstall rule.
- [x] **Review leaves the instance drifted**, and the persistent notification takes it from there —
      the same answer this design already gives for an instance that cannot be applied to right now.
- [x] **Apply carries the consequence in its label**, not just the verb. "Go to the Recycle Bin" is
      the difference between a frightening button and an informed one, and it is recoverable.

### Where the rest of it is surfaced

- [ ] **Check-in is clicked from the drift notification**, not found by navigating to it. *"You
      played Big Valley for 2 hours. Check it in so the others can take it."* Unchecked-in play is
      one of the three savegame drift states, the notification is already app-level and persistent,
      and its dismissal already expires when the set changes. A second notification competing with
      it would be strictly worse.

      *Not started. `DriftNotificationViewModel` reports unchecked-in play and carries only
      OpenModList, Reapply and Dismiss.*
- [ ] **Reachable from both ends**, as activation is:

      | From | Fixed | Chosen |
      | --- | --- | --- |
      | The Saves page | the savegame, and therefore its profile | the instance, where there is more than one, and the slot |
      | The instance's Saves | the instance | the savegame, grouped by profile |

      *The Saves-page end is done. The instance's Saves page publishes and checks in but offers no
      check-out, so the second row is missing.*
- [x] **State is one chip per row**, in the vocabulary the member list already uses: *Available*;
      *You have it*, plus *unchecked-in play* where the slot has moved; *Anton has it, since 20
      minutes ago*; *Anton has had it since 3 March* for a stale claim; *2 revisions behind*,
      caution-coloured only where a locked pin moved between the two.
- [x] **The check-out confirmation is where the locked-mod warning finally lands.** This design has
      always said locked drift deserves naming rather than a count, and nothing renders it yet. The
      moment before somebody plays a shared save is when a map at the wrong version stops being
      untidy and starts being a damaged save.
- [x] **_Take a copy_** — a third download mode, writing a save into a slot without taking the claim
      and without a binding. It answers what a Guest is offered (the list, the history, and this),
      and it covers a Member who wants to see what revision 4 was like without holding the save
      hostage. The copy is an ordinary unrecognised slot from then on.

### Settled

- [x] **Check-in has a _keep playing_ option.** The same version is minted, and the local copy and
      the claim are both kept, so a mid-session backup does not become an upload followed
      immediately by re-downloading what was just sent. `CheckInAsync` rebases the binding onto the
      version it just minted.

Three things settled with it, and deliberately not built:

- **Retention stays at ten for every repo.** Configurable-per-repo is a column, a migration and an
  admin field for a number nobody has yet wanted to change. `SavegamePruning.PruneAsync` takes
  `keep` as a parameter, so the day it becomes a setting it is one call site.
- **Mods-less repos get no implicit profile, and never will.** Superseded by
  [Phase 9](#phase-9--one-current-savegame-per-profile), which makes the savegame-to-profile
  relationship optional on both ends instead.
- **No backwards compatibility anywhere in this phase.** One developer, no users, no data worth
  migrating.

## Phase 9 — One current savegame per profile

Designed in full in [10 — Savegames and profile revisions](10-savegame-profile-binding.md). That
document is the specification; this is the order to build it in.

A profile gets at most one *current* savegame and a succession of past ones, and which revision a
savegame was played on stops being guessed from the folder and starts being observed. It closes
the drift notice nobody could act on — *"checked out against revision 1 and this folder is on
revision 5"*, where re-apply did not help because re-apply always applies head.

**Slice 1 goes first and alone.** Everything after it needs the regenerated OpenAPI client.

### 1. Server schema and API

- [x] **`Savegame.SupersededAt`**, with a filtered unique index on `(RepoId, ProfileId)` where it
      is null. Not also filtered on `ArchivedAt`, unlike the name index beside it — an archived
      savegame still holds its profile's slot. Named `IX_Savegames_OneCurrentPerProfile` rather than
      left to convention, because publish reads the name back off a unique violation to tell a name
      clash from somebody else's publish. It replaces the plain foreign-key index EF had made over
      the same two columns, so the one query that wants past savegames too — "does anything follow
      this profile?", asked once by `DeleteProfileV1Endpoint` — scans a repo's handful of rows.
- [x] **`ProfileId` and `ProfileRevision` nullable** on `Savegame` and `SavegameVersion`, with a
      check constraint making each pair all-or-nothing. The pair only exists on `SavegameVersion`,
      which is where that constraint went: a savegame pins no revision, and pinning one on it would
      be the thing `Savegame`'s own remarks refuse. `Savegame` carries the constraint that is
      available to it instead — superseded implies a profile, since a savegame following no mod list
      is in no succession and is neither current nor past.
- [x] **Publish supersedes.** `PublishSavegameRequest.ProfileId` becomes nullable; publishing to a
      profile that has a current savegame supersedes it in the same transaction. Two commits inside
      it, in order, for the reason the box below gives.
- [x] **Make a past savegame current.** A swap: the incumbent is superseded before the incoming row
      is cleared, or the unique index rejects the intermediate state.
      `POST repos/{repoId}/savegames/{savegameId}/makeCurrent`, answering with both savegames so the
      client can name what it displaced. Two commits inside one transaction, the shape
      `MoveModVersionV1Endpoint` already uses to take an ordering through a unique index.
- [x] **`UpdateSavegameV1Endpoint` becomes a rename.** Moving a savegame between profiles would put
      `Savegame.ProfileId` and its versions' `ProfileId` in disagreement. `CreateVersion` stopped
      taking a profile at all with it: the version's is the savegame's, so nothing can pass one that
      disagrees, and the pairing became one rule at the single place versions are minted.
- [x] **Regenerate the client.** `openapi/v1.json` and `Generated.cs` both.

Two things fell out of the boxes above rather than being added to them:

- **Check-in's revision is nullable too.** Otherwise box 2 would leave a savegame with no mod list
  publishable and never checkable in. The request sends a revision exactly when the savegame follows
  a profile, and either mismatch is refused rather than resolved in one direction.
- **Three problem types**: `savegame-current-conflict` for losing a profile's slot to a publish in
  the same instant, `savegame-profile-not-paired` for half a pair, `savegame-has-no-profile` for
  asking to place a savegame in a succession it is not in.

### 2. Play attribution on the client

- [x] **`LastObservedHash` and `LastPlayedRevision`** on `SavegameCheckoutBinding`, and its
      `ProfileId`/`ProfileRevision` paired the same way the server rows are. `LastObservedHash` is
      unset-reads-as-`ContentHash` rather than an assignment every construction site has to remember,
      so a binding taken by some future route cannot leave the boundary unrecorded and report a fresh
      check-out as an evening. Checking in and carrying on resets both halves, because that is a
      check-out in every respect that matters.
- [x] **`Observe()`**, at two call sites: inside the manifest write, so it reads the outgoing
      revision, and at check-in, where the packed hash is the observation and costs no second pass.
      Folded into the write rather than called beside it — no apply path can then rewrite the
      manifest without attributing the play first, and the no-work path had to be covered too, since
      a revision can move without a single mod doing so.
- [x] **Invert `ResolveAppliedRevision`** to prefer the binding over the manifest. It answers `int?`
      with it: a savegame following no mod list sends no revision, and the server refuses one that
      does. `Observe()` inherited the manifest guard the old order carried — a folder synced to a
      *different* profile has no number this savegame can record, so the play is seen and the number
      withheld, rather than `LastPlayedRevision` becoming a way around the check.
- [x] **Thread the revision through `ModSyncService.GetDesiredAsync`**, which passed `null` and
      therefore always resolved to head. `ModSyncRequest.Revision` carries it; null still means head,
      which is what an instance following its profile wants, and the number the server answers with is
      what the manifest records.
- [x] **One hash format.** The tolerance came out of `ModContentHasher.Matches` rather than being
      routed around: two parts of the client spelling a hash differently is a bug at the writer, and
      absorbing it at every comparison site hides that bug while inviting the next comparison to lean
      on it. The slot safety check and the drift rules got stricter with it, which is the right
      direction for both — the safety check now errs towards refusing a write.

One thing fell out of the boxes above rather than being added to one:

- **The sync engine now knows one fact about savegames.** `ISavegamePlayObserver`, a seam of one
  method in the shape `IInstanceModFolders` already had, and `RecordAlreadyMatched` became
  `RecordAlreadyMatchedAsync` with it. The observation is uncancellable on purpose: by the time it
  runs the folder is already what the profile asked for, and abandoning it would credit everything
  played on the outgoing revision to the incoming one, quietly and permanently.

### 3. The rules

- [x] **Check-out targets** the profile's head for a current savegame, the pinned revision for a
      past one, and nothing for one with no profile. `SavegameCheckoutBinding.TargetRevision` records
      which at the one moment the savegame's current-or-past state is in hand, and a number there is
      the whole of "this instance is holding a past savegame".
- [x] **The apply table** — current follows, past only re-applies its own revision, a different
      profile is refused. `SavegameHoldRules` is the rule; `ModSyncService.PlanAsync` is where no
      apply path gets past it. The profile-switch refusal falls out of the same table rather than
      being a check on `SetActiveProfile`: every switch in the app applies first, and a refused apply
      does not record the intent.
- [x] **Holding a past savegame is stored state**, and its instance passes that revision into
      `InstanceDriftService.Check` in place of head. Nothing is suppressed; the comparison comes out
      equal on its own.
- [x] **Narrow `HasMovedOffItsModList`** to compare against the savegame's target rather than the
      binding's check-out value, which fired on the ordinary follow-the-profile flow.
- [x] **The checkout limit counts savegames with a profile**, not savegames — and it turned out never
      to have been enforced at all, only assumed. Refused in `CheckOutAsync` and `PublishAsync`,
      before the claim in one and before the pack in the other.

Three things fell out of the boxes above rather than being added to them:

- **`ModSyncRequest.Revision` null stopped meaning head.** It means "whatever this instance must be
  on", resolved in `PlanAsync` from what is held there — so the drift notice's re-apply, the mod list
  editor's save and the instance page's apply all target a held past savegame's revision without any
  of them knowing what a savegame is. Only the check-out dialog names a number, because it previews
  the apply for a savegame nothing is holding yet.
- **`ISavegamePlayObserver` became `IHeldSavegames`** and took `CheckDriftAsync` with it, so the drift
  monitor stopped depending on the whole savegame client for two facts about held slots. It is now
  the whole of what sync and the monitor know about savegames, which is what the seam claimed to be.
- **`ProfileApplyStatus.Refused`, and `ProfileApplyOutcome.RecordsIntent` with it.** "Could not be
  reached" was the only answer a refusal could have got, and it is the wrong one — waiting does not
  fix it. The intent flag went the same way: recording a profile switch that a held savegame forbids
  applying would leave an instance whose standing profile it can never be put on.

### 4. Interface

- [x] **Chips and the savegames list** — past is `Neutral`, never `Caution`; a *Show past savegames*
      toggle, off by default, with the count it is hiding said out loud beside it. A filter nobody can
      see is a list that is quietly wrong, and the toggle is what keeps this the one repo-level list
      rather than reintroducing a per-profile one.
- [x] **Row actions**, two buttons with the disabled reason carrying the explanation.
      `SavegameRowRules` is the rule and the wording, tested in Core the way `SavegameHoldRules` is;
      the page supplies the instance, what it holds and what its folder is on. It picks the instance
      that *could* host the savegame now over the one following its profile, because a row that answers
      about a folder the buttons do not act on is a puzzle rather than an answer.
- [x] **Instance page** — the apply button's meaning changes while a past savegame is held, and the
      profile dropdown is disabled while one with a profile is. The label came from
      `InstanceActivation.Label`, which already existed for exactly this reason: what the control will
      do, not the screen it sits on.
- [x] **Drift notice** — never "behind the profile" for a held past savegame, and its action reads
      *Re-apply rev 4*. The first half needed no code: slice 3 hands the drift check the revision the
      instance is supposed to be on, and the comparison comes out equal on its own.
- [x] **The three dialogs** — check-out names the revision it will run on, check-in names what it
      recorded, publish carries the profile picker, the declared revision and the supersede notice.
      Publish is the one that grew: `SavegamePublishTarget` replaced the instance's active profile,
      and `PublishAsync` takes a repo rather than deriving one from a profile it no longer requires.
- [x] **Profile page** — its current savegame, a count of past ones, and the archived-current case
      with its three ways out. On Overview, and it reads both savegame lists: archiving does not
      release a profile's slot, so an archived savegame is still its current one.

Three things fell out of the boxes above rather than being added to them:

- **Publishing from a never-synced instance stopped being refused.** `RequireAppliedRevision` was the
  last place the client insisted the folder be on the profile first, and the design says why it should
  not: a first version's revision is *declared*, so requiring a sync would observe the folder at the
  moment of publishing — a different fact, not a better one. The number is now
  `SavegameService.DeclaredRevisionFor`, shown in the dialog before it is recorded.
- **`ProfileApplyService.ApplyAsync` gained a revision.** The row's *Apply profile* prepares the folder
  for a savegame nothing is holding yet, so "let the instance decide" resolves to head — correct for a
  current savegame and wrong for a past one, whose check-out a moment later would leave the folder drifted
  against the revision it had just pinned. The same number goes into `DecideApply`, so the refusal and
  the apply are asked the same question.
- **`ISavegameService.GetPlayedRevision`**, and `ResolveAppliedRevision` split in two to answer it. The
  check-in dialog has to name the revision the version will carry, and a second computation of that is
  how a dialog comes to name a number the version does not have. The refusal stayed on the check-in:
  what a dialog has to say about a savegame whose revision nothing on this machine knows is nothing.

### Verified after slice 4

A pass over all four slices against the code. Everything the boxes claim is in the tree; four things
they did not claim were not:

- **`makeCurrent` had no client.** Slice 1 built the endpoint and said it answers with both savegames
  "so the client can name what it displaced"; no slice built that client, so a server verb the design
  names as one of the *two* things that change which savegame a profile follows was unreachable. It is a
  third row action on a past savegame now, with the confirmation the design asks for. Making a savegame
  current while holding it clears its pin: a current savegame follows its profile, and a number left
  behind would hold that folder at its old revision forever and refuse every apply that tried to move
  it. That is not "the hold moves under its holder" — that argument is about somebody else's
  publish, which is not stated to whoever is playing.
- **The drift notice explained `PlayedOnAnotherModList` with the wrong numbers.** It named the
  binding's check-out revision against the folder's, which is neither of the pairs the rule compares:
  slice 3 narrowed the rule to the savegame's *target*, and the other way the rule fires is a
  different profile entirely, where two revision numbers are not comparable at all. One kind, two
  sentences, and `SavegameDrift.RunsOnAnotherProfile` to tell them apart — which also made
  `TargetRevision` load-bearing rather than written and never read.
- **The profile page's activation control was offered and then refused.** The instance page's
  dropdown is disabled while a held savegame forbids the switch; this is the same switch from the other
  end and was not, so the refusal arrived after the click. It asks `DecideApply` too now.
- **The instance page went stale on a check-in from its own sub-page.** The slot list is a child of
  that page, so checking a savegame in there left the shell above it showing a disabled dropdown and
  a *Re-apply rev 4* about a hold that had ended. It listens to `SavegameBindingStore.BindingsChanged`
  now. Every other surface asking the hold question is rebuilt by navigating to where a check-in
  happens, so none of them needs the subscription.

A fifth was found the same way and is Phase 8's rather than this phase's: **`UpdateSavegameV1Endpoint`
had no client caller either**, and never had one - renaming a savegame was unreachable before Phase 9
started, and slice 1 only narrowed the endpoint to that verb. It has one now, on the repo's saves list
beside Archive. `RenameModalViewModel` was already the name prompt the archive uses and took a
confirm-label parameter to be it for both, since "Restore it" over a rename is a button somebody has
to read twice. The clash stays the server's to find: names are unique behind a filtered index, so
checking first would be a second copy of a rule that would still be racing somebody else's rename -
losing that race re-opens the dialog with what they typed instead of an error to start over from.

### Already shaped for this

Worth knowing before starting, so none of it gets rediscovered:

- **`SavegameBindingStore` is already plural**, and every `GetBindings` call site treats it as a
  list. Holding several savegames needs no storage change.
- **`InstanceDriftService.Check` already takes `currentRevision` and `profileDependencies`** as
  parameters. Slice 3 changed what callers pass, not the signature.
- **The no-mods branch already exists** in `RepoSavegamesPageViewModel.ApplyProfileAsync`.
- **`GetModDependenciesV1Endpoint` already serves any revision**, the client can ask for one since
  slice 2, and slice 3 decides which. Nothing is left here.
- **Pruning already refuses** a revision a savegame version holds, so a past savegame stays
  reproducible with no new guarantee.

### Settled with it

- **Mods-less repos still get no implicit profile**, and now never will — the savegame-to-profile
  relationship is optional on both ends instead. This supersedes the box and the note under
  [Phase 8's Settled](#settled).
- **A published savegame's first version carries a declared revision.** The bytes existed before
  ModsDude saw them; no arrangement of the publish flow recovers what was in the folder at the
  time. Every version after it is observed.

## Phase 10 — One game, many targets

The instance is two things wearing one name: **a policy holder** — which profile this follows, which
savegame is held here — and **a folder** on disk. Almost every game has one folder, so the two look
identical and the conflation costs nothing to notice. BeamNG.drive with BeamMP has three, and there
the conflation is not merely redundant, it is wrong: activating a profile on the dedicated server
leaves the MP client on the old mod list, nothing connects the two actions, and nothing says they
have diverged. The client matching the server is the entire point of that arrangement.

So the two halves separate. **Policy moves up to the game; the folder stays where it is and stops
carrying anything else.** A game has one active profile and holds at most one savegame; its
*targets* are how many folders that reaches. Farming Simulator has one target and never mentions it.
BeamNG has three and mentions them only where they differ.

This supersedes [Instances are scoped to a game, and an instance is one mod
folder](#instances-are-scoped-to-a-game-and-an-instance-is-one-mod-folder). That decision was right
given its premise — with policy on the folder, modelling installations with child targets buys
nothing — and this phase changes the premise.

**Client-only. No server changes, and no endpoint moves.** The server has never heard of an
instance.

### The shape

**`Game`** — one per `GameIdentity` per machine. Holds `ActiveProfile` and the savegame hold. Keyed
by identity in `LocalState`, so *configured twice* stops being representable rather than being
checked for.

**A target** — 0 or 1 mod folder and 0 or 1 savegame folder, paired. That pairing is what makes the
rest fall out: a save in target T's savegame folder was played against target T's mod folder, and
nothing else.

**A target is a value the adapter returns, not a persisted entity.** No id, no row, no list the user
manages. A game that needs more than one says so in its `LocalSettings` — which is where the BeamNG
adapter will offer each folder, and offer leaving one blank.

- [x] **`ILocalModAdapter.ModFolder` becomes `ModTargets`**, a keyed list of
      `(Key, DisplayName, Path)`. The key is adapter-defined and stable, because it ends up in a
      filename. Farming Simulator returns one and names it nothing; a blank folder in `LocalSettings`
      is a target the adapter omits rather than one with a null path.
- [x] **`PersistedGame.ModFolders` stays a first-class persisted field**, derived but written
      whenever settings are saved. This is not redundancy: store eviction and the drift candidate
      list both read a folder path **without hydrating an adapter**, because a game whose identity no
      loaded repo serves still owns its folders and still has a standing intent. Its `LocalSettings`
      are an opaque blob in that state. `ModFolder` was already this trick; it grew an `s` and
      nothing else. The callers that still read one folder take `Game.SingleModFolderOrNone`, the
      persisted-side twin of `SingleTargetOrNone`, and it dies with it in 2b.
- [x] **One manifest per target**, `manifests/{game-identity}_{target-key}.json`. Not one per game:
      syncing the dedicated server must not rewrite the MP client's manifest, and
      atomic-write-per-folder is what keeps a half-finished apply safe.
- [x] **A manifest for a target that no longer exists is stale, and is dropped.** That is half of
      the orphan question the fake adapter makes reachable, and it belongs here rather than in slice
      1 because until the manifest is keyed on a target there is nothing to orphan. Emptying a
      settings field and an adapter author renaming a key produce the same file and nothing can tell
      them apart, which is fine for a manifest: losing one costs a rescan. The other half — a
      **binding**, which is a savegame this machine is still holding — is not fine, and is
      [decided in slice 3](#one-savegame-held-per-game).
- [x] **The store encodes what it puts in a filename**, with a length cap — it is not a rule adapter
      authors have to obey. **Two** adapter-authored strings land in that name: the identity's
      discriminator, which a scripted adapter declares from inside its script, and the target key.
      A third entry beside the [two discriminator
      rules](04-game-adapters.md#two-rules-for-the-discriminator) would make a bad one into a
      manifest that cannot be written, found at sync time on somebody else's machine; encoding at the
      store cannot be violated at all. Ordinary keys stay legible —
      `_farming_simulator#fs25_mods.json` — and pathological ones are escaped rather than refused.
- [x] **No `Guid` on `Game`.** Everything that would have keyed on one keys on `GameIdentity`, which
      is already the `LocalState` dictionary key. Once the store is encoding anyway a `Guid` buys
      only a fixed-length filename, and costs the thing this phase spends its argument on: a game
      having one name rather than two.
- [x] **Bump `LocalState.CurrentVersion`.** A per-game dictionary defaulting to empty would read as
      "no profile is set anywhere", which is the one thing that must not be silently guessed. No
      migration, per the standing decision.
- [x] **A mod source per target, not per game.** `ModSourceId.ForGame(identity)` offers one scan
      source per game; with three targets a BeamNG import would look in one folder of three and
      quietly report what the other two hold as missing. `ModSourceKind.Game` is user-facing as
      *"Game install"* and wants the target's name where there is more than one. `ModSourceId` is
      persisted — it is what remembers *do not look in this folder* — so the format change drops
      those preferences; harmless, and 2a's version bump has already dropped them once.
      `ProfileModsEditorPageViewModel.ScanGame`, the drift notice's deep link, scans a target.

### A fake adapter with optional targets, before anything needs one

Farming Simulator has one target and there is no BeamNG adapter yet, so **every multi-target path
would otherwise ship having never run.** The fake is written in slice 1, before the code that needs
it exists, and it is what the rest of the phase is developed against.

- [x] **Targets driven by its `LocalSettings`**, the way the real BeamNG adapter will be: three
      optional folders, each present only when its field is filled in. That makes the whole matrix
      reachable from one adapter — 0, 1, 2 and 3 targets — rather than needing a fake per shape.
- [x] **Savegame folders independently optional.** A target with mods and no saves, one with saves
      and no mods, and one with both all have to be ordinary — it is the pairing the whole phase
      rests on, and the MP client is exactly a target whose saves live somewhere else.
- [x] **The transitions are reachable, which is what the fake is for.** Emptying a target's field
      removes a target; emptying only its mod folder leaves a target still holding savegames;
      neither renumbers the others. Those are the fake's own tests. **What happens to the things
      behind a removed target is decided where they are keyed** — the manifest in 2b, the binding
      in 3 — because until then there is nothing keyed on a target to orphan.
- [x] **Renaming a target key is the same event.** An adapter author changing `"mp"` to
      `"multiplayer"` orphans both, and nothing can tell that from a removal — which is the argument
      for keys being adapter-stable, and it is
      [written down in 04](04-game-adapters.md#targets). The fake pins its keys in a test that
      looks like it asserts constants, because that is exactly what it is for.
- [x] **`RequireSingleTarget` stays covered too.** Slice 1 and 2a hold every caller to one target, so
      a test that the helper throws on two is what stops that scaffolding becoming silently
      first-target-wins. **None is not the same failure**: a game whose settings point at no folder
      is an ordinary answer, so `SingleTargetOrNone` is the forgiving form the ownership check uses
      and `RequireSingleTarget` says so in a sentence somebody can act on. Both die in 2b.

### Activating is intent; applying is work

They have been one word and one button, and the split is load-bearing everywhere below.

| | Activate | Apply |
| --- | --- | --- |
| What it is | Intent | Work |
| Scope | The game — one profile | Each target, independently |
| Sets | `Game.ActiveProfile` | Files on disk; that target's manifest |
| Can fail | Only by **refusal** | Yes, per target |
| On failure | Nothing is recorded | Intent stands, the target is drifted |

- [ ] **Activating implies applying; applying never implies activating.** Activation runs the apply
      immediately after — one gesture. *Save and apply* in the mod list editor is pure apply: the
      profile is already active on whatever game follows it.
- [ ] **A failed apply does not retract the activation.** The game still means to be on that profile,
      the target is drifted, and the app-level notice carries it from there — which is
      [the next section](#an-intent-that-was-not-carried-out-is-drift), and is not true today. Two
      things stop an activation happening at all: a held savegame refusing it, and the user declining
      the plan.
- [ ] **That line already exists and only needs naming.** `ProfileApplyOutcome.RecordsIntent` is
      false for `Refused` and `Declined` and true for `Unavailable` and `Failed` — refusals are
      intent-level, failures are work-level. The split makes it structural: check the refusals,
      record the intent, then do the work.
- [ ] **The plan confirmation moves to once per activation**, showing what happens across every
      target, rather than once per target. Otherwise declining the server's plan while accepting the
      client's leaves an activation half-consented-to.
- [ ] **`InstanceActivation` becomes `ProfileActivation`**, and its two kinds stop describing a label
      and start naming which verb runs.

### An intent that was not carried out is drift

The bullet above claims the notice carries a failed apply. **It does not, and has never had to** —
today a failed activation is uncommon enough that nobody has been left in the state. This phase makes
it ordinary: one intent, several targets, and *the dedicated server is locked while the client
applies fine* is the normal BeamMP evening. That leaves one target on the old mod list, silently,
which is the exact thing this phase exists to prevent.

Two things combine to produce the silence. The manifest is written **only on success**, so a failed
apply leaves one describing the *previous* profile — and `InstanceDriftService.Check` early-returns
`NeverSynced` for a manifest whose `ProfileId` is not the active one, while
`InstanceDrift.IsDrifted` is `Status is Drifted || HasSavegameDrift`. `NeverSynced` is neither, so it
never reaches the notice.

Two failure shapes reach it, and the second is the one the word is wrong for:

| Where the apply failed | The folder | Reported as |
| --- | --- | --- |
| Fetch — download or store population | Untouched; sync stops before the destructive phase on purpose | `NeverSynced`, silent |
| Remove or install | Genuinely half-applied — neither profile | `NeverSynced`, silent |

- [ ] **Split `NeverSynced` in two.** *No manifest at all* — a fresh install, discarded local state —
      is genuinely nothing-known, promises nothing, and stays quiet. *A manifest describing a
      different profile* is, under the two verbs above, **definitionally intent recorded and work not
      done**, which is the cleanest description there is of a target needing an apply. It becomes
      `NotApplied` and `IsDrifted` includes it. Nothing has to be invented: the split names the state.
- [ ] **The third case in that guard gets its own sentence.** A manifest describing a different
      *folder* is the settings having been repointed, not an apply that did not happen, and it should
      not inherit the wording of one.
- [ ] **Surfacing only — there is nothing to repair.** A later re-apply already produces the right
      plan: reconciliation works from the folder's contents, and the planner reads the manifest purely
      as a filename-size-time to hash cache, which is profile-independent. The state is unprompted,
      not wrong. See [07 — Mod sync design](07-mod-sync-design.md#it-has-to-be-unmissable-everywhere).
- [ ] **Re-apply on this drift names the target**, since with several of them the interesting half is
      *which* one did not get there.

### One profile per game

- [x] **`Game.ActiveProfile` replaces `LocalInstance.ActiveProfile`.** Every target follows it. Two
      targets of one game cannot disagree, which is the BeamMP requirement stated as a type.
- [ ] **`ProfileApplyTargets` collapses to a lookup.** A profile belongs to a repo, a repo has one
      `GameIdentity`, games are keyed by identity — so a profile maps to exactly one game. Repo →
      game → its targets, with no search. `DescribeSaveAction` says *Save and apply*, always.
- [ ] **A folder that wants a different mod list does not get connected.** "A group runs the same
      mods at the same versions" is the premise of the system, so a private singleplayer mod set is
      out of scope by construction. The two ways out are a repo of your own for it, or leaving the
      folder out — which the adapter offers as a blank field in `LocalSettings`.

### One savegame held per game

- [ ] **The hold limit counts per game, not per target.** `SavegameHoldRules.FindConflictingHold` is
      asked once for the game. You play one save at a time; hosting one save on the server while
      playing another in singleplayer would hold two of the group's saves and block two people.
- [ ] ***Take a copy* is the escape hatch**, unchanged: no claim, no binding, an ordinary
      unrecognised slot. It is already the answer to "I want to look at another save without holding
      it".
- [ ] **`IHeldSavegames` splits along the seam it already has.** `ObserveAsync` and `CheckDriftAsync`
      stay per target — the bytes are in a folder. `GetRequiredRevision` and `DecideApply` move to
      the game. The interface was keying both halves on one id; only the keys change.
- [ ] **A binding for a target that no longer exists must not vanish with a settings edit.** The
      other half of the orphan question, and the half that is not droppable: a binding is a savegame
      this machine is still holding, and a claim somebody else is waiting on. A settings edit and an
      adapter author renaming a key are indistinguishable here too, so the answer cannot be "work
      out which happened" — it is that the hold survives a target it can no longer address, and the
      game says so where holds are shown. The manifest half is
      [dropped in 2b](#the-shape); this one is why they are two bullets.
- [ ] **A slot is identified by `SavegameSlotRef(TargetKey, SlotId)`**, a compound value in the shape
      `GameIdentity` and `ActiveProfile` already have — not a prefixed string, which would invite
      parsing. `{target}:{slot}` exists only as the persisted rendering, exactly as
      `_farming_simulator#fs25` is for an identity, and the key is also what groups the picker.
      *The type landed in slice 1 with `ModTargets`, since it is the same contract; nothing is
      addressed by one until here.*

      **Uniqueness across the game is then a construction rather than a contract.** An adapter mints
      ids unique within its own target, which it cannot get wrong, and nothing has to be asked of it
      or tested. The alternative — global uniqueness as an adapter obligation — puts two targets'
      slots on one binding the first time somebody numbers from one twice, and
      `SavegameBindingStore`'s one-binding-per-slot rule would enforce the collision rather than
      catch it.

### Play attribution stays per target

The one thing that does **not** move up to the game, and the reason is worth writing down because
per-game looks simpler and is not.

- [ ] **`ObserveAsync` goes on reading the manifest of the folder it is about to rewrite.** It is
      already called from inside `WriteManifestAsync`, which is already per folder; with N targets it
      is that same loop N times. Per-game would mean *adding* an activated-revision field and logic
      to prefer it over the manifest — more code, for a worse number.
- [ ] **The number has to be observed, not declared.** A failed apply never reaches
      `WriteManifestAsync`, so nothing is attributed, which is correct: that folder did not change,
      so what is being played there did not change either. Record the *activated* revision instead
      and the case breaks exactly where it matters — activate rev 12, the server's apply fails, the
      folder is still physically on rev 8, and a check-in stamps the version with 12. The save then
      reproduces wrong for whoever checks it out next.
- [ ] **Activated-but-not-applied becomes a normal state** under this phase rather than an
      exceptional one, so divergence gets *more* reachable, not less. That argues for observing
      harder, not for trusting intent.
- [ ] **Both existing guards survive verbatim**: a folder on a *different* profile records no number
      at all — the hash still moves, because the bytes did — and the no-work path still observes,
      because a revision can move without a single mod doing so.

### Interface

- [ ] **No instance list in the sidebar, and no current-instance dropdown.** The dropdown was the
      answer to "which folder does this act on" while policy lived on folders. Under one game per
      machine there is no such question, so it is not built.
- [ ] **Activation lives on the profile page only**, and loses its instance picker —
      `ProfilePageViewModel.SelectedInstance` and `HasInstanceChoice` go with it. The target is the
      game.
- [ ] **Check-out is one flat slot list** across every target with a savegame folder, grouped under a
      target header only where more than one has them. No instance step. Slots are already labelled
      with the game's own name for the save, so the list reads the same at one target or three.
- [ ] **Publish moves to the repo's Saves page**, where it can be reached without opening a game
      page. It is still inherently about a slot; the slot list is the same one.
- [ ] **Drift reports per target**, naming the folder only where the game has more than one — *"your
      MP client folder has 2 differences"*.
- [ ] **Connect game loses its name field.** A game is called Farming Simulator 25 and
      `Adapter.DisplayName` already says so. The name box, its "Game" default and its
      uniqueness-within-scope check all go; connecting becomes filling in the settings form.
- [ ] **`GamePage` is reached rarely and on purpose**: its settings, its targets and its slot list.
      Nothing in a normal evening requires opening it.

### Naming

"Instance" leaves the user-facing vocabulary entirely. The rename **rides each slice** rather than
being a pass of its own, because every file it touches is a file these slices already open.

| Now | Then |
| --- | --- |
| `LocalInstance` | `Game` |
| `PersistedLocalInstance` | `PersistedGame` |
| `LocalInstanceRepository` | `GameRepository` |
| `InstanceScope` | `GameIdentity` |
| `IInstanceGameAdapter` | `ILocalGameAdapter` |
| `IInstanceModAdapter` / `IInstanceSavegameAdapter` | `ILocalModAdapter` / `ILocalSavegameAdapter` |
| `InstanceSettings`, `GetInstanceSettingsTemplate`, `WithInstanceSettings` | `LocalSettings`, `GetLocalSettingsTemplate`, `WithLocalSettings` |
| `IInstanceModFolders` | `IModFolders` |
| `InstanceDriftService` / `Monitor` / `Report` | `DriftService` / `DriftMonitor` / `DriftReport` |
| `InstanceActivation` | `ProfileActivation` |
| `CreateLocalInstancePage` | `ConnectGamePage` |
| `EditLocalInstancePage` | `GameSettingsPage` |
| `InstancePage` / `InstanceSavegamesPage` | `GamePage` / `GameSavegamesPage` |

`Game` means *your local installation* on the client, while in conversation "the game" is what the
adapter is for — which lives on `Repo.Adapter.DisplayName`. They never appear together, and
`Repo.Game` does not exist.

`ConnectGamePage` is the one that was already true: the menu item has said *Connect game* since it
was written.

### The order to build it in

Sequential, and genuinely so — this is a stack, not a set. Three things are independent of it and
can land in any order beside it: relocating publish (which gates the sidebar deletion in slice 5),
the store's filename encoding, and the pass over 02, 04, 05 and 06 at the end.

**Widen the interface, keep the callers narrow, widen the callers later.** Slice 1 has `ModTargets`
return a list while every caller takes `RequireSingleTarget()` — a named helper rather than
`.Single()`, so the tripwire explains itself. Farming Simulator behaves identically and every
existing test stays green. The helper dies in slice 2b, which is where multi-target becomes real.

- [x] **1. The adapter answers with targets.** `ModTargets`, `SavegameSlotRef`, the store's filename
      encoding, the adapter-layer renames, `RequireSingleTarget` at every caller — and the fake
      adapter above, which is written here because everything after this is developed against it.
- [x] **2a. The `Game` and its state.** `PersistedGame`, `Game`, `GameRepository`,
      `LocalState.Games` keyed by `GameIdentity` with its JSON key converter, the version bump.
      Targets still resolve through `RequireSingleTarget`.
- [x] **2b. Re-key the per-folder stores** — the manifest, the drift service, store eviction and mod
      sources. One slice rather than four, because it is the same edit four times and splitting it
      means four rounds of half-compiling. `RequireSingleTarget` is deleted here.
- [ ] **3. Savegames go per game.** The hold limit, the `IHeldSavegames` split, slot identity and
      grouping. Attribution is deliberately untouched.
- [ ] **4. Activate and apply become two verbs**, with the confirmation moved, `RecordsIntent` made
      structural, and `NeverSynced` split so an activation that did not land is drift. The split
      belongs in this slice rather than slice 2: it is only *definable* once the two verbs are, and
      until then there is no such thing as an intent that was not carried out.
- [ ] **5. Interface.** The sidebar, the profile page's activation, the check-out list, publish, the
      drift wording, and Connect game losing its name.

### What this deletes

Worth knowing before starting, because it is most of the argument for doing it:

- **The sidebar instance list and `InstanceItemViewModel`** — and with them the only consumer of
  `MenuItemViewModel`'s title-tracking machinery, which exists solely because instances have no
  server refresh to rebuild their menu entries. See [05 — Client](05-client.md#navigation).
- **`RepoSavegamesPageViewModel.Offer`'s host ranking** — "an instance that would accept, else one
  following this profile, else the first". There is one game, so there is nothing to rank.
- **Half of `InstancePageViewModel`** — the profile dropdown, the apply button and the hold-lock
  wiring, all of which are the second copy of a control the profile page already has.
- **`SavegameRowRules.NoInstance` and its `hasInstance` parameter.**
- **Three validation rules**: instance name uniqueness within a scope, the name field itself, and
  most of the cross-scope duplicate-folder check — which collapses to "a game's targets are distinct
  from each other" plus "paths do not collide between games". Two of the three went in 2a: the
  uniqueness rule had nothing left to be unique against, and the folder check was rewritten over the
  list. The name field itself waits for slice 5, where the box goes.

### Settled

- **No per-target profiles, ever.** Targets of one game cannot disagree. A folder that wants its own
  mod list is a folder that is not connected, or one belonging to a repo of your own.
- **No instance groups.** The set of targets on a profile *is* the group, and it is maintained by the
  one fact that already exists. A second way to express membership is a second way for it to
  disagree with the first.
- **The current-instance dropdown is not built.** It was the right answer to a question this phase
  removes.
- **Attribution is observed, never declared** — except a published savegame's first version, which
  declares because the bytes predate ModsDude and nothing recovers what was in the folder then.

## Deliberately not planned

- **Dependency resolution between mods.** A profile is a pinned list, not a constraint
  system. If a mod needs another, someone adds it.
- **A web client.** `ModsDude.Client.Core` is UI-agnostic and could support one; there is no
  reason to build it.
- **Public sign-up and self-service repo creation.** `IsTrusted` stays a manual database flip.
  For a group this size that is the correct amount of machinery.
- **Multi-tenancy, quotas, abuse handling.** Not this project.
