# Mods

This page covers how the client sees mods, how they get into a repo, how versions are ordered, and how
a profile's mod list is edited. Getting them onto disk is covered in [Sync](sync.md).

## How it works

### The catalog

`ModCatalog` merges the repo's registered versions with the versions found in folder sources into one
flat list of `CatalogModVersion`, one row per version. The profile editor adds remote offers and other
profiles' pins on top (`ProfileEditorState`). A row records:

- whether the version is registered in the repo and whether it is present locally;
- the attributes, images, content hash, size and sequence number;
- usage (how many profiles pin it);
- deletion schedule;
- a remote download offer, if any;
- a likely-copy flag.

Grouping by mod is a query over the rows.

The registered half comes from the shared `IModStore`, which the [change poll](client.md#keeping-data-current)
keeps current. The local half comes from **sources**:

| Source kind | What | Loaded |
| --- | --- | --- |
| Game folders | The connected game's mod targets | Standing; scanned when switched on |
| Downloads | The user's Downloads folder | Standing; scanned when switched on |
| Remote | The adapter's remote sources, such as ModHub | Always loaded, on by default |
| Ad-hoc folders | Folders the user adds for the session | On request |
| Profiles | Another profile's pins, added as offered versions with their locks | Composed by the editor, no scan |
| Repo | The repo's registered versions | On by default, no scan |

Scans are cached as one task per source and composed on demand. They start 150 ms after a source is
switched on, so flicking through the chips doesn't scan everything. Arriving from a drift notice switches
on only the folder that drifted.

A source folder is never a sync target. The only files removed from sources are rejected copies the user
chose against in a conflict (outside game folders, to the Recycle Bin once the revision is written) and
downloaded remote files once imported.

### Identity and file names

A mod is identified by `ModKey`, a mod ID normalised at the adapter boundary, and a version by
`ModVersionKey`. A registered version keeps its `ModFileName`: the bare name the file gets in a game's
folder. That name is validated so that its stem is the same key. One member's file name becomes every
member's path, so it can't be allowed to carry anything else. If a file on disk has another name for the
same version, applying renames it instead of downloading it again.

### Importing and registering

New versions enter a repo only when a profile is **saved**. Dragging a local version into a profile
only marks it as "imports on save". On save, `ProfileSaveService` runs `ModImportService` for every
pending version, then writes the revision.

Importing first asks about any likely copies (`x (1)`, `x - Copy`). Then, for up to five mods at once,
and each mod's versions one after another:

1. Get an upload link. `FileAlreadyPresent` means a previous attempt uploaded it already, and the file is
   hashed to adopt it; `AlreadyRegistered` counts as done.
2. Upload in blocks, hashing (SHA-256) as it goes and recording the hash in blob metadata. The upload is
   retried per block, renews its link, and fails on a stall.
3. Register the version at its computed position.
4. Make the image renditions and upload any the server doesn't have.
5. Seed the local content store with the file, so applying doesn't download it again.

A version is never registered before its upload finishes. A repeated registration with the same hash and
name succeeds.

A version found in several sources is hashed in each. Identical copies are taken silently. If they
differ, the user picks one in `ModSourceConflictModal`, and a version left unanswered isn't imported. A
length difference already shows a warning chip in the list.

### Version order

Each mod's versions have a stored order (`SequenceNumber`). The client works it out and the server
stores it:

- The client compares the new versions with the adapter's comparer and builds a partial order. A
  topological sort turns it into one order, keeping abstentions as unknowns.
- If the comparer abstains, the user is asked once, in a single batched dialog, before those mods are
  registered. Mods that need no question carry on meanwhile. Cancelling skips the asked-about mods.
- Registration says "after A, before B". The server refuses if either neighbour isn't what the client
  saw (`VersionPlacementConflict`), and the client re-reads and recomputes.
- Members can move a version by hand on the repo's Mods page.

Clients never re-derive the order. Everyone reads the stored one, so batch actions such as "update all"
agree on every machine.

### Remote sources

An adapter can offer remote sources (`IRemoteModSourcesAdapter`). Farming Simulator's is ModHub, read
through the server's crawler. Offers are only made for mods already known. A remote version joins the
list only if it is **newer than at least one known version and older than none** (versions it can't be
compared with are allowed and stay unordered), as an offer that "downloads on save". If several sources
offer one, the first wins.

When the profile is saved, `RemoteModDownloader` downloads the offers, up to ten at once, into a folder
only ModsDude uses, then imports them like any local file. Every download is checked:

- the host is allowed, on every redirect hop;
- the Referer the source asks for is sent;
- the content type isn't text;
- the file starts with a zip signature;
- it is under 8 GB.

If a download turns out to be something other than what the source described, the user sees a mismatch
modal. A note is kept for that link, so a broken offer is marked as broken instead of being offered again.

### Mod imagery

Each version stores an icon and the images the adapter picks (store images, map previews or
screenshots) as **WebP renditions**: a 128 px thumbnail and
a full image of up to 1024 px. The client makes them when importing, because only the client can
decode the games' DDS textures. Images are content addressed by hash and shared across versions and
repos.

- A registered version always uses the server's renditions, even when the file is on this machine. An
  unregistered one uses images extracted from its archive.
- The server verifies hash and format on upload. The client verifies the hash again on read, because the
  image cache is shared and permanent.
- Setting a version's images replaces the whole set, so retries and backfills can't half-apply.
- Whoever holds a file whose images are missing on the server fills them in opportunistically.
- The client caches images on disk (512 MB by default) and decodes them lazily, with a throttle.

Measured on a real FS25 folder of 540 mods, store art was about 1% of the archive bytes, and the
renditions came to a median of 2.7 KB per thumbnail and 21 KB per full image.

### Locking

Some mods break savegames when their version changes, such as maps in Farming Simulator. A mod can be
locked in two places, and a pin counts as locked if either is set (`ProfileModLock`):

- **By the adapter.** `ModVersion.Locked`, set at registration from the file.
- **By the profile.** The pin's own lock, set by the user or carried over from another profile's pin.

Locked mods are not counted as updates. "Update all" skips them and says how many it skipped. A separate
opt-in modal (all boxes unchecked) updates chosen locked mods.

### Updates and the ignore list

A pinned mod has an update when a newer version exists (in the repo, in a folder being read, or online)
and the mod is not locked, not ignored, and the newer version isn't dismissed. Versions the comparer couldn't place are never
counted as updates. The profile's ignore list holds **ignored mods** and **dismissed versions**; it has
its own concurrency token and is saved with the profile.

### The mod list editor

The profile's Mods tab holds a `ProfileDraft`: the pins being edited, plus pending imports and
downloads, plus ignore list changes.

- Undo and redo go back 50 steps.
- The left list shows what the selected sources hold. The right list is the draft.
- Removed pins stay in the right list, struck through, until saved.
- Status chips show imports on save, downloads on save, likely copies and same-version conflicts. Filter
  chips include an updates count.
- It also offers paste a list, copy from another profile, revert, sort, and ignore or dismiss.
- **Save** saves and then applies the profile to every game on this machine that follows it. **Save
  only** skips applying.

Saving belongs to `ProfileSaveService`, not the page. It takes the profile lease, shows progress in the
strip, can be cancelled, and survives navigating away. A page with unsaved work or a save in flight is
kept alive while hidden, with an attention dot on its tab (see [Client](client.md#kept-alive-pages)).
A stale save asks "save mine anyway" or "load theirs". Both are safe, because history keeps the other.

### Deleting mods

The repo's Mods page lists every registered version, with usage and deletion schedule. Members can delete
one or many. A mod's only version can't be deleted on its own; deleting every version deletes the mod.
A single delete refused because revisions pin the version opens a list of those revisions. A bulk delete
lists what it had to keep.

## Decisions

**One flat catalog per version.** Files on disk and pins in a revision are both per version. A per-mod
model would need to be split back apart at both ends.

**Navigating never touches the disk.** Only the repo and remote sources start on. Folder sources start
off, are never remembered, and scan after a short delay. Opening a page with two thousand mods must not start reading
archives.

**Import on save, not on drag.** Cancel stays meaningful, and the repo doesn't fill with mods nobody
kept.

**Upload, then register, per mod.** At most one orphaned blob per mod in flight, and reclamation cleans
those up. A version that is registered always has its file.

**Order is computed by the client, stored by the server.** The server never parses version strings, so a
new game needs no server change. Asserting both neighbours turns a concurrent insert into a retry
instead of a silent misorder, which could offer a downgrade as an upgrade.

**Never register at a provisional position.** A temporarily wrong "newest" would offer people a downgrade
in the meantime.

**Remote offers must be updates.** An offer is only worth showing when it is newer than what is known.
Anything else is already in the repo or older.

**Remote downloads are checked like untrusted input.** The file comes from a third-party website, so
every hop, type, size and signature is checked before it is imported.

**Renditions, not originals.** Store art is cheap to store but expensive to use: a cold list of 540 rows
would pull tens of megabytes of DDS and decode most of it through a slow managed path. WebP renditions
cut that by about ten times and decode with the codec the app ships.

**Registered versions always use server imagery.** Finding and decoding the local file for each row is
the work renditions exist to avoid. Server images are immutable, so they cross the wire once per machine.

**The adapter sets the lock; there is no repo-wide override.** Behaviour must not depend on tags, so
`Locked` is a real column. Unlocking is a per-profile decision.

**Update all skips locked mods instead of prompting.** A question already settled turns into noise when
asked on every save. Locked updates have their own opt-in path.

**Saving is a service, not a page.** Pages can be closed mid-save. The service owns the lease, progress
and modals, so the save finishes and reports either way.
