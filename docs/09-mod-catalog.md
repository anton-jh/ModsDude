# Mod representation and the catalog

**Status: designed, not implemented.**

A mod can be on disk, in the repo, or both, and three different pages need to reason about
that. This document is the design for how it is represented and where the merging happens.

## One identity, two facts

The join key already exists and is exact. `LocalMod.Id` and `LocalMod.Version` — the archive
filename stem and `modDesc/version` — are literally what `RegisterModV1Endpoint` stores as
`ModId` and `ModVersionId`. There is no fuzzy matching to do.

So "local only / server only / both" is **not three kinds of mod**. It is one identity with
two independent facts:

```csharp
public bool IsLocal { get; }      // found in some instance's mod folder
public bool IsOnServer { get; }   // registered in the repo
```

Two bools rather than a flags enum, and rather than a three-case enum. The three-state value
is derived where a page needs it, never stored — storing it means two sources of truth for the
same question. Two bools also bind straight through the existing `BoolToVisibilityConverter`,
which the XAML already uses in ten places; a flags enum would need a new mask-taking converter
to express what a bool expresses for free.

**Presence belongs on the version, not the mod.** A mod is "has something to import" precisely
when it has a local version with no server counterpart, which is a per-version question. The
profile editor needs per-version answers throughout.

### The casing trap

`Directory.EnumerateFiles` returns whatever casing the file happens to have. Windows does not
care; **Azure blob names do**, and `ModStorageService.BuildModFilename` interpolates `ModId`
straight into the path:

```csharp
return $"{repoId.Value}/{modId.Value}/{versionId.Value}";
```

`FS22_MyMod` and `FS22_mymod` are the same file to the user and two different mods to the
system. Normalize once, at the adapter boundary, and carry the result in a key type rather
than `(string, string)` tuples so no code path can bypass the normalization.

## A merged model

`ModListItemViewModel` currently takes a `LocalMod` and delegates `Id`/`Name`/`Version`/
`Author` to it. That is the thing that will hurt: the profile editor's available-mods list is
a *mixed* set that has to sort, filter and select uniformly, and two row types force
`IEnumerable<object>` plus duplicated templates.

One core model, in `Client.Core`:

```csharp
public record CatalogMod(ModKey Id, IReadOnlyList<CatalogModVersion> Versions);

public record CatalogModVersion(
    ModKey ModId, ModVersionKey VersionId, string Name, string Description,
    bool IsLocal, bool IsOnServer)
{
    public string? Author { get; init; }
    public ModImage? Icon { get; init; }
    public Func<Stream>? OpenStream { get; init; }   // null for server-only
    public IReadOnlyList<ModSource> FoundIn { get; init; } = [];
}
```

The lazy-image design generalizes for free. `LocalModImage` is
`(Name, CacheKey, Func<CancellationToken, Task<byte[]>>)` and says nothing about zip archives —
a server-backed version hands back the same record with an HTTP fetch in `Load`, and
`IModImageProvider` keeps working untouched. Rename it `ModImage`.

Two renames worth doing at the same time: the client-side `Mod` that wraps `ModDto` reads
confusingly generic next to `LocalMod` — call it `RepoMod`. And `LocalMod` is fine as adapter
output, because it genuinely is "what was found on disk".

`ModStatus` should be split. It currently mixes fact (`AlreadyInRepo`) with context-dependent
judgment (`New`, `UpdateAvailable`), and "New" means different things on the import page than
in the profile editor. Facts live in the two bools; display status is computed per context.

### What the server cannot supply yet

- **No icons.** There is no download endpoint at all, so a server-only row falls back to
  `Initials` forever — in exactly the list where the user is choosing between a local row and a
  server row of the same mod. Store a downscaled icon per version at registration: the client
  has the bytes in hand during import and already decodes to 64px.
- **No description.** `ModVersionDto` carries `Description`, but the client's `Mod.Version`
  drops it on the floor, so a details modal on a server-only mod would be blank.
- **No usage information.** See [Manage](#manage) below.

## Mod sources

A mod does not only arrive via an instance's mod folder. It is at least as common for it to be
sitting in Downloads, freshly fetched from wherever the group gets mods. The import surfaces —
Manage and the profile editor — scan a **set of sources**, not a fixed folder.

### Standing sources

Present automatically, without the user configuring anything:

| Source | Where |
| --- | --- |
| Each instance's mod folder | From the instance settings, via `IInstanceModAdapter.GetInstalledMods` |
| The system Downloads folder | Once per machine, not per instance |

Downloads needs care to locate. .NET has no `SpecialFolder.Downloads`; the correct route on
Windows is `SHGetKnownFolderPath` with `FOLDERID_Downloads`, because the user may have
relocated it. Falling back to `%USERPROFILE%\Downloads` when that fails is fine, but do not
*start* there — a relocated Downloads is common and the fallback path will simply not exist.

### Ad-hoc sources

The user can add a folder with the system folder browser — `IDialogService.PickFolder` already
exists — and it appears alongside the standing ones. Ad-hoc sources are **view-scoped**: they
live as long as the page does and are not persisted. Someone importing from a USB stick or an
extracted archive should not have that folder haunting the UI for months.

### The source list

Every currently available source is listed, each with an enable/disable checkbox. Disabling one
removes its mods from the merged list without removing the source, so a user can narrow to "just
what is in Downloads" without losing their instance sources.

**Disabling a standing source persists.** Someone who never wants Downloads scanned should not
have to say so every session. Ad-hoc sources remain view-scoped, as above — there is nothing to
persist about a folder that stops existing when the page closes.

The disabled set lives **machine-wide**, in `LocalState.Settings`, keyed by source id — the
instance id for an instance folder, a well-known constant for Downloads. Not per repo: "do not
look for mods in this folder" is a fact about the folder, not about which repo happens to be
open, and an instance is already shared across every repo using its adapter. This is the same
reasoning that keeps the content store's settings machine-wide — one place to configure a
thing, so there is nothing to drift.

Disabling an instance as a *source* has no effect on syncing to it. The two roles are
independent; see below.

### Scanning arbitrary folders already works

`IBaseModAdapter.GetModsFromFolder(path, ct)` takes any path, and the instance variant is a
thin wrapper that supplies the game's own folder. Non-mods are already handled: `GetZip`
returns `null` on `InvalidDataException`, and a zip with no `modDesc.xml` yields `None`. A
Downloads folder full of installers and PDFs scans cleanly.

The cost is lower than it looks, too. Rejecting a non-mod archive reads the zip central
directory and does one dictionary lookup for `modDesc.xml` — it does not decompress anything.
The existing `ProcessorCount` cap and cancellation apply unchanged.

Scanning is **not recursive**, matching `Directory.EnumerateFiles`. That is right for
Downloads and for a game's mod folder. Whether an ad-hoc source should offer recursion is an
open question; leaving it flat is the safer default.

### Sources are not sync targets

Worth stating plainly, because the two look similar and are not: a **source** is somewhere to
*find* mods to import. A **sync target** is a mod folder that sync will make match a profile,
which means uninstalling things from it. An instance's mod folder is both. Downloads and ad-hoc
folders are **only ever sources** — nothing in sync will ever delete, move, or quarantine a file
in them.

### Same mod, several sources

Deduplication is on `(ModId, VersionId)`, and `FoundIn` records every source a version turned up
in, so a row can say where it came from. With a single enabled source, naming it on every row is
noise; show it once more than one is active.

Once `ContentHash` exists, a sharper case becomes detectable: **two files claiming the same mod
id and version but hashing differently** — typically a re-uploaded build the author did not
renumber. Since only one can be registered, surface the conflict on the row and let the user
choose the source rather than picking silently. Without hashing this is invisible and whichever
source is scanned first wins.

## The `ModCatalog` service

`RepoModsImportPageViewModel.InitAsync` currently does the folder scan, the dedupe and the
instance-source dictionary inline. The profile editor needs all three. Extract a repo-scoped
`ModCatalog` into `Client.Core/Services`, merging the source scans with `GET repos/{id}/mods`.

- **Cache per source, and compose on demand.** Not one cached catalog: a
  `Task<SourceScan>` per source, with the merged view built from the enabled ones. This is what
  makes the checkbox usable — toggling a source recomposes from memory and is instant, and
  adding one scans only the new folder rather than every folder again.
- **Cache the `Task`, not the result.** A second caller arriving during an in-flight scan joins
  it rather than starting a second `Parallel.For` over a thousand archives.
- **Invalidate explicitly** — on import, and on instance-settings change. Never silently. A
  stale catalog that quietly refreshes mid-interaction is worse than one the user re-triggers,
  so expose a Rescan action, per source and for all.
- **Move the 150 ms scan delay and the cancellation behaviour into the service**, unchanged.
  They exist so that dragging down the sidebar never touches the disk; that reasoning is not
  specific to the import page.
- **Report per-source progress and failure.** A source can vanish or be unreadable — an
  unplugged drive, a folder the user deleted. That should mark one source as failed in the
  list, not fail the whole catalog.

## Pages

### Manage

**Merge Import into Manage.** They are currently sibling menu items in `RepoModsPageViewModel`
showing overlapping data under different rules, which is the main thing that will confuse. One
list, presence filter chips (All / In repo / On disk only / Unused), and bulk import becomes a
*selection mode* that reveals the footer bar the import page already has. Same rows, same
templates, one service.

The page carries the source list described above — instance folders, Downloads, anything the
user adds for the session — each with its checkbox, so the set of local candidates is
adjustable in place rather than being a fixed consequence of the repo's instances.

Two things this needs that do not exist:

- **"Unused" cannot be computed client-side safely.** `GetModsV1Endpoint` returns mods with no
  usage information, and profile dependencies arrive one profile at a time. Deleting on a
  partial client view risks removing a version a teammate's profile just picked up. Add usage
  to `ModDto`, or a dedicated endpoint — the server-side join is cheap.
- **There is no delete endpoint**, and `Mod.RemoveVersion` refuses the last version, so
  "remove whole mod" needs its own path rather than a loop of version deletes.

### Profile mod list editor

Two lists: available on the left, in-profile on the right. The left is the union of registered
mods and local candidates, so a mod can be added to a profile *and imported* in one action
without a detour to another page. It carries the same source list and checkboxes as Manage —
adding a mod straight from Downloads while building a profile is the point of having sources at
all.

**Updates belong on the right, not the left.** If a mod is already in the profile, also showing
it on the left as "update available" puts the same mod on both sides at once. Render it as an
update affordance on the right-hand row, plus a "N updates available" batch action in the
header. The left list stays a clean "not in the profile" set.

The right list is keyed by `ModId` — the domain enforces one `ModDependency` per mod — so
moving a mod rightward also means choosing a version. That row needs a version selector and a
`Locked` toggle.

## Version locking

`Locked` exists to stop version-sensitive mods being bumped by accident. The motivating
case is a Farming Simulator map: changing map versions partway through a save can
corrupt it, and the damage shows up long after the change that caused it.

### The adapter sets it once, at registration

An adapter can tell that a mod is version-sensitive — a Farming Simulator map mod declares its
maps in `modDesc`, so the adapter can spot one while it is already parsing that file.

It sets **`Mod.Locked`**, a real domain property, when a **completely new mod** is registered.
Not per new version: a mod that is version-sensitive stays so, and re-deciding on every upload
would let a later import quietly overturn a user's choice. There is no prompt — the adapter's
answer is the starting value, and the user changes it afterwards if they disagree.

Two properties, two scopes, described in full in
[02 — Domain model](02-domain-model.md#locking-in-two-places): `Mod.Locked` is repo-wide,
`ModDependency.Locked` is per profile, and a mod counts as locked in a profile's list when
either is true. The adapter can only ever set the first.

**This is not a `ModAttribute`.** Attributes are tags and categories; the system must never
depend on one for its behaviour. `Locked` changes what the software *does* — which mods a batch
update is allowed to touch — so it is a property with a column, exactly like `ContentHash`.
Being a real property also means a **server-only** mod carries its lock state, which matters
because the client has no file to inspect for a mod it has never had locally.

### Batch updates skip locked mods entirely

The obvious design is for "apply all updates" to sweep everything and then prompt about the
locked ones at save. That produces a modal listing the same locked mods every single time,
asking a question the user already answered when they locked them. Re-asking a settled question
is what makes a safety prompt into noise — and a prompt people have learned to dismiss protects
nobody.

So: **"apply all updates" applies updates to unlocked mods only** — unlocked meaning neither
`Mod.Locked` nor `ModDependency.Locked`. Locked mods are not candidates, and the action reports
what it skipped: *"Update 47 mods · 3 locked, skipped"*. The save that follows cannot contain an
unintended version change, so it needs no prompt at all.

Changing a locked mod's version is then a deliberate act on that specific row, with its own
confirmation. For the case where someone genuinely does want to move locked mods in bulk, the
skipped-count is a link to a modal listing them with an **unchecked checkbox each** and the
consequence spelled out per mod. Same dialog as the original design — reached deliberately,
rather than as the standing cost of the common action.

Both toggles are editable from the profile mod list: the row shows the effective state and which
level it came from, since "locked because this mod is locked repo-wide" and "locked because I
locked it here" are different situations with different fixes.

The distinction worth holding onto: a prompt should mark a decision the user is *making*, never
one they already made.

## Import-on-save

When a local-only mod moves right, mark it **pending**; do not upload. Uploading immediately
makes Cancel meaningless and litters the repo with mods nobody kept.

Save then runs: upload each pending file, register it, and update the profile's dependencies
**last, in a single request**. Partial failure during the upload phase leaves orphaned blobs,
which the protocol below reclaims — but the profile must move atomically or not at all.

### The invariant

**A mod is never registered before its file is in blob storage.** The server already enforces
this: `RegisterModV1Endpoint` calls `CheckIfModExists` and returns `ModFileDoesNotExist`
otherwise. So the residue of a failed import is orphaned blobs, never dangling registrations.

### Per-mod ordering, bounded concurrency

Upload-then-register **one mod at a time**, not batch-upload followed by batch-register. That
bounds the orphan set to at most one blob rather than the whole remaining batch.

Do not run it strictly serially, though — 200 mods at two round trips each will crawl. Run a
handful of mods concurrently, each doing its own link → upload → register in sequence. The
*per-mod* ordering is what protects the invariant; batching across mods is independent of it.
This is network-bound, so a fixed 4–6, not the `ProcessorCount` the scanner uses.

### Retry is impossible without splitting the problem type

`CreateModUploadLinkV1Endpoint` has two guards that return **the same** problem type:

```csharp
if (mod is not null && mod.CheckHasVersion(...))     → ModVersionAlreadyExists
if (await modStorageService.CheckIfModExists(...))   → ModVersionAlreadyExists
```

The second fires on exactly the orphan a failed import just created. So retrying that mod can
never obtain an upload link again — and because both return `ProblemType.AlreadyExists`, the
client cannot tell which case it hit.

The two cases need opposite responses. An orphaned blob is *recoverable*: `RegisterMod` only
requires the blob to exist, and it does, so the correct move is to skip the upload and register
anyway. Give them distinct problem types and the per-mod flow becomes idempotent, making a
retry a no-op over everything already done:

| Link response | Action |
| --- | --- |
| `200` | Upload, then register |
| `FileAlreadyPresent` | Blob is there but unregistered — skip the upload, register |
| `AlreadyRegistered` | Skip both, count as success |

That last row also covers a teammate registering the same version concurrently, which from the
client's point of view is the same situation — and is a **success** for this flow, since the
bytes it wanted are present.

A useful consequence: orphans get adopted by the next import of the same mod version, because
identity is deterministic. A cleanup sweep for blobs nobody ever re-imports is worth doing
eventually, but the import flow does not have to solve it.

One thing to get right on the upload itself: a torn upload must not leave a visible blob, or
`CheckIfModExists` starts lying about completeness. Block-staged uploads only become visible on
commit, so this holds by default — just do not invent a resume scheme that commits partial
content.

## A note on "update available"

On the import and management pages, "this local version is not registered yet" is the right
definition, and it needs no version-string parsing at all — a local version either has a server
counterpart or it does not.

That is separate from `ModDependency.CanBeUpgraded()`, which asks whether a profile pins an
older *registered* version than the newest one, and therefore depends on how the server orders
versions. See [02 — Domain model](02-domain-model.md#version-ordering) and
[PLAN.md](PLAN.md) — ordering is moving from a curated `SequenceNumber` to something derived
from the version string, via a comparer the adapter supplies.

The two positions reconcile: an earlier design pass argued against parsing version strings at
all, because `modDesc/version` is free-form. The settled design parses **best-effort and
abstains** — where the strings genuinely do not decide the order, such as `v1` against `1.0`,
the user arbitrates once in a batched dialog and the answer is persisted repo-wide. Parsing
where it is safe, refusing where it is not.
