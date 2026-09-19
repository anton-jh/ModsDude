# Mod sync design

**Status: implemented.** The exceptions are called out where they occur. The largest of them is
closed: Farming Simulator's in-game updater has been tested, it renames over mod files rather than
rewriting them in place, and
[hardlinking is switched on](#hardlink-support-is-an-adapter-property) for it. The narrower question
of read-only store blobs is closed too, and the answer was no:
[a rewritten blob is caught rather than prevented](#detecting-a-rewritten-blob).

Applying a profile to a game is the reason ModsDude exists. This document is the
design the implementation was built against, and it is kept as the reasoning rather than
rewritten into a description of the code — everything here is a decision, not a survey, and
where a decision has a real cost, the cost is stated.

## Goal

Given a game and a `Profile`, make every mod folder that game reaches contain exactly the mods
the profile pins, at exactly the pinned versions — quickly, repeatably, and without ever
destroying a file the user cannot get back.

The scale that shapes everything below: **1,000–2,000 mods in a profile, thousands of
versions registered per repo, and a game that may reach several mod folders.** A design that copies file
bytes on every profile switch is not viable; at ~40 MB average that is 40–80 GB per switch.

## Where each piece lives

| Piece | Where |
| --- | --- |
| `ModVersion.ContentHash` | On the entity, on `ModDto`, and on `ModDependencyDto` |
| `ModVersion.SizeBytes` | The same three places. Nullable - unknown, not zero - and what the plan confirmation adds up into "N mods to download, X MB" |
| Download link endpoint | `POST files/createModDownloadLink`, Guest level |
| Local content store | `Client.Core/Sync/ContentStore.cs`, one per volume via `ContentStoreProvider` |
| Reconciliation engine | `ModSyncPlanner` plans, `ModSyncService` executes |
| `ILocalModAdapter` write side | `ModTargets`, `GetModFilePath`, `GetInstalledModPath` — paths only |
| Upload half of import | `ModImportService` |
| Drift | `DriftService`, `DriftMonitor`, `SyncManifest`, `SyncManifestStore` |
| Rewritten-blob detection | `StoreIntegrityService` off the drift check, `ContentStore.VerifyAllAsync` on demand |
| Store housekeeping | `ContentStoreMaintenance` — sweep, verify, and what a store is costing |
| The UI | The activation bar on `ProfilePage`; the plan confirmation from `ProfileApplyService`; the background-task strip; the app-level drift notice; the folder lines on `RepoOverviewPage` |

## Content hashing

Every `ModVersion` carries a **SHA-256 of its file** as a first-class domain property, lowercase
hex.

Not a `ModAttribute`. Attributes are opaque adapter-supplied metadata the server stores and
never interprets; the content hash is a property the system itself depends on for
correctness and isolation. It belongs in the schema.

The client computes it while uploading — off the same buffer the upload blocks are cut from, so
the file is read once — and sends it with registration. **The server does not
verify it**, for the reason set out under [Cache isolation](#cache-isolation) below —
the guarantee comes from verification on the *download* side, not from trusting the
publisher.

### It has to reach the reconciler without a full mod list

The reconciler's *Desired* set is `(modId, versionId, contentHash)`, and it gets its input from
`GET repos/{id}/profiles/{id}/modDependencies`. Resolving hashes from the mod list instead would
mean walking `GET repos/{id}/mods` — every version in the repo — on every sync.

So **`ModDependencyDto` carries the content hash too**. It went in with the same schema change
that added `ContentHash`, which is what kept sync from shipping on top of a mod-list endpoint
that was at the time unpaged.

The rule is about the reconciler, not about the page. Planning still needs nothing but
dependencies; the plan *preview* renders each row with the same list row as the repo's mod list
and a profile's, and the icon, the description and the real display name only exist on the mod
list. So the page fetches it — after the plan, never as an input to it, and on a failure the
rows fall back to what the plan itself carries. What is being applied is decided without the mod
list either way.

### Hostile or wrong hashes have to be unregisterable, not just undownloadable

The server does not verify the hash it is given, and that is fine as long as the *only* way to
register one is to have uploaded the bytes it describes. The retry protocol in
[09 — Mod catalog](09-mod-catalog.md#retry-is-impossible-without-splitting-the-problem-type)
breaks that: on `FileAlreadyPresent` the client skips the upload and registers anyway, sending
the hash of **its** file against **whatever bytes are already in the blob**.

Where the orphan blob came from a different build carrying the same id and version — the case
[09](09-mod-catalog.md#same-mod-several-sources) identifies as common enough to surface in the
UI — the repo ends up with a registration nothing can ever satisfy. Verification then fails on
download for every member, permanently, and there is no repair path: the blob exists, so no
upload link can ever be minted for it again.

Adopting an orphan therefore has to establish what the blob actually contains. Either:

- the client re-downloads the orphan blob and hashes it before registering, which is correct but
  costs a download on a path taken precisely because the upload already happened; or
- the upload records the hash against the blob — Azure's `Content-MD5` is the wrong algorithm,
  so this means blob metadata written at upload time — and
  the link endpoint returns it, so the client can compare without transferring anything.

The second is what shipped. The upload link response names the metadata key the client must
write the SHA-256 into (`sha256`, sent as `x-ms-meta-sha256`), named in the response rather than
agreed by convention because the server is the only party that reads it back and a silent
mismatch would surface only as an unrepairable registration much later. `FileAlreadyPresent`
returns the hash of the blob that is already there. A client whose file hashes differently
knows it is looking at a genuine id/version collision rather than its own failed upload,
and says so instead of poisoning the registration; a `null` — a blob predating the metadata —
means nothing has been established and it must not register either.

## The content store

### Content addressing

The local store is content-addressed. Files are named by their hash, never by mod id:

```
{storeRoot}/
  blobs/{hash[0..2]}/{hash}
  tmp/...                      staging, so a torn write is never visible at an address
  quarantine/{timestamp}/...   only where the Recycle Bin is unavailable
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

Stores live **per volume**, not per repo, per game, or per adapter. A store is addressed
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
local settings, not on repo settings. Keeping it in one place is what stops the "same thing
configured in several places, then drifting" problem that scoping mod folders to repos created.
It holds, per volume that hosts mod folders:

| Setting | Default |
| --- | --- |
| Which store serves this disk | A store on this same disk |
| Store path | `{volume}/ModsDude/store` |
| Maximum size | User-set |

Volumes appear in the settings as mod folders are configured on them; there is no reason to
create a store on a drive with no mod folders. A store on `D:` may serve `D:` by hardlink and
`C:` by copy at the same time.

**An unconfigured volume gets those defaults rather than a refusal.** A store has to have a
ceiling before the first sync writes to it, and refusing to sync until somebody has visited a
page to accept a number would be a worse answer than starting from the one that page offers.

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
- N mod folders on that disk sharing a mod cost 1× disk.

There is no "force move" option. Its only purpose was reclaiming space on the same volume,
and hardlinks already make same-volume duplication cost nothing.

Two caveats the implementation must handle:

- **Only warn about copying when it was not chosen.** A cross-disk assignment is deliberate
  and should be left alone. A same-disk assignment that silently falls back to copying —
  exFAT, a network path, a filesystem without hardlink support — is a misconfiguration worth
  surfacing, because the user is paying the copy cost while believing they are not.
- **Both names are the same file.** Under hardlinking, anything that rewrites an archive in
  the mod folder rewrites the stored copy, which is now shared across repos. The safety is carried
  by `SupportsHardlinks`, which defaults to false and is set true only where somebody has tested
  that the game's updater renames over mod files rather than rewriting them
  ([the adapter property](#hardlink-support-is-an-adapter-property)). **Blobs are left writable**,
  and the residual exposure is answered by
  [catching a rewritten blob](#detecting-a-rewritten-blob) rather than by preventing one — read-only
  blobs would break the very rename-over the updater depends on. Copy-served disks are unaffected
  either way — the mod folder holds its own bytes.

### Ingestion

Four paths put bytes into a store, and only one is a download:

1. **Import.** A mod uploaded from a folder the game does not read — Downloads, an archive
   folder, anywhere the user keeps mods — is copied into the store serving the repo's mod folders
   as it goes. The user already has the bytes; fetching them back is absurd. It happens after the
   registration, verified against the hash that registration recorded, and it can never fail a
   version: a store that could not be written is a cold store, not a failed import.

   **A file already sitting in one of those mod folders is deliberately not copied.** Sync will
   find it there and keep it, so a store copy would duplicate the archive to save nothing — and
   across a 2,000-mod install that is tens of gigabytes of nothing. It reaches the store for free
   later, by path 2, on the uninstall that displaces it. Which store is chosen follows the same
   rule as everywhere else: the one serving the disk the file is already on where that is one of
   them, otherwise the store those folders are served by.
2. **Uninstall.** A registered mod being uninstalled is moved into the store — but only if
   **no store on the machine** already holds that hash. If any other disk's store has it, the
   bytes are already recoverable without a download and the mod folder's copy is simply
   deleted. Switching from profile A to profile B and back must not re-download A's mods, and
   must not duplicate them onto a second disk to avoid it.
3. **Cross-store copy.** A hash wanted on this disk that another disk's store already holds is
   copied across rather than downloaded. See *Install* below.
4. **Download**, for anything the first three did not supply.

The practical effect is that a member who imports their existing 2,000-mod install and then
applies a profile made of it downloads nothing: what was already in the mod folder stays there,
and what came from anywhere else is in the store by the time sync looks.

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

### Staying inside the limit is the app's job

**Eviction at the end of a sync only ever runs on the store that sync was using**, which leaves
three ways for a store to sit over its limit indefinitely:

- A disk that used to hold a game keeps its whole cache once the folder pointing at it is gone or
  repointed. Nothing sweeps it, because nothing syncs through it — and it is the store most likely
  to be holding tens of gigabytes nobody wants.
- An import seeds what it registered into the serving store. That is the other thing in the app
  that makes a store bigger, and unlike a sync there is nothing behind it to sweep.
- A limit lowered in settings does nothing until the next apply happens to that disk.

A limit somebody typed is a promise the app makes, so `ContentStoreMaintenance.SweepAllAsync` keeps
it, running on the three events that open those gaps: **startup**, **a finished import**, and
**settings being saved**. It skips a store something else is using, for the same reason the
end-of-sync sweep does — nobody is watching it, and the next trigger comes round.

There was a **Sweep to limit** button here, and it was the same work offered as a chore. A button
is a promise handed back to the user to keep, and one only kept by the users who notice the button.
It is gone.

### The user has to be able to take the space back

What automatic eviction cannot decide is that you want the space *now*, below the cap you set — a
hundred gigabytes agreed to in the abstract is a different thing from a hundred gigabytes on a disk
that is now full.

So settings reports what every store on this machine is holding — the ones serving a mod folder now
and the ones only the settings still name — and offers two actions per store: **verify integrity**,
which reads every blob back and drops what no longer matches its address; and **reclaim**, which
drops everything the store uniquely holds. Both are safe to hand to the user for the same reason
eviction is: the cost is bandwidth, never data. The image cache gets the same treatment.

**Reclaim is not "empty the store", and the difference is the whole point.** An entry hardlinked into
a live mod folder is one file under two names, so deleting the store's name frees not one byte — the
folder still holds the data. Dropping it buys a guaranteed re-download for nothing at all. So
reclaiming drops the uniquely-held entries and stops, which leaves behind exactly what the mod
folders on that disk are running. On a copy-served disk nothing is hardlinked, so every entry is
uniquely held and it does empty the store — correctly, because there every entry genuinely costs its
own bytes.

This used to be `Clear`, which deleted every blob and counted only the part that helped. A button
offering 7.6 GB back was quietly making the other 18.7 GB cold as well.

The button **carries the figure it frees** — "Reclaim 7.6 GB" — and because the operation stops at
the uniquely-held entries, that figure is now both what comes back and what goes. The row used to say
"26.3 GB in 942 files, 7.6 GB of it reclaimable" beside a button called "Empty store": a number with
no route to it, next to a button with no number, describing an action that did more than either said.
What is left beside the button is the only part it cannot say — why the two figures differ, which on
a hardlink-served disk is that the remainder is shared with an installed mod folder and already costs
nothing. On a copy-served disk they are equal and nothing is said.

The **quarantine folder is the exception and is handled as one**: it is the only part of a store
that nothing can fetch back, since a quarantined file is precisely a mod no repo registers. It is
reported separately, deleted separately, and its dialog is the only alarming one on the page.

## Reconciliation

Sync computes a plan first, shows it, and only then executes. The user must be able to see
what is about to happen to their files before it happens.

### Inputs

- **Desired**: the mod dependencies of the profile being activated — a set of
  `(modId, versionId, contentHash)`.
- **Actual**: the result of `ILocalModAdapter.GetInstalledMods` for the target being synced —
  `(modId, versionId)` with file paths.
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
| yes | same version, matching bytes | **Keep.** No I/O |
| yes | same version, different bytes | **Replace** |
| yes | different version | **Replace** — uninstall the installed one, install the wanted one |
| yes | absent | **Install** |
| no | installed, this exact version is registered in the repo | **Uninstall (recoverable)** |
| no | installed, not registered in the repo | **Uninstall (quarantine)** |

**"Matching bytes" comes from the manifest, not from hashing the folder.** `GetInstalledMods`
reports `(modId, versionId)` read out of the mod's own metadata, so two different builds that
both call themselves `1.0.0` are indistinguishable to it — and that is a case
[09](09-mod-catalog.md#same-mod-several-sources) says happens in practice. Content addressing
protects the *store*; it does nothing for the *mod folder* unless something compares.

The [sync manifest](#detecting-it-cheaply) already records each installed file's hash, size and
mtime, so the comparison is free in the common case: a file whose size and mtime match the
manifest is the file the manifest describes, and its recorded hash is the answer. Only a file
that fails that stat check needs rehashing. Where there is no manifest at all — a folder the
user populated themselves, or a first sync — every desired-and-installed file needs hashing
once, which is the honest cost of not knowing.

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

### Downloading

**One connection to blob storage is not enough to fill a fast line.** Measured outside the app,
a single GET against a mod's SAS link tops out around 100–120 Mb/s on a 1 Gb/s connection, and two
concurrent GETs of different blobs got ~80 Mb/s each — so the limit is per connection (RTT and
TCP window bound), not the line or an account cap. The client therefore fetches in parallel on
two levels:

**Within a file: parallel range GETs, reassembled in order before anything reads them.**
`HttpModFileDownloader` opens every download with `Range: bytes=0-{chunk-1}`. A file that fits in
that first answer — most of the catalog — costs exactly the one request it always did. Anything
larger learns its total size from `Content-Range` and fans the rest out as fixed-size chunks
over several connections, pinned to the first answer's ETag with `If-Match` so a blob replaced
mid-download fails loudly rather than splicing two versions together.

The chunks are handed back **as one ordinary stream, in file order**. That is the decision the
content store's safety rests on: `ContentStore.IngestAsync` stays the single path in, hashing the
bytes sequentially as it writes them, and nothing about verification knows chunking exists. The
alternatives were worse on exactly that axis — writing chunks at their offsets and hashing in a
second pass re-reads a 1.4 GB file from disk and adds a second code path through the store's one
check; hashing a contiguous prefix as it grows is the same reassembly with more moving parts.
The price is memory: a bounded window of chunks is held while the one at the front is still
arriving (about a hundred megabytes for one large file at the defaults).

- **Chunk failures are retried in isolation.** A dropped connection, a 5xx, a 408/429 or a
  connection that stops delivering bytes for a while re-requests that one range with backoff; the
  rest of the file carries on. Only a chunk that exhausts its retries — or a failure that is not
  transient, like a 403 or a failed `If-Match` — fails the download.
- **Progress stays one figure per file, and moves smoothly.** The store's own count - bytes
  hashed, in file order - would wait on the chunk at the front and then leap several chunks at
  once. So the downloader also reports bytes *received* across every chunk in flight, and the
  sync shows whichever of the two is further on. The UI still sees a single total and nothing of
  the chunking. The bar can reach the end a moment before verification does; a retried chunk
  holds it still rather than moving it back.
- **A server that ignores `Range`** (a 200 rather than a 206) is simply read as a single stream.
- **Known trade-off: the SAS lifetime.** Each chunk is a new request authorised by the link, and
  the link lives 30 minutes. A single GET started inside that window runs to completion; a ranged
  download slower than ~30 minutes would fail on a late chunk instead. At the speeds this exists
  for, a 1.4 GB file takes a couple of minutes; if it ever matters, the downloader needs a way to
  re-mint the link, which is a callback rather than a redesign.

**Across files: a few fetches at once.** A small mod is one request, so the per-file costs — the
link mint round trip, time to first byte, and a single connection's ceiling — dominate. Fetching
runs several items concurrently, and every range request from every file draws from one
process-wide connection budget, so a big file and a handful of small ones share the line rather
than multiplying connections. Disk-to-disk copies from another store stay one at a time, since
several at once on one spinning disk is slower than one.

**Speed limits are the user's, per direction, and off by default.** Settings → Network takes a
download and an upload limit in Mbit/s. Each is one token bucket (`TransferLimits`) shared by every
transfer in that direction — a limit is a promise about the line, so four downloads at once get
the limit between them, not each. Downloads pay for each socket read and uploads for each slice
of a block as it is written, 64 KB at a time, so the rate is smooth rather than bursty; a change
saved in settings applies to transfers already running from their next read. Under a limit a
download also opens fewer range connections — one per 256 KB/s — because the limit, not a single
connection's ceiling, is now the bottleneck, and storage gives up on a request that crawls. Tasks
on the strip that move bytes declare their direction, and the strip appends the limit holding
them back on a line of its own under the title ("Downloads capped at 50 Mbit/s"), re-read on
every redraw.

The numbers (chunk size, connections per file, the budget, files at once) are constants on
`RangedDownloadOptions` and `ModSyncService`, chosen from the two-stream measurement rather than a
sweep. `RangedDownloadBenchmark` in the test project runs against a real SAS link when
`MODSDUDE_BENCH_SAS` is set, for re-measuring them.

### Uninstall rules

This is the part that must never be got wrong, because it is the part that touches files the
user owns.

**A mod version registered in the repo is recoverable.** The bytes are in blob storage and can
be fetched again. If **no store on the machine** holds the hash, sync moves the file into the
serving store first; if any store already has it, the mod folder's copy is simply deleted.
Where it arrived by hardlink the move is already satisfied and the delete is genuinely free.

**Recoverability is a property of the bytes, not of the version id.** A file wearing a registered
version id while containing something else cannot be re-fetched, so it is treated as
unrecoverable — which is why the planner's *Registered* set is keyed by hash rather than by
`(modId, versionId)`.

**A mod version not registered in the repo is not recoverable.** It is a file the user put
there and nothing else has a copy. These are never deleted. They go to the **Windows Recycle
Bin**, so recovery uses a mechanism the user already understands and there is no
ModsDude-specific quarantine to manage, garbage-collect, or explain. Where the Recycle Bin is
unavailable — a drive with it disabled, a network path — it falls back to
`{storeRoot}/quarantine/{timestamp}/` and the UI says so.

One shell detail is load-bearing. `FOF_NOCONFIRMATION` alone lets the shell **permanently
destroy** a file it cannot recycle — most often because it is larger than the bin's quota, which
a mod archive easily is. `FOF_WANTNUKEWARNING` partially overrides it so the shell asks first;
declining aborts, which is reported as a failure and sends the file to quarantine instead.
Without that flag the exact outcome these rules exist to prevent happens silently.

**The user is warned either way.** Before executing a plan that uninstalls anything
unrecognised, show a dialog listing the affected mods by name, stating plainly where they are
going, and requiring explicit confirmation. Sync silently eating an unrecognised mod is the
one failure that would make the tool untrustworthy.

**The user chooses where they go.** The Recycle Bin is the default, and the dialog
(`UnrecognisedFilesModalViewModel`) offers a folder of their own instead - for somebody who does not
want to dig through the bin for a mod they mean to hand to a teammate. The answer is part of the
consent, so it is put on every folder's plan (`ModSyncPlan.QuarantineFolder`) after the plans were
made, and `ModSyncService.Quarantine` tries it first. A file already in that folder is never
overwritten (`mod (2).zip`, the way Explorer does it), and a folder that cannot be written to falls
back to the bin rather than leaving the file where it blocks the install: they asked for the file to be
kept, and the bin keeps it. The last folder chosen is remembered for the session, not persisted.

### Execution order

1. Populate the serving store with everything this profile needs that it lacks — from another
   disk's store where possible, by download otherwise. Nothing in the mod folder is touched
   yet, so a failure or cancellation here leaves the folder exactly as it was.
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
protects the save, and it is the easiest one to forget, because **nothing anywhere told the
user their mod folder had drifted.** It was a manual remedy for an invisible problem.

Reminding people harder is not the fix. Making the drift visible is.

> **What is built, and what is not.** All of this is implemented: `DriftService` answers
> from a directory listing against the manifest, `DriftMonitor` runs that check at startup
> and on window activation, and the surfacing described from [Surfacing it](#surfacing-it) through
> [When to check](#when-to-check) — the app-level notification, save-and-apply, activation from
> either end — is in the shell.
> [Hardlink support](#hardlink-support-is-an-adapter-property) is settled too: the updater was
> tested and Farming Simulator hardlinks, and the exposure that opens is answered by
> [catching a rewritten blob](#detecting-a-rewritten-blob) — which rides on this same check, off the
> files it already found changed.

### What sync records, and why it has to

A mod folder cannot tell you which profile it was supposed to be. Once the game has updated a
few mods, the contents match neither the old profile nor any other, and two different profiles
can pin identical mod sets anyway. **Drift is only meaningful against something recorded.**

Two things are stored, and they are not the same kind of thing:

| | What it is | If it is lost |
| --- | --- | --- |
| `ActiveProfile` on the game | The standing intent: *this folder follows that profile* | The intent is gone. Nothing can recover which profile it was |
| The sync manifest | A snapshot: what the last sync actually installed | Costs a full folder scan. Nothing is wrong |

`ActiveProfile` is a **source of truth** — underivable, so it must be persisted, which is why it
sits on the persisted game in `LocalState` rather than being inferred at runtime.

The manifest is an **optimisation only**. Reconciliation never needs it: it works from the actual
folder contents against the profile's dependencies, which is what a first sync does. The manifest
exists so the *check* can be cheap. Losing it degrades a stat-per-file into a rescan and nothing
else, so it never needs to be authoritative, backed up, or repaired.

Worth noting the manifest also catches drift from the other direction. It records the mod set
that was applied, so comparing it against the profile's current dependencies detects **someone
else having edited the shared profile** since this folder last synced.

The manifest also records **which revision of the profile was applied**, read out of the same
response the dependencies came from. It is nullable and did **not** bump
`SyncManifest.CurrentVersion`: a manifest written before revisions existed deserializes with it
null, which reads as "not recorded" — true, and harmless. The bump for `Locked` was needed because
the old data answered its question *wrongly*; this one answers it not at all, and discarding a
manifest costs a full rescan for nothing.

### A profile that moved on is drift too

`DriftReport.ProfileHasMoved` compares the revision the manifest records against the one
the profile is on now, and a difference is **drift in its own right** — even when the folder holds
exactly what was installed. That is the one kind of drift no directory listing could ever find:
the folder matches a list nobody is using any more. A save that changes nothing mints no revision,
so a moved number always means a different list.

The notice says it in those terms — *"this folder was made to match revision 6; the profile is now
at revision 8"* — rather than as a bare "something differs".

**Where the head comes from, and where it does not.** `IProfileRevisions` is answered by
`ProfileService` out of the profile list it has loaded, which is one repo at a time; for every
other repo it answers `null`, and null means "not asked" rather than "unchanged". That is
deliberate. Fetching it would put a network round trip per game into a check that runs on
every window activation and whose whole point is that it works offline and costs a directory
listing. The consequence is worth stating plainly: **this half of drift is reported for the repo
the user is standing in, and silently skipped for the rest.**

Two integers, rather than the mod-by-mod `ProfileChangedMods` comparison beside it, is what makes
it cheap enough to be free. Naming the changed mods needs the profile's current dependencies;
noticing that there are some needs nothing but the number.

Two states to handle rather than assume away:

- **A dangling `ActiveProfile`** — the profile was deleted, or the user was removed from the
  repo. The game should say so and offer to pick another, not fail silently or keep
  reporting drift against something unreachable.
- **`ActiveProfile` set but no manifest** — a fresh install, or discarded local state. Drift is
  simply unknown; fall back to a full reconcile, which produces the right answer anyway.

### Where the manifest lives

A **separate file per folder**, alongside `state.json` —
`manifests/{game-identity}_{target-key}.json` — not inline in `LocalState` and not in the game's
own folder.

Per folder rather than per game, because syncing the dedicated server must not rewrite what the
MP client is running, and because atomic-write-per-folder is what keeps a half-finished apply
safe. A manifest for a target the settings no longer produce is stale and is dropped — losing one
costs a rescan — and the sweep works from *what is still expected* rather than from what has
gone, which is what makes one pass collect a renamed key, an emptied field and the `.tmp` an
interrupted write left behind.

Not inline, because `state.json` is loaded eagerly and rewritten whenever a game changes; a
manifest for 2,000 mods with a hash each is a few hundred kilobytes that has no business being
re-serialised every time someone renames something.

Not in the mod folder, because writing bookkeeping into a directory the game owns and an in-game
updater rewrites is asking for it to be clobbered or to confuse something. It would survive the
loss of `LocalState`, which is the one argument for it — but per the table above, losing the
manifest costs a scan, so that is not worth buying.

The name goes through `StoreFileName`, which is where a store encodes what it puts in a filename.
**Two adapter-authored strings land in it** — the identity's discriminator, which a scripted
adapter declares from inside its script, and the target key — and a rule adapter authors had to
obey would fail as a manifest that cannot be written, found at sync time on somebody else's
machine. Ordinary parts pass through and
pathological ones are percent-escaped, with a length cap that truncates and stamps rather than
collides. See [04 — Game adapters](04-game-adapters.md#targets).

### Nothing keeps the manifest in sync, and nothing should

The manifest is **frozen between syncs**. It is written when a sync completes and then never
touched until the next one. It does not follow the folder, and must not: drift *is* the
difference between the two, so a manifest that tracked the folder could never detect anything.

That makes changes while ModsDude is closed — the normal case, since people play the game with
it shut — the **intended detection path** rather than a hole in it:

```
sync completes        manifest written, folder matches it
ModsDude closes
game updates mods     folder changes, manifest does not
ModsDude opens        compare  ->  mismatch  ->  drift
```

Nothing has to be observing at the moment of the change. The manifest is what lets a comparison
made *later* still be meaningful, which is exactly what a folder alone cannot give you.

Two rules keep it trustworthy:

- **Write it only on success**, atomically via temp-file-and-rename. A sync that fails halfway
  leaves the previous manifest in place, so the next check reports drift — which is true, and
  re-applying fixes it. A partially-written manifest would instead claim a state that never
  existed.
- **An unreachable folder is unknown, not drifted.** An unplugged drive or an offline network
  path should say so quietly, not raise a warning about mods that may be perfectly fine.

And one that is easy to miss: **"nothing to do" is not "nothing to record."** Apply a profile to a
folder that already matches it and no file changes, but the manifest may still be out of date —
the case being a mod dropped in by hand and then imported and pinned, which is precisely the
journey the drift notice sends people on. The folder ends up right, the plan comes back with no
work, and the manifest still does not mention the file. `ModSyncService.RecordAlreadyMatchedAsync` is
what closes that: applying is the moment the user has said the folder is what they want, so it is
the moment the record catches up. Without it the notice reports an addition that its own re-apply
button can never clear, while the status line underneath says the folder already matches.

### Detecting it cheaply

Reconciliation already computes drift properly — desired versus actual, a plan computed and not
executed. The cost is opening every archive to read `modDesc`, which is far too slow to run on
every launch against a 2,000-mod folder.

**The expensive part is reading the archives, not listing the directory.** A single
non-recursive `Directory.EnumerateFiles` over 2,000 entries, taking name, size and modification
time, is milliseconds. So the cheap check is a directory listing compared against the manifest,
which catches all three cases that matter:

| Difference | Meaning |
| --- | --- |
| A name in the listing that is not in the manifest | A mod was added |
| A name in the manifest that is not in the listing | A mod was removed |
| Same name, different size or mtime | A mod was replaced or updated |

Only when the listing disagrees does anything open an archive, and then only for the files that
actually differ. The recorded hashes are not read at all on this path — they exist so an
uninstall knows which store blob a file corresponds to, and so a suspicious file can be
confirmed.

A `FileSystemWatcher` per mod folder can catch changes as they happen, so the app already
knows at next launch. Useful as an optimisation on top of the manifest, not as a replacement —
watchers miss events across sleep, and on network paths.

### Surfacing it

> Everything from here to
> [Hardlink support is an adapter property](#hardlink-support-is-an-adapter-property) is built.
> Drift is checked at startup and on window activation — throttled, since `Window.Activated`
> fires on every alt-tab — and surfaced in the shell rather than on one page.

Drift status belongs wherever the game appears: the app-level notice, and the repo and profile
overview pages. (It was also on the game's own page, until there stopped being one — see
[05](05-client.md#the-game-is-not-a-place). The repo's Overview is what inherited it, which is why
it says every status the monitor reports and not only `Drifted`.) *"3 mods differ from Season 4"* with a Re-apply action.

**Drift on a locked mod is the dangerous case and deserves different treatment.** An unlocked mod
sitting at the wrong version is untidy; a locked map at the wrong version is a corrupted save
waiting to happen. Say so specifically — *"Your map is at 1.4, the profile pins 1.2. Hosting
this save may damage it."* — rather than folding it into a count. `DriftReport.LockedDrift` carries
the mods and the reason each is named, and it gets **its own card** in the column, at Critical where
the folder's is at Warning: it is a different severity from the drift it arrived with, and stacking
it as a coloured line inside that card was the closest a single box could get to saying so.

One card per folder however many mods are in it, though — twenty locked mods are one update-all with
one remedy, and the names belong in the body rather than in twenty cards.

### Turning the chore into something useful

The mods that drifted are, by definition, versions the user now has on disk and the repo may not
have. That makes the drift notice double as an import prompt: *"The game updated 6 mods. Import
them to the repo?"* The same interruption that warns about a problem also offers the first step
of the flow the user was going to perform anyway.

**Never revert silently.** Auto-syncing on detection would undo updates the user deliberately
made in-game, which is its own bad surprise. Detect and offer. A per-game *keep this game
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
- **The editor opens already scanning the drifted folder.** Mod sources are off by default so
  that navigating never reads a disk, but this navigation is the user asking about one specific
  folder — the versions the game downloaded are in it. The folder's `ModTargetRef` rides through
  `GoToProfileModsAsync` to `ScanTarget`. It is the only pre-enabled source anywhere; see
  [09 — Mod catalog](09-mod-catalog.md#the-source-list).

Dismissal should last until the drift set changes or the app restarts — not forever. A dismissed
warning that never returns is a savegame silently at risk.

The notification lives in the shell, next to the modal slot `MainWindowViewModel` already owns,
but it is **not** a modal: it must not block the app, and the user has to be able to keep working
while it is up.

#### One card became a column

Everything above is still the requirement. What changed is that it is no longer *one* notification:
the shell draws a [column of them](05-client.md#the-notice-column), and drift contributes a card per
folder, a card per folder with locked mods in it, and a card per drifted savegame.

**The single card was doing list-rendering in prose.** It showed the first drifted game and
summarised the rest as *3 games have drifted*, with no way to reach the other two; it said the
locked half and the savegame half as lines inside the mod half's box; and it ended those lines with
*2 more locked mods are affected as well* and *2 more savegame problems here as well* — counts
standing in for items each of which had its own remedy.

**The arguments it made against being split are on the record and were overruled deliberately.** It
said that *two notices racing to say one each is how a warning becomes noise*, and that *a person
acts on one problem at a time, and a list of three would be read as a wall rather than as three
things*. Both are true of a corner with room for one card in it. They stop being true of a list that
sorts by what is at stake, keeps a game's folders together under one heading, opens only the first
two and the critical ones, and holds the tail behind a count. If the column ever reads as a wall,
the corner was right and this should go back.

**Dismissal became per notice**, which is the change that needed the split to be worth anything.
It used to be one signature over every drifted folder and every corrupt blob at once — because one
card has one button, and one button can only mean everything — so waving away a folder that gained
two stray mods also silenced a locked map in another game. `DriftMonitor` no longer holds it at all;
`DismissalLedger` does, keyed per notice and signed with what that notice says. What the monitor
kept is the same signature's other job: deciding whether a re-check changed anything, so an alt-tab
that re-listed the same folders does not redraw the column.

#### Everything that can change the answer re-asks it

Startup, the folder watcher and window activation are the three mechanisms that cover drift the
app did not cause. They are no use at all for drift it *did*: somebody who takes a mod out of the
active profile, saves without applying and alt-tabs straight back to the game has changed what the
notice would say, and nothing was watching. The check has to be driven by the facts changing.

So the four that are not a folder listing are events, wired once in `NoticeCenterViewModel`
rather than remembered at each call site:

| Event | Raised by | Because |
| --- | --- | --- |
| A profile's head revision moved | `ProfileService.ProfileUpdated` | Every folder built against the previous one is drifted from that moment — whether this client saved it or a refresh brought back a teammate's save |
| A game was repointed | `GameRepository.GameChanged` | A new mod folder, or a new active profile, makes every previous answer about it meaningless. `CollectionChanged` covers adds and removes; this covers the edits, which used to be silent |
| A savegame was taken, handed back or forgotten | `SavegameBindingStore.BindingsChanged` | What this machine holds is the other half of what the notice reports, and it changes without anything touching a mod folder |
| The mod list editor stopped suppressing | `NoticeCenterViewModel.Release` | It is the one page that can change the answer while being told not to say it, so the last computed result is precisely what must not be trusted there |

All of them run as `DriftCheckReason.Explicit`, so the five-second activation throttle never
swallows one: they are consequences of something the user just did, and the complaint they answer
is a notice that arrives one alt-tab too late.

#### The notice needs a repo, and says so when it has none

**The check deliberately runs without one.** `IDriftCandidateSource` is the persisted folders and
the persisted intent and nothing else, which is what lets it work offline and answer for a game no
loaded repo serves. So the drift is *found* with no adapter and no membership in hand.

Every action it leads to is the opposite. Re-applying needs the profile's mod list; reviewing needs
a page under a repo in the sidebar; even naming a folder needs the adapter, which is hydrated from
a repo's base settings. So the notice resolves a repo for the game before it words anything — the
one the active profile belongs to, or failing that any repo about the same game — and it has two
branches:

- **With a repo**, everything reads as it always has, and the folder names come from
  `TargetNames.Read`.
- **Without one**, the drift is real and this account can do nothing whatever about it. The notice
  says *that* instead: no buttons, and a sentence naming the cause. Two dead buttons over a count
  of changed files is what this replaces — see
  [08](08-known-issues.md#a-drift-notice-can-outlive-the-account-that-could-act-on-it).

**Two causes, one of them not worth alarming anybody about**, which is why `RepoRepository`
exposes `HasLoaded`. An empty repo list means "not asked yet" for the second or two between the
shell appearing and the first fetch landing, and "this account is in no repos" ever after; the
notice would otherwise accuse a perfectly healthy startup of having lost a membership. Before the
first read it says it is still checking and waits — `OnReposChanged` re-renders it. After one, it
says the game belongs to a repo this account is not in.

This is also the whole reason a folder's display name is **not** persisted beside its path. It was,
for one slice, so that this notice could say *in the 'MP client' folder* without an adapter — a
stale copy of derived data bought to improve a sentence in the one state where the sentence should
not have been about folders at all.

#### The savegame half is its own notice

`TargetDrift.IsDrifted` is true for a held savegame that has moved even when the mod folder is
exactly what was installed — `SavegameDriftRules` decides which of the three ways it has, and
`DriftReport.SavegameDrift` carries them — so the column can be raised entirely by the savegame
half, with nothing at all to say about mods.

It used to have to say so inside the mod half's card, as a caution-coloured line under a headline
about mod files; a game whose mod folder and profile were both empty produced *"no longer matches
the applied profile"* with no detail underneath, because every sentence the detail line could build
was about file counts and revisions and there were none. A savegame is now its own card, so that
problem cannot recur: there is no mod half to be empty.

**One card per savegame, not per kind.** The kinds are not exclusive — a save can have been played
here *and* taken over by somebody who checked in, which is the worst case and the one where saying
only half would be actively misleading — so every kind that applies is said on the one card, and the
headline is the first, which `SavegameDriftRules` makes unchecked-in play.

**Gathered across the game's folders.** A held save sits in one target's savegame folder, so the
entry carrying it need not be the entry whose mods drifted; the builder reads every entry of a game
before deciding what savegame cards it has.

**Its action is the saves list.** Check in, discard and stop tracking all live on the repo's saves
list beside the row the card names, so the card navigates there rather than growing its own copy of
a flow that has confirmations in it. The one exception is a past savegame whose folder has moved off
the revision it pins: that is the single savegame problem a re-apply actually fixes, and the card
offers it with the number it will install on the button.

### Saving changes re-applies by default

If the user does go to the mod list editor and folds some of the drift into the profile — new
mods they installed in-game, say — then **the obvious, default save action also re-applies the
profile afterwards.**

This is the step that matters. The user came back to ModsDude to update their profile; the
re-apply is what actually reverts the auto-updated locked map, and separating it into a second
deliberate action is precisely how it gets forgotten. Apply to the game that has this profile
active — and to every folder it reaches.

#### Which game does apply target?

**Derive it; do not ask.** A profile belongs to a repo, a repo is about one game, and a game is
keyed by which game it is — so the target of a re-apply is a lookup with a yes or a no in it, not
a set to select from. `ProfileApplyTarget.Find` is that lookup.

This matters because two different operations are easy to conflate:

| | Means | Target |
| --- | --- | --- |
| **Apply** | Make a game already on this profile match it again | Derived |
| **Activate** | Move a game onto a different profile | Derived too, now — and it is a decision rather than a target |

Every awkward option — a checklist beside the button, a dropdown, a pre-selected one — came from
treating either verb as though it needed a target chosen. Neither does. What *is* chosen is the
profile, which is why activation lives on a profile's page and asks nothing about where.

The drift case falls out for free: a drifted folder is *by definition* one that no
longer matches its own game's active profile, so it is already the derived answer.

What the save button says scales with that, and says nothing about a game in the common case:

| Games on this profile | Button | Extra UI |
| --- | --- | --- |
| 1 | *Save and apply* | **None.** The word "game" does not appear |
| 0 | *Save changes* | None, and no second action to shape |

That last row is the onboarding case: a profile just created that nothing is using yet. After
saving, offer activation as a **follow-up** rather than folding it into the save —
*"No game is using this profile. Use it on Farming Simulator?"* — dismissible. A button promising
an apply that would reach nowhere is a worse lie than a plain one.

**Folders that cannot be applied to right now** — a dedicated server mid-session, a folder
locked by a running game — are reported and left drifted rather than being something the user
pre-deselects. Failure is **per folder** by design: the dedicated server being locked must not
stop the client being put right. The drift notification then covers what did not get there, which
is the machinery that already exists for precisely this state.

#### Activating is intent; applying is work

They were one word and one button, and splitting them is what makes a failure safe.

| | Activate | Apply |
| --- | --- | --- |
| What it is | Intent | Work |
| Scope | The game — one profile | Each folder, independently |
| Sets | `Game.ActiveProfile` | Files on disk; that folder's manifest |
| Can fail | Only by **refusal** | Yes, per folder |
| On failure | Nothing is recorded | The intent stands, and the folder is drifted |

**Activating implies applying; applying never implies activating.** Activation runs the apply
immediately after, as one gesture. *Save and apply* in the mod list editor is pure apply: the
profile is already active on whatever game follows it.

**The order is the guarantee**, and it is four statements in one method: answer the refusals, ask
the one confirmation, record the intent, then do the work. So a failed apply does not retract the
activation — the game still means to follow that profile, the folder that did not get there is
drifted, and the app-level notice carries it. Two things stop an activation happening at all: a
held savegame refusing it, and the user declining the plan.

**One confirmation per gesture, across every folder.** A game reaching three of them is one
decision about one profile; asking three times would let somebody accept the client's plan and
decline the server's, which is an activation half-consented-to and a state nothing downstream
could describe.

**Label it for what it will do.** Where the game is already on this profile, the operation is
*Re-apply*. Where it is on a different profile, or none, it is *Activate* — and activation
re-syncs every mod folder, which means uninstalling whatever the previous profile put there. That
deserves the plan preview as its confirmation rather than a bare "are you sure": the reconciler
already computes exactly what would change, so show it, with a block per folder.

Put the control on the **profile shell** rather than one page, so it is present on
Overview, Mods, History and Manage alike — `ProfilePageViewModel` already owns that sub-navigation.

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

**The manifest comparison is the primary mechanism, at startup and on window activation.** It is
the only one that works in the common case, which is ModsDude being closed while the game runs —
a watcher observing nothing cannot report anything. Debounce the activation check, since
`Window.Activated` fires on every alt-tab and someone switching back and forth does not need a
directory listing each time.

`FileSystemWatcher` is a **latency optimisation on top of that**, for the narrower case where
ModsDude happens to be open while mods change: it puts the notification up immediately rather
than at the next activation. Worth having, but it decides nothing on its own, and the design
must not depend on having been running.

### Hardlink support is an adapter property

An in-game update rewriting a mod file is precisely what a shared content store is exposed to.
If the file in the mod folder is a hardlink to a store blob and the updater **rewrites it in
place**, it corrupts the blob — which is now shared with every other mod folder and repo on that
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

`_farming_simulator@1` declares **`true`**, and it is an opt-in of exactly that kind: the in-game
updater was watched against the real game, and it writes a new file and renames over the old one.
The directory entry is replaced, the hardlink breaks harmlessly, and the blob keeps the bytes every
other repo on the volume is relying on. The main game therefore materialises by hardlink wherever a
disk is served by its own store — seconds of directory operations for a 2,000-mod profile instead
of tens of gigabytes copied on every install and replace.

**The residual exposure, stated plainly.** While `SupportsHardlinks` was false, writable blobs cost
nothing — nothing was linked, so there was no shared file to write through to. Switching the flag on
removes that cover. The test answered what the updater does today, in the paths it was watched on;
it cannot answer for every update path in every future version. If one of them ever writes into an
existing mod file, it reaches a shared blob. [The next section](#detecting-a-rewritten-blob) is what
happens then.

One consequence for the store assignment UI: for an adapter without hardlink support, same-disk
and cross-disk both copy, so the choice becomes a plain speed-versus-space trade — a same-disk
copy is faster, a cross-disk store keeps the cache off the game's drive. Present it that way for
those adapters rather than implying a fast path that does not exist.

### Detecting a rewritten blob

The guard against the exposure above is **detection, not prevention**, and the reason is that
prevention was tried on paper and does not survive contact with Windows.

Making blobs read-only was the obvious move: an in-place rewrite would fail loudly instead of
corrupting silently. It fails for a reason that has nothing to do with the bytes. On NTFS the
read-only attribute lives in the file record, which every hardlink shares — so marking the blob
marks the mod folder's file, because they are one file. And on Windows the attribute does not only
block writing; it blocks `DeleteFile` and `MoveFileEx`'s replace. **Unlinking the name is exactly
the harmless thing the updater does**, so read-only blobs break rename-over — the one update path
the test confirmed works. It would also break `Evict` and `Clear`, silently, since both swallow a
failed delete.

An ACL denying `WriteData|AppendData` while leaving `DELETE` granted separates the two and is the
version that could work. It was not taken: it puts Windows-only security code into `ContentStore`,
which is otherwise pure `System.IO`; it degrades silently on non-NTFS volumes and on a store copied
between machines, whose SIDs no longer resolve; the file's owner can strip it anyway; and whether it
keeps the updater working depends on which replace API the game calls, which the test did not
capture. It would cost another session with the real game to find out.

So the store stays writable and answers the question afterwards instead. **Everything in a store is
registered in some repo and re-downloadable** — the same property eviction and *empty the store*
already lean on — so a corrupt blob found promptly costs a download, which is close enough to what
preventing it would have bought.

**The cheap discriminator.** Both an in-place rewrite and a rename-over leave the mod folder holding
different bytes than the manifest recorded, and drift already finds both. They differ in one fact:

| What the updater did | The mod folder's file | The blob |
| --- | --- | --- |
| Renamed a new file over the old one | A **new** file | Untouched, one name, still hashes to its address |
| Wrote through the existing file | **Is** the blob | Rewritten, no longer hashes to its address |

So the test is *whether the installed file is still the same file as the blob* — the volume serial
and file index from `GetFileInformationByHandle`, which `FileLinks.TryGetFileIdentity` exposes.
Two handle opens, no bytes read. A link count cannot answer it: a rename-over leaves the blob with a
perfectly ordinary count of one.

This matters because an in-game update-all changes hundreds of files, and hashing them would be tens
of gigabytes. Only a file that fails the identity comparison — normally none of them — is ever read.

**Then, and only then, the hash decides.** A blob that reaches that point is re-hashed against its
own address. Identity says something wrote through; the hash says whether it changed anything, and a
file whose timestamp moved but whose bytes did not keeps its place. Nothing is deleted on suspicion.

**A confirmed one is deleted.** Removing the store's name is the whole repair: under hardlinking the
mod folder's name for those same bytes survives, so the user keeps the file the game gave them, and
the address goes back to being re-downloadable. Leaving it would go on serving the wrong bytes to
every repo on the volume under an address they all trust.

`StoreIntegrityService` runs this off whatever the folder comparison found changed, and
`DriftMonitor` **accumulates** what it finds rather than recomputing it. That is deliberate
and is the one piece of state here that outlives the check that produced it: finding a corrupt blob
destroys the evidence, so a notice rebuilt from the latest check alone would drop the warning on the
next alt-tab. It is also the warning least worth dropping — it means the game's updater writes
through hardlinks, which is the assumption `SupportsHardlinks` is set on.

**What this does not do.** Corruption still happens; it is caught rather than prevented, and between
the rewrite and the next drift check the wrong bytes are installable. A copy-served disk has nothing
to detect and falls out of the check for free, by failing the identity comparison — which is also
what happens on a filesystem that cannot report identities at all.

### Verifying a whole store

The check above is narrow on purpose: it only ever looks at files a mod folder reported changed. That
is what makes it free enough to run on every window activation, and it is also its blind spot — a
blob damaged by a failing disk, or rewritten by some tool that never went near a mod folder, leaves
no trail for it to follow.

So **Verify store** in the settings does the exhaustive version. `ContentStore.VerifyAllAsync` reads
every blob and re-hashes it against its own address; anything that fails is dropped, on the same
reasoning as *Empty the store* — the bytes are provably not what the address names, and what should
have been there is registered in a repo and downloads again.

It is a **button, not a schedule.** A pass reads every byte in the store, which is tens of gigabytes
on a full one, so nothing runs it in the background and the command is asked about before it starts
— not because it is dangerous, but because a button that silently pins the page for six minutes is
one people press twice. It takes progress in both files and bytes, since one mod is 4 KB and the next
is 400 MB and either count alone misleads, and it is cancellable: a stopped pass is a partial pass,
never an undone one, so whatever it repaired before stopping stays repaired.

**Dropping the blob is not the whole repair, and the report has to say so.** Where the entry was
hardlinked into a mod folder, that folder still holds the same wrong bytes under the same name;
removing the store's copy fixes what would be *served next*, not what is installed now. Only
re-applying that profile replaces the file. A store cannot work out which folders those are — it
deliberately knows nothing about what a blob *is* — so `ContentStore.VerifyAllAsync` reports bare
addresses and `ContentStoreMaintenance.VerifyAsync` turns them into mod folders and profile names off
the manifests. Every served folder is consulted, not only the drifted ones: a folder whose file
still matches its manifest exactly is the *worst* case here, because it means nothing has noticed.

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
be fetched once per batch. Nothing has needed it yet.

`ModDependencyDto` carries `ContentHash`, per [above](#it-has-to-reach-the-reconciler-without-a-full-mod-list).

`CreateModUploadLink`'s `FileAlreadyPresent` response returns the existing blob's hash, per
[above](#hostile-or-wrong-hashes-have-to-be-unregisterable-not-just-undownloadable), which means
the upload path records it — blob metadata written at upload time, since Azure's built-in
content hash is MD5.

## Fitting it into the client

**`ILocalModAdapter` has a write side**, and it is deliberately only paths. Reading installed
mods is not enough; the adapter has to say where a mod file belongs and what it should be called,
because that is game knowledge:

```csharp
ModTargets ModTargets { get; }
string GetModFilePath(ModTarget target, ModKey modId, ModVersionKey versionId, ModFileName? fileName);
string GetInstalledModPath(LocalMod installed) => installed.FilePath;
```

There is no `InstallMod` taking a stream, and that is the point: the link-versus-copy decision
depends on the store assignment and the filesystem rather than on the game, so it belongs in a
shared service and not in each adapter. Adapters supply paths, the sync engine performs the
filesystem operations. `GetInstalledModPath` is separate because what is on disk is not
necessarily where this adapter version would put it.

**`ModSyncPlanner` and `ModSyncService` in `Client.Core`** split the cycle: the planner does no
I/O beyond hashing the few files whose stat no longer matches the manifest and changes nothing;
the service executes and reports per-mod progress. Cancellable, because 2,000 files is minutes
of work even on the fast path and a frozen progress bar is indistinguishable from a hang.

### Where applying is reached from

There is no page for it, and there used to be: **`SyncPage`**, under the game's own `GamePage`,
showed the plan, the unrecognised-files confirmation, drift status and live progress. Every part of
it had grown a second home, and the page itself was reachable only by opening a game page nobody
otherwise had reason to open.

| What the page did | Where it is |
| --- | --- |
| The plan, before anything moves | `ProfileApplyService.ConfirmPlanAsync` — the reconciler's own plan, a block per folder, in one dialog, headed by what it will download - a count and a size, from `PlannedDownloads`. Distinct by content across every folder, and net of what another disk's store already holds, since that is copied and not fetched. A size the repo does not have is said to be unknown rather than added as zero |
| The unrecognised-files confirmation | The same place, and asked even where the caller waived the first |
| Per-mod progress, and Stop | The background-task strip, which is what the apply reports to anyway |
| Drift status, and Re-check | The app-level notice, and the folder lines on the repo's Overview |
| Apply | **Activate** on the profile, *Save and apply* in the mod editor, *Re-apply* on the notice |

What was genuinely lost with it is the browsable preview: the plan as mod rows with icons, rather
than as counts. If that turns out to be wanted, the answer is a richer confirmation dialog — not a
page nobody navigates to.

### Leases

Applying is minutes long, reachable from six places, and **must not happen twice at once to one mod
folder**. Two applies interleaving their installs and uninstalls leaves that folder holding a mixture
of two profiles — a state neither of them describes, and one the user can launch the game in — and the
two runs also read the manifest minutes before writing it, so the second one's write silently
discards the first's.

The obvious fix is a modal over the page that started it, and the obvious fix is wrong. It guards the
user's attention, and what needs guarding is the folder: the drift notice's *Re-apply* goes through no
page at all, so a dialog over the page it did not come from protects nothing — while a mod folder
somebody is merely browsing is perfectly safe to browse. A modal would also have to be dismissed by
whatever finished last, which is the bookkeeping the background-task strip exists to remove.

So the lock is on the resource. `IResourceLeases` is one process-wide table of who is touching what,
and three kinds of thing are claimable:

| Resource | Key | Mode | Claimed by |
| --- | --- | --- | --- |
| One mod folder | `ModTargetRef` | Exclusive | `ProfileApplyService.RunAsync`, for the whole gesture |
| One repo's mod set | repo id | Exclusive | `ModImportCoordinator.RunAsync` |
| One content store | normalised root path | **Shared** to install and ingest, exclusive to delete | `ModSyncService.ExecuteAsync`; `ContentStoreMaintenance` |

Four things about the shape are load-bearing:

- **Keyed by name, not by object.** `ContentStoreProvider` builds a fresh `ContentStore` on every
  call — it is a handle over a directory, not a singleton — so a lock field inside it would guard a
  different instance each time. Store keys are normalised, because the same store is reached from
  settings on one route and off a mod folder's volume on the other. Every key is spelled in
  `ResourceKeys` and nowhere else.
- **The store is shared/exclusive, not a mutex.** Its additive half is already safe beside any number
  of syncs: content-addressed paths, hash verified before placement, temp-file-then-place. Only
  deletion racing installation is not, so only `Evict` and the clears take the exclusive side.
  Serialising the installs too would be a real cost for no gain.
- **Applies and imports refuse rather than queue**, because a person is holding the mouse. The
  buttons are greyed from `IsBusy`, the refusal names what is running, and the refusal is the guard —
  `IsBusy` is only a hint, already stale by the time a button is redrawn from it.
- **The end-of-sync store sweep is the one claim that skips rather than waits.** It runs after every
  apply, on a store other applies are likely reading; waiting would serialise every sync on the disk
  to avoid a race that costs one re-download. Everything in a store is re-downloadable, so a skipped
  sweep is just the next apply's sweep.

The settings page's store operations are the mirror image: they **do** wait, because somebody asked
for them and refusing on the grounds that a sync is running only sends them back to press the same
button. The cost is that they hold the store exclusively — they all delete, verify included, which
repairs as it reads — so an apply wanting that store queues behind them, and a verify pass is minutes.
That ordering is right, but it must not be silent, so all four announce themselves on the strip for
the whole of it, wait included. It is the only thing on screen that can be read from another page.

The claim is taken for the whole gesture, **including across the confirmation dialog**. A dialog
asking whether to take the previous profile's mods back out is part of the gesture, and another apply
starting on that folder while the question is on screen is exactly what wants preventing: the answer
would be about a plan that no longer describes the folder.

Three things deliberately go unclaimed:

- **The savegame list's *what would this change?* previews**, which call `TryPlanAsync` without a
  lease. They are previews; the apply that follows re-plans under the claim, and a folder being
  written underneath them surfaces as the `IOException` that path already treats as "not plannable
  right now".
- **The import's store seeding.** The worst a sweep racing it can do is delete what was just seeded,
  which costs exactly the download the seeding was saving — the same outcome every other failure on
  that path already has, since it is best-effort cache warming that never fails a version. Waiting for
  housekeeping before warming a cache is the more expensive half of the bargain.
- **The sync manifest store**, which needs no lease of its own: it locks internally and writes
  temp-then-move, so a torn manifest is not reachable, and one-apply-per-target is what serialises the
  read-modify-write around it.

### Interrupting the work

Two consequences of the strip outliving the page, both of which were missing:

- **Cancel lives on the strip.** The button that started a long job is on a page, and pages are
  rebuilt from scratch on every navigation — so the moment somebody uses the freedom the strip exists
  to give them, their Cancel is gone and the work is unstoppable. Work that can be stopped now hands
  `Begin` a way to stop it, and page-level Cancel buttons and the strip's are the same act on the same
  linked token source.
- **Closing the window asks.** Not a refusal: an interrupted apply leaves the previous manifest
  standing and the folder reported as drifted, which re-applying repairs, so it is the user's call —
  and their reason may be that something has hung. The question is asked of the leases rather than of
  the strip, because the leases are what is actually being written to.

## Things this design deliberately does not do

- **No dependency resolution.** A profile is a pinned list, not a constraint system. If a mod
  needs another mod, someone adds it to the profile.
- **No partial sync.** Sync makes the folder match the profile. There is no "install just
  these three".
- **No sync of anything but mods.** Savegames are a separate capability and a later phase.
- **No detection of a running game.** Worth adding — writing a mod folder while the game has
  it open will fail confusingly — but it is a nicety, not a blocker.
