# Sync

Sync makes a game's mod folders match its active profile, then keeps watching them. Everything here
runs on the client, in `ModsDude.Client.Core/Sync`. The rules for local file work in
[CLAUDE.md](../CLAUDE.md#correctness-and-stability) apply throughout; this page covers how they are
met.

## How it works

### Activate and apply

- **Activating** a profile records intent: the game's `ActiveProfile` in local state, written before
  any file moves.
- **Applying** does the work, once per mod target. A failed apply doesn't undo the activation. The
  folder then shows as drift until an apply succeeds.

A game follows the profile's head, unless something pinned a revision (a savegame in compatibility
mode, or following a friend who has one). Saving a profile applies it to every game here that follows
it, unless the user chose **Save only**. Deactivating clears the intent, and optionally removes every mod
it installed.

### The content store

Mod files are kept in a **content store**, one per disk, shared by every repo and game on that disk.
Game folders get hardlinks into the store where the target allows it, and copies otherwise.

- Layout: `{root}/blobs/{hash[0..2]}/{hash}`. Every lookup is by SHA-256, and every write is verified
  against the hash it is stored under.
- The store doesn't know what a file *is*. The server maps `(repo, mod, version)` to a hash, so two repos
  that disagree about a version ask for two different addresses, and neither can serve the other's
  bytes.
- Store locations and size limits are machine-wide settings (`ClientSettings`). The default limit is
  100 GB. A store on another disk than the game is allowed; it just means copies instead of links.
- A store folder is created only in an empty folder, with a marker file. The store never writes to or
  deletes from a folder holding anything of the user's.
- Last-use times are marker files under `used/`, never the blob's own timestamp. A blob linked into a
  mod folder *is* that folder's file, and touching it would disturb the manifest.
- Eviction removes the least recently used blobs, beyond the limit. Only blobs held nowhere else count
  towards the limit, because evicting a linked blob frees nothing.
- A store can be moved to another folder (`ContentStoreMove`).

### Hardlinks

A target allows hardlinks only if `ModTarget.SupportsHardlinks` is set, which means someone has tested
that nothing rewrites mod files in place there. Turning it off makes the next apply replace every linked
file with a copy (the `Unlink` action).

Blobs stay writable. Marking them read-only would block the harmless rename-over that in-game updaters
do on Windows. Rewrites are detected instead. `StoreIntegrityService` runs before every plan and on every
drift check. It compares each mod-folder file's size and time with the manifest. For a file that changed,
it checks whether the file is still the store blob itself, and if so, hashes the blob. A rewritten blob is
marked suspect in the manifest, evicted and downloaded again. Settings can also verify a whole store
(`ContentStoreMaintenance.VerifyAsync`).

### Planning

`ProfileApplyService` (in the app) drives an apply through `ModSyncService`. Each apply goes through
these steps, all before touching disk:

1. Take leases on the mod folders.
2. Refuse if a held savegame forbids this apply (see [Savegames](savegames.md#holding-a-save)).
3. Refuse if the game is running (`GameRunningGuard`, from the adapter's `ProcessNames`).
4. Recover crash leftovers in the folder (`ModFolderRecovery`): staging copies and half-done renames.
5. Check the store's integrity for that folder.
6. Build the plan (`ModSyncPlanBuilder`, `ModSyncPlanner`):
   - read the folder, using the manifest so only changed files are opened;
   - ask the adapter for the layout;
   - classify every file.
7. Check free disk space per volume, keeping a 1 GB reserve (`DiskSpaceGuard`).
8. Confirm:
   - The plan is shown for confirmation when the caller asks for it and there is work to do.
   - Separately, if any file the repo can't reproduce would be removed or replaced, a modal always asks
     whether it goes to the Recycle Bin or to a folder of the user's choice.

Each file gets one action:

| Action | When |
| --- | --- |
| `Keep` | Pinned, present, bytes match |
| `Rename` | As `Keep`, but under the wrong name |
| `Unlink` | As `Keep`, but hardlinked on a target that no longer allows it |
| `Install` | Pinned and absent |
| `Replace` | Pinned, present, wrong version or wrong bytes |
| `UninstallRecoverable` | Not pinned, and its bytes are registered in this repo or in the repo the folder was last applied from |
| `Quarantine` | Not pinned and not registered anywhere: the user's own file |

Files that aren't mods are left alone, unless one sits at a name an install needs. On a shared target,
only files ModsDude installed, or that sit at a name the layout is about to use, are considered at all.

### Executing

`ModSyncExecutor` runs the plan in this order:

1. Check the game isn't running, take a shared lease on the stores, and check the disk space again.
2. **Fetch** everything the plan needs into the store (`ModFetcher`): from any store on the machine
   first, otherwise downloaded. Large files download as parallel ranged requests, reassembled into one
   ordered stream so verification has a single path. If a store copy fails verification, the fetcher
   tries the next store, then the download. Fetching changes nothing in the game folder, so a failure or
   cancel here leaves the game exactly as it was.
3. Check the game still isn't running.
4. **Remove**:
   - A recoverable file still matching what the plan saw is deleted only if the serving store provably
     holds the bytes. Either the file is that store's blob under a second name, or it is moved into the
     store after re-hashing.
   - Everything else goes to the folder the user chose, otherwise to the Recycle Bin. If the bin can't
     take a file (deletes immediately, too small, a `subst` drive), it goes to the store's quarantine
     folder. If that fails too, the file stays and the item fails.
5. **Install**: hardlink straight to the final name, or copy under a temporary name and then move into
   place. An existing file is never overwritten.
6. **Apply managed file edits** from the layout.
7. **Write the manifest**: atomically, once per target, only on success.

### The manifest

`SyncManifest` (format v4; v3 is upgraded on read, anything older reads as missing) records, per
target:

- what the last apply installed: mod, version, hash, file name, size, time, lock;
- the managed files;
- the files left alone;
- which repo, profile and revision it was, the folder and when;
- whether the target is shared.

It lives in the client's state folder, never in the game folder. Between applies it changes only to mark
entries whose blobs turned out to be corrupt (`ContentSuspect`). The profile is the source of truth; the
manifest is only an optimisation and the drift baseline.

### Drift

`DriftService` compares each target with its manifest and the profile:

| Status | Meaning |
| --- | --- |
| `InSync` | Folder matches the last apply, and the profile hasn't moved |
| `Drifted` | Mods were added, removed or replaced, managed files were edited, or the profile has a newer revision |
| `NeverSynced` | An active profile but no manifest. Loud, because something is intended and nothing is recorded |
| `NotApplied` | The manifest describes another profile |
| `FolderRepointed` | The manifest describes another folder |
| `FolderUnreachable` | Unplugged drive or offline path. Unknown, not drifted |
| `NoActiveProfile` | Nothing to compare with |
| `DanglingProfile` | The profile's repo is gone or no longer reachable for this user |

The check is cheap:

- Known files are compared by size and time, and by whether they are marked suspect.
- Only files added since the last apply are opened, so that a non-mod file doesn't count as drift.
- Managed files are checked by re-running their edit and seeing whether anything would change.

`DriftMonitor` runs checks at startup, on window activation, on a timer, on resume and unlock, and
when a folder watcher fires. Watcher failures are logged and trigger a check. Checks don't overlap.

Drift is shown as a notice and, when the window isn't in front, as a Windows toast for critical and
warning drift, held back while a game is being played (see [Client](client.md#feedback-channels)). Nothing ever reverts drift automatically. The
notice offers to apply.

### Leases

`ResourceLeases` lets work claim resources, not pages. Each key is spelled once, in `ResourceKeys`:

- a mod folder (by target, not path);
- a store (by normalised path);
- a repo's mod set;
- a profile;
- a savegame and a savegame folder.

Applies, imports, saves, savegame operations and settings changes take the leases they need. A busy
folder, repo, profile or savegame is **refused, not queued**, and the UI says what holds it. Stores are
different: applies wait for a shared lease, maintenance waits for an exclusive one, and the eviction after
an apply is skipped while the store is busy.

## Decisions

**Content addressing is the security boundary.** All isolation between repos comes from looking up by
hash and verifying every write. One lookup by mod ID or one unverified write would break it.

**One store per disk, not per repo.** Hardlinks only work within a volume, and a mod shared by five repos
should be on disk once. Storing on another disk is the user's choice of space against time.

**Detect rewrites, don't prevent them.** Read-only blobs would break game updaters on Windows, and an ACL
approach was rejected as Windows-only and fragile on non-NTFS drives. Everything in the store can be
downloaded again, so catching a rewrite quickly costs about the same as preventing it.

**Fetch everything first.** The game folder is only touched once all bytes are local and verified. A
failed or cancelled apply then leaves the game as it was, not half-updated.

**Delete only what can be fetched again, and prove it.** Recoverability is a property of the bytes, not
the version ID. A copy in another disk's store proves nothing, because that store may evict it. So a
recoverable file is deleted only when the serving store holds it; otherwise it is moved into the store.

**The user's own files go to the Recycle Bin or a folder they choose.** ModsDude doesn't own them and
can't get them back. When the bin can't take a file, a quarantine folder does.

**The manifest is an optimisation, not the truth.** Losing it costs one re-check and a loud
`NeverSynced`, never a wrong folder. It is written only on success, so it always describes a complete
apply.

**An unreachable folder is unknown, not drifted.** Warning about mods on an unplugged drive would teach
people to ignore the warning.

**Drift is detected, never reverted.** The user may have added a mod on purpose. ModsDude says so and
offers to apply.

**Activate is intent, apply is work.** The intent survives a failed apply, so the failure shows as drift
and gets fixed, instead of being forgotten.

**Lock resources, not pages, and refuse rather than wait.** A page can be left while its work carries on.
Queuing would hide why nothing is happening; refusing names the holder. Store leases are the exception,
because store maintenance is short and invisible to the user.

**Never touch a running game.** A game holding its files open can corrupt them or its own state.
Checking at plan time and again just before writing catches a game started in between.
