# Mod sync design

**Status: designed, not implemented.**

Applying a profile to a game installation is the reason ModsDude exists, and it is the one
flow that does not work. This document is the design to build against. Everything here is a
decision, not a survey — where a decision has a real cost, the cost is stated.

## Goal

Given an instance and a `Profile`, make the instance's mod folder contain exactly the mods
the profile pins, at exactly the pinned versions — quickly, repeatably, and without ever
destroying a file the user cannot get back.

The scale that shapes everything below: **1,000–2,000 mods in a profile, thousands of
versions registered per repo, several instances per machine.** A design that copies file
bytes on every profile switch is not viable; at ~40 MB average that is 40–80 GB per switch.

## What is missing today

| Piece | State |
| --- | --- |
| `ModVersion.ContentHash` | **Missing.** Nothing identifies a mod file by its content |
| Download link endpoint | **Missing.** `IModStorageService` has `GetUploadLink` but no counterpart |
| Local content store | **Missing** |
| Reconciliation engine | **Missing** |
| `IInstanceModAdapter` write side | **Missing.** The adapter can read installed mods but cannot install or uninstall one |
| Upload half of import | `RepoModsImportPageViewModel.ImportAsync` is an empty `TODO` |

## Content hashing

Every `ModVersion` carries a **SHA-256 of its file** as a first-class domain property:

```csharp
public required ContentHash ContentHash { get; init; }

public readonly record struct ContentHash(string Value);   // lowercase hex
```

Not a `ModAttribute`. Attributes are opaque adapter-supplied metadata the server stores and
never interprets; the content hash is a property the system itself depends on for
correctness and isolation. It belongs in the schema.

The client computes it while uploading and sends it with registration. **The server does not
need to verify it**, for the reason set out under [Cache isolation](#cache-isolation) below —
the guarantee comes from verification on the *download* side, not from trusting the
publisher.

`ModVersionDto` must expose it, since sync is the consumer.

## The content store

### Content addressing

The local store is content-addressed. Files are named by their hash, never by mod id:

```
{storeRoot}/
  blobs/{hash[0..2]}/{hash}
  quarantine/{timestamp}/...
```

The two-character prefix keeps directory sizes sane; thousands of registered versions in one
flat directory is a filesystem hazard on Windows.

Nothing in the store records what a file *is*. The mapping from `(repoId, modId, versionId)`
to a hash lives on the server and is fetched with the profile. That indirection is the whole
security property.

### Cache isolation

A name-addressed cache shared between repos is exploitable. Repo B registers `modA/v1` with
hostile bytes; you sync B, and the cache holds `modA/v1.zip`; you sync A, sync asks for
`modA/v1`, gets a hit, and installs B's file into A's game.

Content addressing removes the attack rather than mitigating it. Repo A's row says `modA/v1`
is `H_A`; repo B's says `H_B`. Syncing A asks for `H_A`, misses, downloads from A's blob,
verifies, and stores at `H_A`. B's file is at a different address and is unreachable.

Four preconditions, and they are not optional:

1. **Every lookup is keyed by hash.** No code path may fall back to looking up
   `(modId, versionId)` in the store. One such path and the property is gone.
2. **SHA-256.** Collision resistance is doing real work — a second-preimage attack against
   the hash *is* the attack.
3. **Verify on ingest.** Hash the downloaded bytes, compare against the hash the server
   declared for that repo, and reject on mismatch before writing to the store.
4. **One repo cannot write another's rows.** Already true: `Mod` is keyed `(RepoId, ModId)`
   and every write is authorized against that repo.

Precondition 3 is what makes precondition-free trust in the publisher acceptable. A hostile
member of repo B who declares `H_A` while uploading different bytes only breaks their own
repo's mod — verification fails on download and nothing is stored. Landing content at address
`H_A` would require a second preimage of SHA-256.

Two repos that genuinely ship the same file share one entry. That is correct and is the
deduplication win: a popular mod present in three repos costs one copy.

Content addressing also disposes of the "same id and version, different bytes" case with no
special handling. Different bytes are a different address; there is nothing to separate.

### Where the store lives

Stores live **per volume**, not per repo, per instance, or per adapter. A store is addressed
by hash and holds no notion of what a file is for — a Farming Simulator archive and a BeamNG
archive are both just bytes at an address — so there is nothing a repo or an adapter would
contribute to the scoping.

What each volume gets is a **store assignment**: for the mod folders on this disk, which
store serves them. The user picks per disk, and the choice is a real trade-off between space
and time.

| Assignment | Materialising | Space on the mod folder's disk |
| --- | --- | --- |
| **Its own store** (default) | Hardlink | The whole cache — but the installed mods cost nothing on top of it |
| **A store on another disk** | Copy | Only the active profile |

The second option exists because the disk with the game on it is often not the disk with room
on it. Mods on a small `C:` with the store on a roomy `D:` means `C:` holds *only* what is
currently installed — the cache history, which is the part that grows, sits on `D:`. Where the
cache is 200 GB and the active profile 40 GB, that is 40 GB on `C:` instead of 200 GB.

What it costs is sync time. Every install and replace becomes a cross-volume copy instead of a
directory entry, which is the difference between a profile switch taking seconds and taking
tens of minutes. Unchanged mods are still free either way — a mod both profiles pin is
classified *Keep* and never touched.

Both assignments are legitimate. Copying is not a fallback to warn about; it is a choice the
user makes with the trade-off in front of them.

**Store configuration is machine-wide**, in a new global settings bag on `LocalState` — not on
instance settings, not on repo settings. Keeping it in one place is what stops the "same thing
configured in several places, then drifting" problem that scoping instances to repos created.
It holds, per volume that hosts mod folders:

| Setting | Default |
| --- | --- |
| Which store serves this disk | A store on this same disk |
| Store path | `{volume}/ModsDude/store` |
| Maximum size | User-set |

Volumes appear in the settings as instances are configured on them; there is no reason to
create a store on a drive with no mod folders. A store on `D:` may serve `D:` by hardlink and
`C:` by copy at the same time.

### Materialising into a mod folder

| Strategy | When | Cost |
| --- | --- | --- |
| **Hardlink** | The disk is served by its own store **and** the adapter declares `SupportsHardlinks` | Instant, zero extra bytes |
| **Copy** | The disk is served by a store elsewhere, the adapter does not support hardlinks, or the filesystem does not | One full file copy |

A hardlink is a second directory entry pointing at the same file data:

- Creating one costs nothing. Materialising a 2,000-mod profile is seconds of directory
  operations, not tens of gigabytes of I/O.
- Deleting the mod folder's name leaves the store's name, and the data, untouched. Uninstalling
  becomes free and safe.
- N instances on that disk sharing a mod cost 1× disk.

There is no "force move" option. Its only purpose was reclaiming space on the same volume,
and hardlinks already make same-volume duplication cost nothing.

Two caveats the implementation must handle:

- **Only warn about copying when it was not chosen.** A cross-disk assignment is deliberate
  and should be left alone. A same-disk assignment that silently falls back to copying —
  exFAT, a network path, a filesystem without hardlink support — is a misconfiguration worth
  surfacing, because the user is paying the copy cost while believing they are not.
- **Both names are the same file.** Under hardlinking, anything that rewrites an archive in
  the mod folder rewrites the stored copy, which is now shared across repos. Mark stored
  blobs read-only, and treat a size or mtime anomaly as grounds to re-verify. Mod archives
  are read-only in practice, so this is a guard rail rather than a live problem. Copy-served
  disks are unaffected — the mod folder holds its own bytes.

### Ingestion

Four paths put bytes into a store, and only one is a download:

1. **Import.** A mod uploaded from an instance's mod folder is placed into the store as it
   goes. The user already has the bytes; fetching them back is absurd.
2. **Uninstall.** A registered mod being uninstalled is moved into the store — but only if
   **no store on the machine** already holds that hash. If any other disk's store has it, the
   bytes are already recoverable without a download and the mod folder's copy is simply
   deleted. Switching from profile A to profile B and back must not re-download A's mods, and
   must not duplicate them onto a second disk to avoid it.
3. **Cross-store copy.** A hash wanted on this disk that another disk's store already holds is
   copied across rather than downloaded. See *Install* below.
4. **Download**, for anything the first three did not supply.

The practical effect is that a member who imports their existing 2,000-mod install ends up
with a fully warm store having downloaded nothing.

On the uninstall path, prefer checking whether some store already holds the expected hash over
hashing the file — one usually does, and rehashing 2,000 archives to discover that is minutes
of pointless I/O. On a hardlink-served disk the file *is* the store entry, so the move is a
no-op and the uninstall is a plain delete. On a copy-served disk the serving store has it
whenever the copy came from there. Only a user-added file that happens to be registered and is
in no store at all needs a genuine move, which across volumes is a copy plus a delete.

Where a move is needed, it goes into **the store serving that disk**, not necessarily a store
on that disk — for a self-served disk those are the same thing, and for a copy-served disk the
serving store is where future installs to it will look.

One consequence worth being deliberate about: declining to keep a copy leans on another
store's, and that copy is subject to *its* store's eviction policy. Nothing breaks if it later
disappears — the mod is registered, so it is re-downloadable — but "some store has it" means
"no download needed right now", not "guaranteed present forever". That is the correct trade:
paying a possible future download beats duplicating a mod onto a disk the user chose to keep
free.

### Store eviction and the size limit

Distinct from uninstalling: this is about dropping blobs from a store, not about removing mods
from a mod folder.

The store must not grow without bound. It is bounded by a configured maximum size per store,
with LRU eviction on last-used time.

Accounting has one subtlety worth getting right: **an entry hardlinked into a live mod folder
costs no additional bytes, and evicting it reclaims nothing.** Size accounting should count
only entries the store uniquely holds — link count of 1 — because those are the only ones
eviction can actually reclaim. The rule covers both assignments without a special case: on a
copy-served disk every store entry is a standalone file, so all of them count. Reading the
link count needs
`GetFileInformationByHandle` via interop; `FileInfo` does not expose it.

Eviction never needs to ask the user. Everything in the store is registered in some repo and
therefore re-downloadable — unlike the quarantine path below, which handles files that are
not. Exempt entries serving an active profile on any disk **this** store serves; evicting one
would not break the installation, since a hardlinked file survives losing its store name and a
copied one holds its own bytes, but it would guarantee a re-download on the next sync.

## Reconciliation

Sync computes a plan first, shows it, and only then executes. The user must be able to see
what is about to happen to their files before it happens.

### Inputs

- **Desired**: the mod dependencies of the profile being activated — a set of
  `(modId, versionId, contentHash)`.
- **Actual**: the result of `IInstanceModAdapter.GetInstalledMods` — `(modId, versionId)` with
  file paths.
- **Stored**: which hashes each store on the machine holds — the serving store first, but the
  others matter too, both for installing and for deciding whether an uninstall needs to keep
  anything.
- **Registered**: which `(modId, versionId)` pairs exist in the repo at all, needed to
  classify what is safe to delete.

**Scope is the profile being synced, not the repo.** Sync downloads what this profile needs
and nothing else. There is no prefetching of a repo's full mod set — at thousands of
registered versions that would be tens of gigabytes for content the user may never activate.

### Classification

| Desired | Installed | Action |
| --- | --- | --- |
| yes | same version | **Keep.** No I/O |
| yes | different version | **Replace** — uninstall the installed one, install the wanted one |
| yes | absent | **Install** |
| no | installed, this exact version is registered in the repo | **Uninstall (recoverable)** |
| no | installed, not registered in the repo | **Uninstall (quarantine)** |

### Installing

Look for the wanted hash in this order, stopping at the first hit:

1. **The store serving this disk.** Materialise it — a hardlink where the disk serves itself,
   a copy otherwise.
2. **Any other store on the machine.** Copy the blob into the serving store, then materialise
   from there. A disk-to-disk copy beats a download every time, and it leaves the blob local
   for the next install to this disk.
3. **Download**, verify against the hash the server declared, and store.

Step 2 is safe for the same reason cross-repo sharing is safe: every store is
content-addressed, so a blob at address `H` is by construction content that hashes to `H`, no
matter which disk it sits on. The lookup is still keyed by hash and nothing else. Hash the
bytes as they stream past during the copy — they are already passing through memory, so
verification is close to free and it catches a store entry that has rotted or been rewritten
through a hardlink.

### Uninstall rules

This is the part that must never be got wrong, because it is the part that touches files the
user owns.

**A mod version registered in the repo is recoverable.** The bytes are in blob storage and can
be fetched again. If **no store on the machine** holds the hash, sync moves the file into the
serving store first; if any store already has it, the mod folder's copy is simply deleted.
Where it arrived by hardlink the move is already satisfied and the delete is genuinely free.

**A mod version not registered in the repo is not recoverable.** It is a file the user put
there and nothing else has a copy. These are never deleted. They go to the **Windows Recycle
Bin**, so recovery uses a mechanism the user already understands and there is no
ModsDude-specific quarantine to manage, garbage-collect, or explain. Where the Recycle Bin is
unavailable — a drive with it disabled, a network path — fall back to
`{storeRoot}/quarantine/{timestamp}/` and say so in the UI.

**The user is warned either way.** Before executing a plan that uninstalls anything
unrecognised, show a dialog listing the affected mods by name, stating plainly where they are
going, and requiring explicit confirmation. Sync silently eating an unrecognised mod is the
one failure that would make the tool untrustworthy.

### Execution order

1. Populate the serving store with everything this profile needs that it lacks — from another
   disk's store where possible, by download otherwise. Nothing in the mod folder is touched
   yet, so a failure or cancellation here leaves the instance exactly as it was.
2. Uninstall, quarantining as classified above.
3. Install.

Front-loading step 1 means the destructive phase only ever runs against a store that already
holds everything the profile needs.

## Drift

The flow that motivates this: in Farming Simulator, mods are downloaded and updated **inside the
game**, with an update-all button. Afterwards the mod folder no longer matches the profile — and
if one of the bumped mods is a locked map, hosting that save can corrupt it.

The user's remedy is to come back to ModsDude, import the new versions, add whatever they want
to the profile, and re-apply so the locked mods revert. The re-apply is the step that actually
protects the save, and it is the easiest one to forget, because **nothing anywhere tells the
user their instance has drifted.** It is a manual remedy for an invisible problem.

Reminding people harder is not the fix. Making the drift visible is.

### Detecting it cheaply

Reconciliation already computes exactly this — desired versus actual. A drift check is a plan
computed and not executed. The cost is a folder scan, which is too expensive to run on every app
launch against a 2,000-mod folder.

So **write a sync manifest** when a sync completes: what was installed, with each file's hash,
size and modification time. Checking for drift is then a stat of each file against the manifest,
with a full rescan only when something has moved. That turns a minute of scanning into a
fraction of a second in the common case where nothing changed.

A `FileSystemWatcher` on instance folders can catch changes as they happen, so the app already
knows at next launch. Useful as an optimisation on top of the manifest, not as a replacement —
watchers miss events across sleep, and on network paths.

### Surfacing it

Drift status belongs wherever the instance appears: the instance row in the sidebar, and the
repo and profile overview pages, which are currently `ExamplePageViewModel` placeholders looking
for a purpose. *"3 mods differ from Season 4"* with a Re-apply action.

**Drift on a locked mod is the dangerous case and deserves different treatment.** An unlocked mod
sitting at the wrong version is untidy; a locked map at the wrong version is a corrupted save
waiting to happen. Say so specifically — *"Your map is at 1.4, the profile pins 1.2. Hosting
this save may damage it."* — rather than folding it into a count.

### Turning the chore into something useful

The mods that drifted are, by definition, versions the user now has on disk and the repo may not
have. That makes the drift notice double as an import prompt: *"The game updated 6 mods. Import
them to the repo?"* The same interruption that warns about a problem also offers the first step
of the flow the user was going to perform anyway.

**Never revert silently.** Auto-syncing on detection would undo updates the user deliberately
made in-game, which is its own bad surprise. Detect and offer. A per-instance *keep this instance
in sync* opt-in is reasonable for people who want it, but it cannot be the default.

### It has to be unmissable, everywhere

Launching the game *from* ModsDude does not solve this. The user launches the game themselves,
installs mods and runs update-all **from inside the game's own menus**, and then comes back. By
the time they return, the drift has already happened — there was never a moment where ModsDude
was in the path.

So the requirement is about what they see on returning:

- **A persistent, app-level notification**, visible from any view, not a banner tucked into one
  page. It says the installed mods no longer match the applied profile, and it stays until the
  user handles or dismisses it.
- **Suppressed in one place only**: the drifted profile's own mod list editor. Someone already
  looking at the thing does not need to be told about it.
- **Two actions**: go to that profile's mod list editor, or re-apply the profile directly.
  Re-applying without opening anything has to be one click, because most of the time there is
  nothing to change and the user just wants their locked versions back.

Dismissal should last until the drift set changes or the app restarts — not forever. A dismissed
warning that never returns is a savegame silently at risk.

The notification lives in the shell, next to the modal slot `MainWindowViewModel` already owns,
but it is **not** a modal: it must not block the app, and the user has to be able to keep working
while it is up.

### Saving changes re-applies by default

If the user does go to the mod list editor and folds some of the drift into the profile — new
mods they installed in-game, say — then **the obvious, default save action also re-applies the
profile afterwards.**

This is the step that matters. The user came back to ModsDude to update their profile; the
re-apply is what actually reverts the auto-updated locked map, and separating it into a second
deliberate action is precisely how it gets forgotten. Apply to whichever instances have this
profile active — usually one, occasionally more.

#### Which instances does apply target?

**Derive the set; do not ask.** An instance already carries its `ActiveProfile`, so the targets
of a re-apply are exactly the instances whose active profile is the one being saved. There is
nothing for the user to select.

This matters because two different operations are easy to conflate:

| | Means | Target |
| --- | --- | --- |
| **Re-apply** | Make instances already on this profile match it again | Derived |
| **Activate** | Move an instance onto a different profile | Chosen — and it belongs on the instance |

Every awkward option — a checklist beside the button, a dropdown of instances, a pre-selected
one — comes from treating re-apply as though it needed a target chosen. It does not. Activation
is the operation that involves a choice, and putting it on a profile's save button is what makes
the button feel like it needs a picker.

The drift case falls out for free: a drifted instance is *by definition* one whose folder no
longer matches its own active profile, so it is already in the derived set. There is nothing to
pre-select.

What the UI shows scales with the count, and shows nothing at all in the common case:

| Instances on this profile | Button | Extra UI |
| --- | --- | --- |
| 1 | *Save and apply* | **None.** The word "instance" does not appear |
| 2+ | *Save and apply to 3 instances* | A disclosure listing them — read-only, not a selector |
| 0 | *Save* | None, and no second action to shape |

That last row is the onboarding case: a profile just created that nothing is using yet. After
saving, offer activation as a **follow-up** rather than folding it into the save —
*"No instance is using this profile. Use it on Farming Simulator?"* — dismissible, and naming
the instance because here that genuinely is a choice.

Activation itself belongs on the instance's own page. `EditLocalInstancePage` is currently just
a name and settings form; it should become the instance's real page — name, settings, active
profile, drift status, Re-apply.

**Instances that cannot be applied to right now** — a dedicated server mid-session, a folder
locked by a running game — are reported and left drifted rather than being something the user
pre-deselects. The drift notification then covers them, which is the machinery that already
exists for precisely this state. A per-save checklist would be a worse answer to a problem that
is really "not now" rather than "not this one".

#### Activating a profile on an instance

Activation pairs one profile with one instance. It is reachable from both ends, and which end
you start from decides what you pick:

| From | Fixed | Chosen |
| --- | --- | --- |
| The instance's page | The instance | A profile, from the repos this instance's adapter serves |
| Any of the profile's pages | The profile | An instance, from those matching the repo's adapter |

Both are dropdowns, and both disappear when there is nothing to choose. With one instance for
the adapter — the common case for most games — the profile-side control is a plain button with
no dropdown at all.

The two sets are **not** symmetrical, which follows from instances being adapter-scoped:

- **From a profile**, the candidates are simply the repo's own instance list — the same one the
  sidebar shows under that repo. `RepoPageViewModel` builds both lists, so a profile and an
  instance visible together are compatible by construction. Nothing needs filtering or
  re-checking; a drag between two entries of that menu is always a valid pairing.
- **From an instance**, the candidates span **every repo the instance's adapter serves**, grouped
  by repo. An instance is shared across those repos and holds one active profile that may have
  come from any of them, so a dropdown limited to the repo you happened to navigate in through
  would be unable to display the instance's own current state — it would show a blank for a
  profile that is plainly active.

**Label it for what it will do.** Where the instance is already on this profile, the operation is
*Re-apply*. Where it is on a different profile, or none, it is *Activate* — and activation
re-syncs the mod folder, which means uninstalling whatever the previous profile put there. That
deserves the plan preview as its confirmation rather than a bare "are you sure": the reconciler
already computes exactly what would change, so show it.

Put the profile-side control on the **profile shell** rather than one page, so it is present on
Overview, Mods and Manage alike — `ProfilePageViewModel` already owns that sub-navigation.

One interaction to get right: the mod list editor has its own *Save and apply*. While it holds
unsaved changes, the shell-level control must not quietly apply the last-saved version behind
them. Disable it there and point at the save button, which is the way to apply pending edits.

#### Shaping the two actions

*Save and apply* is the primary button and costs **one click**. *Save only* must cost **at least
one more**, and must say what it means. Two workable shapes:

| | Save and apply | Save only | Notes |
| --- | --- | --- | --- |
| **Split button** | Click the button | Open the dropdown, choose *Save only* | 2 clicks. No state to leave behind |
| **Checkbox** | Click the button | Tick *Save only*, click the button | 2 clicks. The warning can sit permanently beside the checkbox |

**The split button is the better choice**, for one reason that is easy to miss: a checkbox is
*persistent visible state*, and the failure mode is a user ticking it once and leaving it ticked.
That silently converts a per-save decision into a standing mode, which is the opposite of what
this control is for. A dropdown cannot be left in a dangerous position.

If the checkbox shape is used anyway, it **must reset after every save** — never remembered
across saves, and certainly never persisted across sessions.

Either way the wording carries the consequence, not just a caution:

> **Save only** — saves the profile but leaves your installed mods untouched. Your locked mods
> stay at the versions the game updated them to. Only if you know exactly what you are doing.

And the variant only appears when the derived target set is non-empty, per the table above.

### When to check

`FileSystemWatcher` on the instance folders is the primary signal here rather than an
optimisation: it catches the change *while the game is running*, so the notification is already
there the instant the user alt-tabs back, with no scan on return at all.

Back it with a manifest check on window activation, since watchers miss events across sleep and
on network paths. That check is a stat per file, so it is affordable — but debounce it, because
`Window.Activated` fires on every alt-tab and a user switching back and forth does not need
2,000 stat calls each time.

### Hardlink support is an adapter property

An in-game update rewriting a mod file is precisely what a shared content store is exposed to.
If the file in the mod folder is a hardlink to a store blob and the updater **rewrites it in
place**, it corrupts the blob — which is now shared with every other instance and repo on that
volume. If the updater writes a new file and renames over the old one, the directory entry is
replaced, the link breaks harmlessly, and the store is untouched.

Which of those a game does is game knowledge, so it belongs on the adapter:

```csharp
bool SupportsHardlinks { get; }
```

**When it is false, mods are always copied — even when the store is on the same disk.** The
store assignment still decides *which* disk holds the cache; it no longer decides link versus
copy, because copy is the only safe option.

Default it to **false**. An adapter author who has not thought about this should get the safe
behaviour, because the failure mode is silent corruption of data shared across every repo on the
volume, discovered long afterwards. Setting it true is an opt-in that means *someone tested this
game's updater*.

That includes Farming Simulator: `_farming_simulator@1` declares `false` until somebody
verifies what the in-game updater actually does. That costs the main game its fast path for
now, which is the right way round — a slow sync is visible and recoverable, a corrupted store is
neither.

Marking store blobs read-only is a complementary guard: an in-place rewrite then fails loudly
rather than corrupting silently. It also stops the in-game updater working at all, which users
may not want, so it is a separate decision from `SupportsHardlinks` and best made after the same
testing.

One consequence for the store assignment UI: for an adapter without hardlink support, same-disk
and cross-disk both copy, so the choice becomes a plain speed-versus-space trade — a same-disk
copy is faster, a cross-disk store keeps the cache off the game's drive. Present it that way for
those adapters rather than implying a fast path that does not exist.

## Server support required

A download counterpart to the upload link, mirroring it exactly:

```
POST api/v1/files/createModDownloadLink
     { repoId, modId, versionId }  →  { link }
```

- Authorization: **Guest**. Reading mods is a Guest-level operation everywhere else, and a
  Guest who can see a profile must be able to apply it.
- A user-delegation SAS with `Read` permission on the single blob, same 30-minute lifetime as
  the upload link.
- Reject with `file-not-found` if the blob is absent.

A profile of 2,000 mods means 2,000 SAS mints on a cold store. If that proves slow, add a
batch form taking a list — the user-delegation key is fetched once per call today and could
be fetched once per batch.

## Fitting it into the client

**`IInstanceModAdapter` gains a write side.** Reading installed mods is not enough; the
adapter has to say where a mod file belongs and what it should be called, because that is
game knowledge:

```csharp
Task<string> GetModFilePath(string modId, string versionId);  // where it would live
Task InstallMod(LocalMod mod, Stream content, CancellationToken ct);
Task UninstallMod(string modId, CancellationToken ct);
```

The link-versus-copy decision belongs in a shared service, not in each adapter — adapters
supply paths, the sync engine performs the filesystem operations.

**A `ModSyncService` in `Client.Core`** owns the plan/execute cycle and reports progress. It
must be cancellable and report per-mod progress: 2,000 files is minutes of work even on the
fast path, and a frozen progress bar is indistinguishable from a hang.

**A sync page** showing the plan (install / replace / uninstall / quarantine counts), the
confirmation for anything unrecognised, and live progress.
`ModListItemViewModel.ModStatus` already has `New` / `UpdateAvailable` / `AlreadyInRepo` and
nothing sets it — it exists for exactly this.

## Things this design deliberately does not do

- **No dependency resolution.** A profile is a pinned list, not a constraint system. If a mod
  needs another mod, someone adds it to the profile.
- **No partial sync.** Sync makes the folder match the profile. There is no "install just
  these three".
- **No sync of anything but mods.** Savegames are a separate capability and a later phase.
- **No detection of a running game.** Worth adding — writing a mod folder while the game has
  it open will fail confusingly — but it is a nicety, not a blocker.
