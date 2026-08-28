# Plan

## Where this is going

ModsDude is a personal project for a small group of players, built so that adding another
game is a matter of writing an adapter rather than reworking the system. It is not a
product, and the plan below does not pretend otherwise: no multi-tenancy, no billing, no
public onboarding. What it *does* have to do is work reliably at real data volumes —
1,000–2,000 mods in a profile, thousands of registered versions per repo — because that is
what a Farming Simulator install actually looks like.

The organising principle: **finish one vertical slice end to end before widening.** The
system today has a broad, shallow surface — most screens exist, few of them do anything.
The value arrives the first time someone can point a game install at a profile and have it
come out right.

## Where it stands

| Area | State |
| --- | --- |
| Identity, users, memberships | Working |
| Repos, adapters, base settings | Working |
| Local instances | Working |
| Profiles (create/rename/delete) | Working |
| Mod dependencies | Server complete, no UI |
| Mod upload | Server complete, client wired up, import does not call it yet |
| Mod download | **Does not exist** |
| Profile → instance sync | **Does not exist** |
| Savegames | Placeholder interfaces only |
| Tests, CI, deployment | None |

---

## Settled architecture decisions

Decisions taken after the initial write-up. All are prerequisites for Phase 3.

### Instances are adapter-scoped, and an instance is one mod folder

`LocalInstance` is currently scoped to a **repo**, which breaks as soon as someone joins more
than one repo for the same game: a member of three Farming Simulator repos has one
installation but must configure it three times, and all three instances believe they own the
same mods folder.

An instance is scoped to the **game adapter** instead — configured once, listed under every
repo using that adapter. It also gains an explicit **active profile**, a `(RepoId, ProfileId)`
pair, since sync makes a folder match a profile exactly and only one repo can own a folder at
a time.

**An instance is one mod folder, not one installation.** Games keeping mods in several places
get several instances. BeamNG.drive with BeamMP needs three — singleplayer, MP client, and a
dedicated server all read from different directories, even where two of them belong to the
same install. The model tracks folders and does not assume there is a game installed at all,
which keeps it far simpler than modelling installations with child targets.

### The content store is per volume and configured machine-wide

Settled as per-volume rather than per-repo. Content addressing makes sharing safe, and the
store is not adapter-scoped either — it holds hashes and has no notion of which game a file
belongs to.

Each disk holding mod folders gets a **store assignment**, chosen by the user: its own store,
materialising by hardlink, or a store on another disk, materialising by copy. The second
trades sync time for space on the constrained disk, and is a legitimate choice rather than a
fallback — the drive with the game on it is frequently not the drive with room on it.

The assignment, store path and maximum size therefore live in a **new machine-wide settings
bag on `LocalState`**, keyed by volume, and stay out of both instance and repo settings. See
[07 — Mod sync design](07-mod-sync-design.md#where-the-store-lives).

### No migration

The system has no users. `LocalState.Version` gets bumped and old state is discarded rather
than migrated.

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
- [ ] **Flatten `Mod` and `ModVersion` into one entity** keyed `(RepoId, ModId, VersionId)`. A
      mod record is really *of* a version, not a container of them, and nearly all the data was
      already on the version. Removes the create-or-append branch in registration, the shadow FK
      properties and owned-collection mapping, the `Versions` auto-include that makes
      `GET repos/{id}/mods` so heavy, and the `Mod.RepoId` TODO. Full reasoning in
      [02](02-domain-model.md#flattening).
- [ ] **Flatten the wire format with the entity.** `ModDto` currently nests
      `ModVersionDto[]`; it becomes one DTO per version. Leaving the response nested would make
      the client re-group on receipt — exactly the shape the flat client model exists to avoid.
      Regenerate the NSwag client afterwards.
- [ ] Keep `SequenceNumber` **contiguous**, with the existing shift-on-insert and close-on-remove
      logic — just moved off the entity, since there is no parent left to hang it on. A sparse
      key was considered and rejected: the shift is tens of rows for one mod, mutated in memory
      and written by one atomic `SaveChanges`, so it was never the problem the aggregate was
      solving. Gaps would only add an exhaustion case to reason about in exchange for nothing.
- [ ] `ModDependency.CanBeUpgraded()` / `Upgrade()` lose their `ModVersion.Mod.Versions`
      navigation; pass the candidate versions in, or move the operation to the endpoint that has
      to query for them anyway.
- [ ] Do this **before** anything registers mods in earnest — one migration and no real data
      makes it nearly free now, a data migration later. Same argument as the casing fix.
- [ ] Add `ModVersion.ContentHash` (SHA-256, a first-class property — not a `ModAttribute`),
      populate it on registration, and expose it on `ModVersionDto`. Everything in Phase 3
      depends on it, and adding it later means a backfill.
- [ ] Move the server base URL into the WPF `appsettings.json` and apply it in
      `AddModsDudeClient` ([known issue](08-known-issues.md#the-clients-server-url-is-hardcoded-to-localhost)).
      Blocks ever running against a deployed server.
- [ ] Add a test project for `ModsDude.Server.Domain` and cover version sequencing,
      membership transitions, and the dependency rules. The two fixes above would each have
      been one test.

## Phase 1 — The mod catalog and the upload loop

Make the import page actually import, on top of a representation the profile editor can reuse.
Full design in [09 — Mod representation and the catalog](09-mod-catalog.md).

- [ ] **Normalize mod id casing at the adapter boundary** and carry it in a key type
      ([known issue](08-known-issues.md#mod-ids-are-case-sensitive-in-blob-storage-and-case-preserving-on-disk)).
      Do this before anything registers mods in anger — afterwards it is a data migration.
- [ ] `IsLocal` / `IsOnServer` per **version**, not per mod. Derived three-state where a page
      needs it, never stored. Split `ModStatus` into these facts plus a per-context display
      status.
- [ ] A merged **flat** `CatalogModVersion` in `Client.Core` — one record per version, no
      parent — so one row view model serves local, server and both. Flat all the way through:
      the server entity is flat, `LocalMod` is already one record per file, and a row view model
      wraps one version, so a parent would be a shape invented mid-pipeline. Grouping for the
      version selector and update detection is a `ToLookup(x => x.ModId)` built where needed,
      which beats maintaining a nested model that must be rebuilt every time a source checkbox
      recomposes the set.
- [ ] Rename `LocalModImage` → `ModImage`. **Delete** the client-side `Mod` that wraps `ModDto`
      rather than renaming it — nothing references it but `ModFakers`, and its only job was the
      latest-versus-older grouping the lookup now does on demand.
- [ ] **Mod sources.** Scan a set of sources rather than a fixed folder: every instance's mod
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
- [ ] Extract a repo-scoped `ModCatalog` service: merge the source scans with
      `GET repos/{id}/mods`, **caching per source and composing the merged view on demand** so
      that toggling a checkbox is instant and adding a source scans only that folder. Cache the
      **`Task`** rather than the result so concurrent callers join one scan. Invalidate
      explicitly on import and on instance-settings change, expose Rescan per source and for
      all, and report per-source failure so an unplugged drive marks one source bad rather than
      failing the catalog. Move the 150 ms delay and the cancellation behaviour in unchanged.
- [ ] **Split the upload-link problem types** into `FileAlreadyPresent` and `AlreadyRegistered`
      ([known issue](08-known-issues.md#the-two-upload-link-rejections-are-indistinguishable)).
      Without this a mod whose import failed after upload can never be retried.
- [ ] Implement import: per mod, link → upload → register, 4–6 mods concurrently. Per-mod
      ordering protects the never-register-before-upload invariant; concurrency across mods
      does not weaken it. Treat both "already present" responses as success, which makes retry
      idempotent and covers a teammate importing the same version concurrently.
- [ ] Handle **several new versions of one mod in a single import** — one from a mod folder, one
      from Downloads. Compute positions against the final intended order, then register in
      ascending order as *insert before the next known version*, so each step is individually
      valid and no batch-placement API is needed. Versions of the same mod must register
      **sequentially**, since each insert depends on the previous; concurrency stays across
      distinct mods. Note this can move an already-registered version's sequence number.
- [ ] **The version comparer and the arbitration dialog are prerequisites of this**, not Phase 7
      work — placing several incoming versions needs them. See the note on Phase 7.
- [ ] **Assert both neighbours when placing a version**, not just the one to insert before.
      *Insert v2 between v1 and v4*, rejected if v4 no longer immediately follows v1. Relative
      placement alone stops collisions but still permits a silently wrong order when two members
      insert against a state neither has seen the other change — which offers a downgrade as an
      upgrade. Optimistic concurrency using only what the client already computed, retried
      through the refetch loop import already has.
- [ ] Per-row progress and error state. At two thousand mods a single global spinner cannot
      distinguish a working import from a hung one.
- [ ] Compute the SHA-256 while uploading and send it with registration.
- [ ] Write imported files into the content store as they go (see Phase 3) so importing an
      existing install leaves the store warm.
- [ ] **Store the icon and every store image server-side**, so a mod nobody has locally still
      renders with its artwork instead of initials and an empty details dialog. Full design in
      [09](09-mod-catalog.md#mod-imagery).
- [ ] Two derivatives per image — a 128 px thumbnail (~6 KB) and a full at native resolution
      capped at 1024 px (~50 KB), as WebP — generated **client-side at import**, since only the
      client can decode DDS (including the managed BC7 path) and the server has no business
      opening mod files. Measured over 540 real mods, store art is only 1.2% of archive bytes
      and tops out at 1024 px, so the full derivative is a **re-encode, not a downscale**; the
      saving is DDS to WebP. The thumbnail is what matters: it turns a cold 540-row list from
      ~27 MB into ~3 MB.
- [ ] No separate storage of originals — they are already inside the mod blob. The case against
      shipping them is transfer and decode (roughly an order of magnitude more bytes, plus a
      managed BC7 decode, to render the same 64 px), not storage, which measurement shows would
      have been affordable.
- [ ] **Registration decides the imagery source, not local availability.** Registered versions
      always render from the server's derivatives, even when the mod file is on this machine;
      only unregistered import candidates are extracted from their archive. Hunting for the local
      file to gain resolution nobody wants in a 96 px strip costs exactly the work derivatives
      exist to avoid. It also gives stable hash cache keys, uniform presentation across a list,
      and means nothing ever reads images out of the content store.
- [ ] **Opportunistic backfill.** Since imagery never blocks registration, a version can exist
      with no derivatives. A client about to render one while holding the mod file should
      generate and upload them rather than fall back locally — closing the gap for everyone, and
      removing the need for a separate backfill sweep.
- [ ] Content-address the image blobs in their own container, `mod-images/{hash[0..2]}/{hash}`.
      Versions overwhelmingly reuse artwork between releases, so dedupe collapses ~15,000
      references to ~3,000 blobs for a 3,000-version repo — on the order of 150 MB of fulls and
      20 MB of thumbnails. Server storage is not a constraint here; transfer and decode are.
- [ ] **A machine-wide client image cache**, configured in `LocalState.Settings` beside the
      stores with its own path, size cap and LRU. One per machine, not per volume — images are
      always copies, so the hardlink constraint that makes stores per-volume does not apply.
      Keep it distinct from the content store, which is what keeps "nothing reads images out of
      the content store" true. Cache server derivatives by their own hash, with no size suffix,
      since they arrive pre-sized.
- [ ] `ModVersion` gains an **ordered collection of image references** (hash, kind, position,
      filename) — structural, so not `ModAttribute`s. References, not ownership: a blob is
      collectable once nothing points at it.
- [ ] **Imagery must never block registration.** The mod file is verified before metadata is
      written; images get the opposite treatment, uploaded best-effort after the fact and picked
      up by the opportunistic backfill above. An import of 2,000 mods cannot half-fail over a
      timed-out thumbnail.
- [ ] A **batch existence check** before uploading — "which of these hashes do you have?" After
      the first import most images are already present, and 2,000 mods x 20 images is 40,000
      uploads that mostly need not happen.
- [ ] Serve them through the API — `GET images/{hash}` at Guest level, redirecting to a SAS or
      streaming — rather than per-image SAS minting, which inverts the mod-file trade-off for
      files that are small and fetched in bulk. Affordable because a content-addressed image is
      immutable and so cacheable forever; the client's existing disk cache then fetches each one
      once per machine, ever.
- [ ] Stop dropping `Description` when mapping `ModVersionDto`
      ([known issue](08-known-issues.md#the-client-drops-description-from-server-mod-versions)).

## Phase 2 — Profile contents and mod management

The profile is currently a name with nothing in it, and Import and Manage are separate pages
showing overlapping data under different rules.

- [ ] **Merge Import into Manage.** One list over the catalog, presence filter chips
      (All / In repo / On disk only / Unused), bulk import as a selection mode that reveals the
      footer bar the import page already has.
- [ ] Put the source list with its checkboxes on both Manage and the profile editor. Show which
      source a row came from only when more than one is enabled — with a single source it is
      noise on every row.
- [ ] Add profile-usage information to `ModDto` or a dedicated endpoint. "Unused" cannot be
      computed client-side safely — deleting on a partial view risks removing a version a
      teammate's profile just picked up.
- [ ] **Reorder a mod's versions by hand** from Manage — the backstop for an ordering that is
      wrong for reasons optimistic concurrency cannot catch: a comparer that guessed badly, or an
      arbitration someone regrets. Same operation the arbitration dialog already performs.
- [ ] Add delete endpoints for a mod version and for a whole mod
      ([known issue](08-known-issues.md#no-delete-endpoint-for-mods-or-versions)). "Remove whole
      mod" needs its own path, since `RemoveVersion` refuses the last one.
- [ ] Replace the `ProfileModsEditorPage` stub with the two-list editor over
      `GET/POST/PUT/DELETE .../modDependencies`. The left list is the union of registered and
      local mods, so a mod can be added *and imported* without a detour to another page.
- [ ] **Updates render on the right, not the left** — an in-profile mod with a newer version
      shows an update affordance on its own row, plus an "N updates available" batch action.
      Putting it on the left would place the same mod on both sides at once.
- [ ] Right-hand rows carry a version selector and a `Locked` toggle, since the list is
      keyed by `ModId` and moving a mod rightward means choosing a version.
- [ ] **Import-on-save, not import-on-drag.** A local-only mod moving right is marked pending;
      Save uploads and registers, then updates the dependencies last in one request. Uploading
      on drag makes Cancel meaningless and litters the repo with mods nobody kept.
- [ ] **`ModVersion.Locked`** — a new domain property and column, alongside the existing
      per-profile flag. Rename `ModDependency.LockVersion` → `Locked` to match, and add
      `IsEffectivelyLocked => Locked || ModVersion.Locked` so the rule lives in one place.
- [ ] **The adapter sets `ModVersion.Locked` at every registration**, re-derived from the file
      rather than inherited — a Farming Simulator map mod declares its maps in `modDesc`, which
      the adapter is already parsing, so the answer comes out the same for every version. No
      prompt at import. An adapter can never set `ModDependency.Locked`.
- [ ] Accept the consequence: **no repo-wide user override.** Someone who disagrees with the
      adapter unlocks on the dependency, which is per-profile and survives version changes since
      `ChangeVersion` does not touch it. "Unlock" means "in my profile", not "in this repo" —
      the price of collapsing `Mod` into the version.
- [ ] **Not a `ModAttribute`.** Attributes are tags and categories and the system must never
      depend on one — `Locked` changes what a batch update is allowed to touch, so it is a real
      property. Same rule that put `ContentHash` in the schema.
- [ ] Expose `ModDependency.Upgrade` / `CanBeUpgraded`, which exist on the domain and have no
      endpoint. **"Apply all updates" skips locked mods entirely** and reports what it skipped
      ("Update 47 mods · 3 locked, skipped"). Save then cannot contain an unintended version
      change and needs no prompt at all. Sweeping locked mods in and prompting at save re-asks a
      question the user already answered, every time — which is how a safety prompt turns into
      noise people learn to dismiss.
- [ ] Changing a locked version is a deliberate per-row act with its own confirmation, carrying
      the reason it is locked. For bulk, make the skipped-count a link to a modal listing the
      locked mods with an **unchecked checkbox each** and the consequence spelled out per mod —
      the same dialog, reached deliberately rather than fired at every save.
- [ ] Add the unique index on `(RepoId, ProfileId, ModId)`
      ([known issue](08-known-issues.md#no-unique-index-backing-the-one-version-per-mod-rule)).

## Phase 2.5 — Rework instances and add global settings

Client-only, no server changes. Small, but it has to land before sync, because sync needs to
know which profile owns a folder and where the store lives.

- [ ] Move instances out from under repos in `LocalState`: key by instance id, carry
      `GameAdapterId` instead of `RepoId`. Bump `Version` and discard old state.
- [ ] `Repo` offers the instances whose adapter `Id` matches, rather than owning a list. The
      sidebar keeps listing them under each repo exactly as it does now.
- [ ] Add `ActiveProfile: (RepoId, ProfileId)?` to the persisted instance.
- [ ] Add `LocalState.Settings` — the first machine-wide client setting — holding, per volume,
      which store serves it, that store's path, and its maximum size.
- [ ] A settings page. There is no such page today; the app has nowhere to put a global
      setting.
- [ ] Rework `CreateLocalInstancePage` / `EditLocalInstancePage` accordingly. The name
      uniqueness check moves from per-repo to per-adapter.

## Phase 3 — Sync

The core feature. Full design in [07 — Mod sync design](07-mod-sync-design.md).

- [ ] `POST api/v1/files/createModDownloadLink` — Guest-level, read SAS, mirroring the upload
      endpoint.
- [ ] The content store: `{storeRoot}/blobs/{hash[0..2]}/{hash}`, **per volume**, shared by
      every repo and instance it serves, with a configurable location and maximum size.
- [ ] **Per-disk store assignment.** For each disk holding mod folders, let the user choose
      between a store on that same disk (hardlink) and a store on another disk (copy). The
      disk with the game on it is often not the disk with room on it: mods on a small `C:`
      served by a store on a roomy `D:` means `C:` holds only the active profile while the
      cache history — the part that grows — lives on `D:`. It costs sync time, since every
      install and replace becomes a cross-volume copy. Present both sides of that trade-off in
      the settings UI, and treat cross-disk as a deliberate choice, not a misconfiguration to
      warn about.
- [ ] **Verify on ingest.** Hash every download and compare it against what the server
      declared before storing. This single check is what makes a shared store safe between
      repos; without it the whole isolation argument collapses.
- [ ] Materialisation: hardlink where a disk is served by its own store, copy otherwise.
      Warn only when a same-disk assignment silently falls back to copying — exFAT, a network
      path — since that is the case where the user is paying the cost without having chosen
      it.
- [ ] **Install looks across stores before downloading.** Serving store first, then any other
      disk's store — copying the blob into the serving store — and only then the network. Safe
      because every store is content-addressed, so a blob at address `H` is content that
      hashes to `H` wherever it sits. Hash it as it streams past during the copy; the bytes
      are already in memory, so verification is nearly free.
- [ ] **Uninstall keeps a copy only when nothing else has one.** If any store on the machine
      already holds the hash, delete the mod folder's file outright. Move it into the serving
      store only when no store has it at all — otherwise a mod that lives on `D:` gets
      needlessly duplicated onto `C:` just to be uninstalled.
- [ ] LRU eviction of store blobs against the size limit, counting only entries the store
      uniquely holds (link count 1), since anything hardlinked into a live mod folder reclaims
      nothing. Never evict what an active profile needs.
- [ ] Extend `IInstanceModAdapter` with the write side: where a mod file belongs, what it
      should be called, install, uninstall. Filesystem operations stay in the sync engine;
      adapters only supply paths.
- [ ] `ModSyncService` in `Client.Core`: plan, then execute. Populate the serving store with
      everything **this profile** needs first — not the repo's full mod set — so the
      destructive phase only ever runs against a complete store.
- [ ] **Uninstall rules, exactly as designed.** A version registered in the repo is
      recoverable — make sure some store has it, then delete. Anything unrecognised goes to
      the Recycle Bin, never to `delete`. Warn before executing, list what is affected by name,
      and say where it is going. This is the rule that makes the tool trustworthy; it is not
      negotiable for a shortcut.
- [ ] A sync page: plan summary (install / replace / uninstall / quarantine), the
      confirmation, per-mod progress, cancellation.
- [ ] **Write a sync manifest** on completion — installed files with hash, size and mtime — so
      drift detection is a directory listing rather than opening 2,000 archives. One file per
      instance at `manifests/{instanceId}.json`, beside `state.json`: not inline in `LocalState`,
      which is loaded eagerly and rewritten on every instance change, and not in the game's own
      folder, which an in-game updater rewrites.
- [ ] Keep the two records distinct. `ActiveProfile` is a **source of truth** — a folder cannot
      tell you which profile it was meant to be, so losing it loses the intent irrecoverably. The
      manifest is an **optimisation** — reconciliation works without it, straight from folder
      contents against the profile, so losing it costs a scan and nothing more.
- [ ] Comparing the manifest's mod set against the profile's current dependencies also catches
      **someone else having edited the shared profile** since this instance synced — no revision
      number on `Profile` required, which is just as well since it has none.
- [ ] Handle a **dangling `ActiveProfile`** (profile deleted, or the user removed from the repo):
      say so and offer to pick another, rather than failing or reporting drift against something
      unreachable. And `ActiveProfile` with no manifest — fresh install, discarded state — falls
      back to a full reconcile.
- [ ] **Drift detection and a Re-apply affordance**, on the instance row and the repo/profile
      overviews. In Farming Simulator mods are updated *inside the game*, which silently leaves
      the instance not matching its profile; today nothing anywhere would tell the user, and the
      re-apply that protects their save is the step easiest to forget. Detect and offer — never
      revert silently, which would undo updates the user deliberately made.
- [ ] **Call out drift on a locked mod specifically.** An unlocked mod at the wrong version is
      untidy; a locked map at the wrong version is a damaged savegame waiting to happen. Name the
      consequence rather than folding it into a count.
- [ ] Let the drift notice double as an import prompt — the drifted files are by definition
      versions the user now has and the repo may not, so "the game updated 6 mods, import them?"
      is the next step of the flow they were about to perform anyway.
- [ ] **Test whether the in-game updater rewrites mod files in place or renames over them.**
      In-place rewriting through a hardlink corrupts the shared store blob; rename-over breaks
      the link harmlessly. Decide the read-only-blobs trade-off after finding out — read-only
      fails loudly instead of corrupting, but also stops the in-game updater working.

**Done means:** two people on two machines pick the same profile and end up with byte-identical
mod folders, and neither loses a file they cannot get back.

## Phase 4 — Make drift unmissable

The flow this exists for: the user launches the game themselves, installs mods and runs
update-all from the game's own menus, and comes back. ModsDude was never in the path, so
launching the game from it does not help. What matters is what the user sees on returning.

- [ ] **An app-level notification, visible from every view** — not a banner belonging to one
      page. States that installed mods no longer match the applied profile, and persists until
      handled or dismissed. Lives in the shell beside the modal slot `MainWindowViewModel`
      already owns, but is **not** modal: the user must be able to keep working.
- [ ] Suppress it in exactly one place: the drifted profile's own mod list editor.
- [ ] Two actions — open that profile's mod list editor, or **re-apply the profile directly in
      one click**. Most of the time nothing needs changing and the user only wants their locked
      versions back.
- [ ] Dismissal lasts until the drift set changes or the app restarts, never permanently. A
      dismissed warning that never returns is a savegame silently at risk.
- [ ] **Save in the mod list editor re-applies by default.** The re-apply is what actually
      reverts an auto-updated locked map; making it a separate deliberate step is exactly how it
      gets forgotten. Applies to whichever instances have that profile active.
- [ ] *Save and apply* is the primary button at **one click**; *Save only* costs **at least one
      more**. Prefer a **split button** with the variant in its dropdown over a checkbox that
      retargets the main button: a checkbox is persistent visible state, and someone who ticks it
      once and leaves it ticked has silently turned a per-save decision into a standing mode. If
      a checkbox is used anyway it must reset after every save.
- [ ] Word it with the consequence, not just a caution — *"saves the profile but leaves your
      installed mods untouched; your locked mods stay at the versions the game updated them to.
      Only if you know exactly what you are doing."*
- [ ] **Derive the apply targets; never ask.** They are exactly the instances whose
      `ActiveProfile` is the one being saved. No checklist, no dropdown, no pre-selection — those
      all come from confusing *re-apply* (target determined) with *activate* (target chosen). A
      drifted instance is already in the derived set by definition.
- [ ] Scale the UI with the count: one instance shows nothing at all — the word "instance" never
      appears, which is the common case for most games. Two or more shows *Save and apply to N
      instances* with a read-only disclosure listing them. Zero shows plain *Save*.
- [ ] Offer activation as a **follow-up** when nothing is using the profile — *"No instance is
      using this profile. Use it on X?"* — rather than folding a mode change into a save.
- [ ] Move activation onto the instance itself. `EditLocalInstancePage` becomes the instance's
      real page: name, settings, active profile as a dropdown, drift status, Re-apply.
- [ ] **Activation from the profile side too**, on the profile *shell* so it is present on every
      sub-page rather than just Overview — `ProfilePageViewModel` already owns that
      sub-navigation. A dropdown to pick the instance when the adapter has more than one, and a
      plain button when it has one. Eligibility is by matching `GameAdapterId`.
- [ ] Label the control for what it will do: *Re-apply* when the instance is already on this
      profile, *Activate* when it is on another or none. Activation re-syncs the folder and
      uninstalls what the previous profile put there, so use the reconciler's plan preview as the
      confirmation rather than a bare "are you sure".
- [ ] While the mod list editor holds unsaved changes, disable the shell-level control and point
      at *Save and apply* — otherwise it silently applies the last-saved profile behind the
      user's pending edits.
- [ ] An instance that cannot be applied to right now — a dedicated server mid-session, a folder
      held by a running game — is reported and left drifted, which the drift notification already
      covers. That is a "not now", not a "not this one", so it needs no pre-selection.
- [ ] **The manifest comparison is the primary mechanism**, at startup and on window activation
      (debounced — `Window.Activated` fires on every alt-tab). It is the only one that works when
      ModsDude is closed while the game runs, which is the normal case. `FileSystemWatcher` is a
      latency optimisation on top, for when the app happens to be open; the design must not
      depend on having been running.
- [ ] The manifest is **frozen between syncs** — written on completion, never updated to follow
      the folder. A manifest that tracked the folder could not detect anything, since drift is
      the difference between the two.
- [ ] Write it **only on success**, atomically. A half-finished sync leaves the previous manifest
      and the next check reports drift, which is true; a partial manifest would claim a state
      that never existed.
- [ ] The cheap check is a **directory listing**, not opening archives: name, size and mtime from
      one non-recursive `EnumerateFiles` catches additions, removals and replacements. Open
      archives only for entries that actually differ. The recorded hashes are not read on this
      path — they are there so an uninstall knows which store blob a file matches.
- [ ] An unreachable folder — unplugged drive, offline network path — is **unknown, not
      drifted**. Say so quietly rather than warning about mods that may be fine.

Sequenced right after sync, ahead of the cosmetic work below: it closes the one failure mode
that silently damages savegames.

## Phase 5 — Fill in the shell

Everything the sidebar promises and does not deliver. Cheap individually, and worth doing
only once the core works.

- [ ] Repo → Members: the member list, add by username search, change level, kick. The server
      is complete.
- [ ] Overview pages for repo and profile — currently `ExamplePageViewModel`. What belongs
      here is whatever the sync flow turns out to need at a glance: instance status, drift
      from the profile, last sync.
- [ ] Use `LocalState.LastSelectedRepos` / `LastSelectedProfiles`, which are declared and
      never read, to restore where the user was.
- [ ] **Stop the sidebar navigating on drag.** A `ListBox` extends selection to whatever the
      pointer passes over while the button is held, and selection drives navigation, so dragging
      through the menu visits every item on the way. Nobody asked for that; it is why the import
      page needs its 150 ms delay. Worth fixing on its own merits.
- [ ] *Nice to have:* **drag a profile onto an instance in the sidebar to activate it.** Depends
      on the fix above — once a drag passes the threshold and `DragDrop.DoDragDrop` captures the
      mouse, selection stops following, which is exactly what makes the gesture possible. No
      drop-target filtering is needed: both lists belong to `RepoPageViewModel`, so everything
      visible is already scoped to the selected repo and therefore to its adapter. One direction
      is enough: "put this profile into that game" reads correctly, the reverse does not.

## Phase 6 — Scale and hygiene

Driven by the stated volumes, not by generic good practice.

- [ ] Give `GET repos/{repoId}/mods` **both** pagination and a delta form keyed on
      `Mod.Updated` ([known issue](08-known-issues.md#get-reposrepoidmods-returns-everything-unpaged)).
      They solve different problems: pagination bounds any single response, the delta bounds
      the steady state. Paginate the delta too — a first sync against an established repo
      returns everything.
- [ ] Stop full-list refreshing after every mutation
      ([known issue](08-known-issues.md#full-list-refresh-after-every-mutation)) — apply the
      returned DTO to the existing collection instead. Removes the need for the
      `*OfInterestChanged` selection dance.
- [ ] Return 401/403 for authorization failures rather than 400
      ([known issue](08-known-issues.md#authorization-failures-return-400)), and branch the
      client on `CustomProblemDetails.Type` rather than status code
      ([known issue](08-known-issues.md#client-error-handling-tests-for-a-status-code-the-server-never-returns)).
- [ ] Fix the duplicate-username crash
      ([known issue](08-known-issues.md#a-second-user-with-the-same-display-name-breaks-signup)).
      One collision and a real person cannot use the app.
- [ ] Authorize before loading in the membership endpoints
      ([known issue](08-known-issues.md#membership-endpoints-authorize-after-loading)).
- [ ] Either wire up the scope policies or delete them
      ([known issue](08-known-issues.md#scope-policies-are-defined-and-never-applied)).
- [ ] Delete `ModsDude.Server.Services`, `ModsDude.Server.Common`, the empty
      `ModsDude.Client.Cli` directory, and the stale `slnLaunch` entry
      ([known issue](08-known-issues.md#empty-and-duplicate-projects)).
- [x] A real README: what it is, what it needs, how to run it.
- [ ] CI in the empty `.github/workflows/`: build and test on push. One file.

## Phase 7 — Version ordering from version strings

> **This is sequenced wrong and most of it belongs in Phase 1.** Import can bring in several
> versions of one mod at once — one from a mod folder, one from Downloads — and placing them
> needs the comparer. Without it Phase 1 can only append, so an out-of-order import writes a
> wrong ordering from the first day and Phase 7 inherits bad data to repair. The comparer, the
> partial-order sort and the arbitration dialog should land with import; what genuinely remains
> here is backfilling existing rows, which is nothing while there is no real data.

A design change, deliberately sequenced late because it touches the domain and the sync engine
depends on stable ordering.

Today `ModVersion.SequenceNumber` is a curated position that a human sets, with `InsertVersion`
to back-fill an out-of-order upload. The intended model is that ordering **derives from the
mod's own version string**, compared by a comparer the game adapter supplies — `1.2.3.4` and
`v2-beta` do not compare the same way, and only the adapter knows which applies.

**Best-effort, with the user as the tie-breaker.** `modDesc/version` is free text and mod
authors write whatever they like in it, so a parser that insists on succeeding will silently
mis-order releases. The comparer should be confident or abstain — never guess.

- [ ] A shared `DefaultModVersionComparer` covering common notation: dotted numerics of any
      depth (`1`, `1.2`, `1.2.3.4`), an optional `v`/`V` prefix, zero-padded segments, and
      pre-release suffixes (`-beta`, `-rc1`, `b2`). Compare segment-wise and numerically, so
      `1.10 > 1.9`. Returns a **three-way result**: ordered, equal, or *cannot compare
      confidently*. Abstaining is a first-class outcome, not an exception.
- [ ] **Adapters can optionally override it.** A default interface member on `IGameAdapter`
      (`IModVersionComparer VersionComparer => DefaultModVersionComparer.Instance;`) so an
      adapter that says nothing gets the shared parser, and a game using dates or build numbers
      replaces it wholesale. An overriding adapter is still expected to abstain rather than
      guess.
- [ ] **Abstain on mixed notation.** `v1` versus `1.0` is the canonical case: they are probably
      the same release, or possibly adjacent ones, and nothing in the strings settles it.
      Likewise a date-like `2024.03` next to a semantic `1.4`. Guessing here produces a wrong
      order that nobody notices until a profile pins the wrong build.
- [ ] **Order a set as a partial order, not a sort.** `OrderBy` assumes a total order and
      misbehaves with an abstaining comparer. Build the partial order from pairwise comparisons
      over the union of registered and incoming versions, then topologically sort it — a few
      dozen versions makes all-pairs free. An abstention is only a *question* when nothing
      settles the pair transitively.
- [ ] **Resolve ambiguity in one batched dialog, before registering.** Collect every pair left
      genuinely unordered and ask once, showing each mod's version list in the order that was
      derived with the unplaceable ones floating and draggable. One dialog per import, not one
      per mod. Unambiguous mods proceed immediately and never wait on it.
- [ ] Cancelling that dialog **skips those mods and continues the import** — one unorderable mod
      is not a reason to lose a two-thousand-mod batch.
- [ ] Never register at a provisional position and fix it later: the newest version would be
      wrong in the interim, and a version appended past the real newest would advertise itself as
      an update and offer everyone a downgrade.
- [ ] Persist the resolution. Ordering is a **repo-level fact shared by every member**, so the
      answer is written to `SequenceNumber` server-side and nobody is asked again. Rows the
      comparer ordered on its own are written the same way.
- [ ] **Send the position with the registration; do not compare on the server.** The server has
      no adapters and cannot parse a version string — `AdapterData.Configuration` is opaque to
      it by design, which is what lets a new game ship without a server deployment. So
      `RegisterModRequest` grows a placement: append, or insert before a named version. The
      client computes it with its own adapter's comparer. The server validates and stores.
      Because ordering is stored rather than recomputed on read, clients on different adapter
      compatibility versions cannot disagree after the fact — the first writer settles it.
- [ ] **Keep `InsertVersion`** as the mechanism the resolution dialog writes through — it is
      exactly "put this version at this position", which is what arbitration produces. Earlier
      drafts of this plan proposed retiring it; it should stay.
- [ ] Backfill existing rows, routing whatever the comparer cannot order into the same dialog.

The division of labour: **the comparer proposes, `SequenceNumber` stores, the user arbitrates.**
That keeps automatic ordering for the overwhelming majority of mods without ever inventing an
answer for the ones where the version strings genuinely do not say.

> An earlier design pass argued against parsing version strings at all, for the free-form
> reason above. The abstain-and-ask design is what reconciles the two positions: it parses where
> parsing is safe and refuses to where it is not. Note also that "update available" on the
> import pages never needs this — a local version either has a server counterpart or it does
> not. See [09 — Mod catalog](09-mod-catalog.md#a-note-on-update-available).

## Phase 8 — Savegames

Only once mod sync is solid.

The adapter interfaces (`IBaseSavegameAdapter`, `IInstanceSavegameAdapter`) and the
`CanSupportSavegames` flag exist as empty placeholders. The shape to aim for: a savegame
belongs to a profile, is uploaded and downloaded through the same SAS mechanism as mods, and
carries enough metadata to tell whose copy is newer.

The hard part is not transport, it is conflict. Two people playing the same shared save on
different machines is a genuine last-writer-wins problem, and the design should probably start
by refusing it — one save, one owner at a time, an explicit hand-off — rather than attempting
a merge.

## Deliberately not planned

- **Dependency resolution between mods.** A profile is a pinned list, not a constraint
  system. If a mod needs another, someone adds it.
- **A web client.** `ModsDude.Client.Core` is UI-agnostic and could support one; there is no
  reason to build it.
- **Public sign-up and self-service repo creation.** `IsTrusted` stays a manual database flip.
  For a group this size that is the correct amount of machinery.
- **Multi-tenancy, quotas, abuse handling.** Not this project.
