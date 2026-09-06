# Mod representation and the catalog

**Status: implemented.** Kept as the reasoning behind the shape rather than rewritten into a
description of it; where the implementation diverged, the divergence is stated.

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

That is `ModKey` and `ModVersionKey`. `ModKey.From` is the only way to build one and it
normalizes, so the type's only representable form is the normalized one — a rule the compiler
enforces rather than one every use site has to remember.

#### The other half: normalizing the id must not rename the file

The first version of this stopped there, and that was half a fix. `GetModFilePath` built the
install path out of `ModKey`, so the normalization did not stay in the identity — it reached the
mod folder, and applying a profile renamed every archive in it to lower case. That is visible:
Farming Simulator's own mod list shows filenames, and a mod referring to another by name is
reading a string the user can see.

The identity has to be normalized and the file has to keep its name, so they are two values, not
one. `ModVersion.FileName` records what the archive was called on the importing machine, the
profile's dependencies carry it to every other member, and the adapter installs under it —
falling back to the id where a repo has nothing usable registered.

The registered name is a string one member of a repo chooses that becomes a path every other
member writes to, so `ModFileName` checks it rather than trusting it, and has a private
constructor for the same reason `ModKey` does. Valid means a bare file name — no separator, no
traversal, nothing a path normalizer would rewrite — whose stem normalizes to the same `ModKey`.
That last clause is the bound: a repo can respell its own mods' files and nothing else.

A folder an older client already lower-cased is corrected by `ModSyncAction.Rename` — one
directory operation, no fetch, no removal. It is its own action rather than a fixup inside
`Keep` because a plan of nothing but keeps reports "already correct" and is never executed.
## A merged model

`ModListItemViewModel` used to take a `LocalMod` and delegate `Id`/`Name`/`Version`/
`Author` to it. That is the thing that would have hurt: the profile editor's available-mods list
is a *mixed* set that has to sort, filter and select uniformly, and two row types force
`IEnumerable<object>` plus duplicated templates.

One core model, in `Client.Core`, **flat — one record per version, no parent**:

```csharp
public record CatalogModVersion(
    ModKey ModId, ModVersionKey VersionId, string Name, string Description,
    bool IsLocal, bool IsOnServer, bool Locked)
{
    public ModVersionIdentity Identity => new(ModId, VersionId);
    public string? Author { get; init; }

    public ModImage? Icon { get; init; }                          // from the archive
    public IReadOnlyList<ModImage> Images { get; init; } = [];    // from the archive
    public IReadOnlyList<ModImageReference> ServerImages { get; init; } = [];

    public IReadOnlyList<ModSource> FoundIn { get; init; } = [];
}
```

The two image collections are not redundant. The archive's own are what derivatives are
*generated from*; `ServerImages` is what a registered version *renders*. See
[Registration decides where imagery comes from](#registration-decides-where-imagery-comes-from).

Flat matches everything around it. The server entity is one row per version, so
the wire format is flat and nothing has to be re-nested on receipt. `LocalMod` — the adapter's
output — is one record per file, which is to say per version. And a row view model wraps
exactly one version. A `CatalogMod` parent would be a shape invented in the middle of a pipeline
that is per-version at both ends.

**Grouping is a query, not a structure.** Two places need "all versions of this mod" — the
profile editor's version selector, and working out whether a newer version exists. Both are a
`ToLookup(x => x.ModId)` built where needed, which is cheaper than maintaining a parallel nested
model that has to be rebuilt every time the flat set changes — and it changes often, since
`ModCatalog` recomposes whenever a source checkbox is toggled.

The lazy-image design generalized for free. `LocalModImage` was
`(Name, CacheKey, Func<CancellationToken, Task<byte[]>>)` and said nothing about zip archives —
a server-backed version hands back the same record with an HTTP fetch in `Load`, and
`IModImageProvider` kept working untouched. It is now `ModImage`.

The client-side `Mod` that wrapped `ModDto` — the one that pre-split latest from older versions —
was **deleted rather than renamed**, along with `ModFakers` and the Bogus reference that existed
only for it. Its only job was
the grouping that the lookup above now does on demand. `LocalMod` keeps its name, because it
genuinely is "what was found on disk".

`ModStatus` was split. It mixed fact (`AlreadyInRepo`) with context-dependent
judgment (`New`, `UpdateAvailable`), and "New" means different things on the management page than
in the profile editor. The facts are the two bools on `CatalogModVersion`; `ModDisplayStatus` is
computed per context from them.

### What the server could not supply

All three of these are now closed:

- **No imagery of any kind.** A server-only row fell back to `Initials` forever, and its
  details dialog was empty — in exactly the list where the user is choosing between a local row
  and a server row of the same mod. See [Mod imagery](#mod-imagery) below.
- **No description.** The client's `Mod.Version` dropped `Description` on the floor, so a details
  modal on a server-only mod would have been blank. That type is gone; `CatalogModVersion` carries
  the description whatever the version's origin.
- **No usage information.** `GET repos/{repoId}/mods/usage` supplies it. See
  [Manage](#manage) below.

## Mod imagery

The server stores **the icon and every store image** for a mod version, so a mod nobody has
locally still renders with its real artwork. Without it, a repo's mods are initials in a list
and a blank details dialog — which is worst precisely where it matters, when someone is
deciding whether to add a mod they have never seen.

### What the volumes actually are

Measured over a real Farming Simulator 25 mods folder — 540 mods, 13.58 GB of archives:

| | |
| --- | --- |
| All images inside the archives | 7.06 GB compressed — **52% of the archive** |
| `icon_*` / `store_*` only | **0.16 GB compressed, 0.58 GB raw — 1.2% of the archive** |
| Count | 593 icons, 2,063 store images (~4.9 per mod, ~230 KB raw each) |
| Longest edge | median **512 px**, p90 512, **max 1024 — nothing larger** |

Two things to take from that. Half of a mod archive is image data, but nearly all of it is model
textures the catalog never touches; the store art we would actually serve is **a bit over one
percent**. And that art is *small* — half-K squares, never above 1024 px.

Storing originals would therefore be entirely affordable: well under a gigabyte for a repo of
this size, before dedupe.

#### And what the derivatives came out at

The figures below are **measurements, not estimates.** The derivative pipeline was run over the
same machine's mods — 2,656 images across 541 mods — with every one decoded, resized, encoded,
hashed and verified:

| | Source | Thumbnail (128 px) | Full (≤1024 px) |
| --- | --- | --- | --- |
| Median | — | **2.7 KB** | **21.4 KB** |
| Total | 594.7 MB of DDS | **7.2 MB** | **58.0 MB** |

Longest edge in the source set: median **512 px**, max **1024 px** — which confirms the sizing
argument below. **60% of the images needed the managed BC7 path**, because WIC refuses BC7.

Both derivatives came in at **roughly half** the ~6 KB and ~50 KB this design was originally
written against, so every figure derived from those estimates is conservative. A cold 540-row
list drawing icons is on the order of 1.5 MB rather than the ~3 MB estimated, against ~27 MB of
fulls or far more of shipped DDS.

### Store derivatives anyway — for transfer and decode

Cheap to store is not the same as cheap to use. A cold list of 540 rows pulls one icon each,
which as shipped DDS is tens of megabytes, and every one of them has to be decoded — for BC7,
through the managed path, because WIC refuses it — and then thrown away down to 64 px.

Re-encoded to WebP, that same list is a couple of megabytes of decodable images — decodable
through the codec the app ships with, since WIC only reads WebP where an optional Windows
extension happens to be installed. Roughly an order of magnitude less data and far less CPU, for
pixels that render identically at the size they are actually shown.

The measurement bears out the CPU half too: **60% of the 2,656 images needed the managed BC7
path**, so the decode being avoided is the expensive one rather than the cheap one.

Note what this means for sizing: since sources top out at 1024 px, the larger derivative is
**not** a downscale. It is a re-encode. DDS to WebP is where the saving comes from, not
resolution.

### Originals need no separate storage at all

They are already on the server, inside the mod blob, so nothing needs a second copy of them.

### Registration decides where imagery comes from

The rule is keyed on `IsOnServer`, not on whether the file happens to be somewhere on this
machine:

| Version | Imagery from |
| --- | --- |
| **Registered** | The server's derivatives — always, even if the mod file is also here |
| **Unregistered** (an import candidate) | Extracted from the archive in its source folder |

Deliberately *not* "prefer local originals where available". Finding the local file means
resolving it in the content store by hash or hunting through source folders, opening the
archive, decoding BC7 through the managed path and downscaling — per row, for a list of two
thousand. That is exactly the work the derivatives exist to avoid, spent to gain resolution
nobody is looking for in a 96 px strip.

It is not even faster after the first fetch. A content-addressed image is immutable, so it
crosses the wire once per machine ever and lives in the disk cache afterwards.

Three things follow:

- **Better cache keys.** A server image is keyed by its hash — stable across machines,
  unaffected by the mod file moving, and carrying no size suffix because a derivative arrives
  pre-sized, so it can never invalidate. The local key is
  `{modPath}|{entryName}|{length}|{crc32}` plus the width it was decoded at, which changes when
  the file does.
- **Uniform presentation.** Every row in a list renders through the same pipeline, rather than
  some from originals and some from derivatives with visibly different sharpness.
- **The content store is never an image source.** It holds mod files for sync and nothing reads
  images out of it, so that code path simply does not exist.

### The gap this leaves, and how it closes itself

Imagery uploads best-effort and never blocks registration, so a version can be registered with
no derivatives yet. Under the rule above that mod renders as initials — even for a user who has
the file sitting right there.

The fix is not a local fallback. A client that is about to render a registered version with no
server imagery, and that holds the mod file, is **exactly the client that should generate and
upload the missing derivatives**. Everyone benefits, not just whoever noticed.

That makes backfill opportunistic rather than a separate sweep: the gap is closed by the first
person who looks at the mod while holding it, which is the most likely thing to happen anyway.

### Two sizes

Store **two derivatives per image**, matching the two ways they are actually consumed:

| Derivative | Bound | Measured median | Consumed by |
| --- | --- | --- | --- |
| Thumbnail | 128 px longest edge | 2.7 KB | List rows (64 px) and the details strip (96 px) |
| Full | native, capped at 1024 px | 21.4 KB | Someone opening one image to look at it |

One small size covers both small uses, since the client downscales and caches per size already.
The cap is a safety net rather than a working limit — no image in the measured set reaches it.

The thumbnail is what earns its keep. Without it, a cold 540-row list pulls tens of megabytes of
fulls to draw 64 px icons; with it, under two. Roughly a tenfold difference on the single most
common operation in the app.

At those sizes the whole thing is small: the measured 2,656 images for 541 mods came to 58.0 MB
of fulls and 7.2 MB of thumbnails, and dedupe across versions of the same mod pushes the
per-version cost far below that.

**Every image is published at both renditions, icons included.** That is a correction to an
earlier shape which gave icons a thumbnail and nothing else: a details dialog for a mod that
ships no store images then had to draw a 128 px image large. Storing an icon only as a full
would have been worse the other way — ~21 KB behind every row of a cold list, which is the
tenfold difference the thumbnail exists to buy.

**Which forced a `Rendition` field onto the reference.** The original model — hash, kind,
position, filename — could not express two derivatives of one image: it allowed at most one
`Icon` reference, and store images had to smuggle the rendition into `Position` as arithmetic
standing in for a missing field. `ModImageReference.Rendition` now says which of the two it is,
`Position` goes back to meaning where the source image sits in the mod's own list, and **the two
renditions of one image share a position** — which is what identifies them as one image,
including when only one of the pair made it up. A partial set still resolves: whichever rendition
arrived stands in for the one that did not. See
[02 — Domain model](02-domain-model.md#images).

**The client generates them at import.** It already decodes DDS — including the managed BC7 path
that WIC refuses, which the measurement puts at 60% of images — and has the bytes open. The
server cannot decode DDS without taking on an image stack, and it has no business inspecting mod
files anyway. They are encoded as WebP.

### Content-addressed, like the mod files

Name image blobs by the SHA-256 of the derivative, in their own container:

```
mod-images/{hash[0..2]}/{hash}
```

Deduplication is the reason. Mod versions overwhelmingly reuse imagery — a release that changes
a script ships the same thirty store images as the one before it — so keying by content collapses
that to one copy. It also dedupes across mods and repos where artwork is shared.

Rough shape for a repo of 3,000 versions across ~600 distinct mods, at the measured ~4.9 images
per mod: ~15,000 references collapsing to ~3,000 distinct blobs. At the measured derivative sizes
that is on the order of **65 MB of fulls and 8 MB of thumbnails** — half what the original
estimates implied, and small enough that server-side storage is not a constraint on
this design at all. The argument for derivatives is entirely about transfer and decode.

### The database holds references

`ModVersion` carries an ordered collection of image references — hash, kind (icon or store),
rendition, position, original filename. **Not `ModAttribute`s.** Which images a version has is
structural: it drives what renders, and the system dereferences it. Attributes are tags. The
same rule applies to `Rendition`, which decides what is drawn at what size.

The blob itself is shared, so the reference is a pointer, not ownership. Deleting a version
removes its references; a blob is only collectable once nothing references it.

### Imagery must never block registration

`RegisterMod` verifies the mod file exists before writing metadata, and rightly so. **Images get
the opposite treatment.** They are decoration, and an import of 2,000 mods must not fail — or
worse, half-fail — because an image upload timed out.

So: register the mod, then upload imagery best-effort through
`PUT repos/{repoId}/mods/{modId}/versions/{versionId}/images`, and let the opportunistic backfill
above pick up whatever did not make it. A version with no images renders with initials, exactly
as a local mod without an icon does.

Best-effort is not the same as unrecorded. Every failure on that path is logged with the reason —
an upload refused with a status code, an image that would not decode, how much of a batch went
missing — and a mod whose imagery did not make it is counted into the shell's background-problem
notice. Without that, a storage container that does not exist, an expired token and a mod that
ships no pictures are the same event seen from outside: a row drawn with initials. See
[05 — Client](05-client.md#absorbed-is-not-hidden).

That endpoint **replaces** the whole reference set rather than adding to it. Imagery arrives
late, in unknown completeness, and possibly more than once — a retry, or a backfill firing on
another machine — and a replace is the only shape of that which is idempotent.

Uploading needs a **batch existence check** — "which of these hashes do you already have?" —
before uploading anything. After the first import into a repo most images are already present,
and 2,000 mods × 20 images is 40,000 uploads that mostly need not happen.

### Serving them back

Mod files go straight to blob storage over a SAS because they are large and fetched rarely.
Images invert both properties, so they invert the answer: minting 40,000 SAS URLs to draw one
list would be absurd.

Serve them through the API instead — `GET images/{hash}`, redirecting to a short-lived SAS or
streaming the bytes. The volume is fine because **a content-addressed image is immutable and
therefore cacheable forever.**

### What "authorized" means for a global address

The route carries no `repoId`, and it cannot: the whole point of content addressing is that one
blob serves every repo that references it. So the check is **authenticated user**, not Guest of
any particular repo — there is no repo to check against. Same for the batch existence check,
which is an existence oracle over every image in the system.

That is a real widening compared to everything else on the server, where repo scoping is baked
into the primary key. It is acceptable here only because of what is behind the address: mod
store art, which is already public on the sites the group downloads mods from, and which reveals
nothing about who is in which repo. Say so explicitly rather than labelling the endpoint "Guest"
and implying a scoping it does not have.

### Verify image bytes too

[07](07-mod-sync-design.md#cache-isolation) argues that a shared, cross-repo cache is only safe
because **every lookup is keyed by hash and every ingest is verified**. The image path is the
same shape — one globally shared address space, one permanently cached blob per address — and
gets the same rule, or the argument does not hold for it.

Concretely: the client hashes what it downloads and rejects a mismatch before writing to the
disk cache. Without that, one member uploading hostile bytes at an address another repo
references poisons that image for every machine, forever, because the client caches by hash and
never re-derives. The blast radius is decoration rather than mod files, which is why this is a
cheap check rather than an architectural problem — but it is the same check, and skipping it
would be an unexplained inconsistency rather than a decision.

Server-side verification on upload is the stronger version and is what shipped, alongside the
client's: `POST images/{hash}` hashes the bytes and refuses them unless they hash to the address
they were sent to. It stops a bad address being created at all rather than being detected by each
reader in turn, and it costs a hash of a few kilobytes.

That last point does most of the work. `ModImageProvider` already keeps a PNG disk cache keyed
by `CacheKey`, and for a server image the hash *is* the cache key — one that can never
invalidate. Each image is fetched once per machine, ever.

The client shape needs nothing new. `ModImage` is
`(Name, CacheKey, Func<CancellationToken, Task<byte[]>>)`, which says nothing about where bytes
come from — a server-backed image is the same record with an HTTP fetch in `Load`, and
`IModImageProvider`, the lazy-loading behaviour and both caches keep working untouched.

### The client-side image cache

The disk cache — `ModImageCache`, defaulting to `{LocalAppData}/ModsDude/image-cache` — is named
by a hash of `{cacheKey}|{maxWidth}`. Server imagery slots into it with one
simplification: a downloaded derivative is already the right size, so it is cached by **its own
hash** with no size suffix, and never needs re-deriving. The decode-and-downscale path stays for
local images, keyed as before.

Eviction approximates least-recently-used by last-write time. Windows does not maintain
last-access time by default and a cache this hot cannot afford a metadata write per read, so a
hit only refreshes the timestamp once it has already gone stale — and sweeps are spaced by how
much has been written rather than run per write, since walking the directory costs the same
whether one file or a thousand were added since.

**One cache per machine, not per volume.** The content store is per-volume because hardlinks
cannot cross volumes; images are always copies, so that constraint does not apply and splitting
them per volume would just duplicate them. It is configured alongside the stores in
`LocalState.Settings` — its own path and its own maximum size, with the same LRU eviction.

**Keep it separate from the content store.** Different size class, different lifetime, no volume
binding — and the separation is what keeps *"the content store is never an image source"* true,
which is the property that removes a whole class of lookup logic.

Sizing it is not a worry. At the measured 2.7 KB a thumbnail, caching every icon in a
3,000-version repo is under 10 MB; fulls are only fetched when somebody opens an image. A few
hundred megabytes is the realistic ceiling across several repos, and the default cap is 512 MB.
Everything in it is re-downloadable or re-derivable, so eviction never has to ask the user
anything.

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

**Every source starts switched off, every time.** The enabled set lives in the `ModCatalog`
itself and **nothing about it is persisted**: opening a page must never read a disk, and a folder
somebody looked in last week is not a standing instruction to look in it again today. `GetAsync`
only starts a scan for sources that pass `IsEnabled`, so with nothing enabled there is no file
access whatsoever — the catalog is the registered mod list and nothing else.

There is deliberately no remembered preference here. "Always scan Downloads" would be a
convenience that costs the guarantee above, and the guarantee is the point: navigating cannot
touch the filesystem, so no amount of clicking around the sidebar can.

Two things switch a source on, and both are the user asking for that folder specifically:

- **Adding an ad-hoc source.** Picking a folder is itself the act of asking for it to be read, so
  it is enabled as it is added. It stays view-scoped — there is nothing to persist about a folder
  that stops existing when the page closes.
- **Arriving from the drift notice.** `ShellNavigationService.GoToProfileModsAsync` carries the
  drifted instance's id through to `ProfileModsEditorPageViewModel.ScanInstance`, which enables
  that instance's mod folder. The versions the game downloaded are sitting in it and looking at
  them is the whole reason the user was sent there; making them find and tick the source first
  would be answering a question with a chore. Nothing else pre-enables anything — navigating to
  Repo → Mods or opening the editor from the sidebar scans nothing.

Because the list otherwise starts empty, the profile editor's **Sources** pane is expanded by
default: a collapsed pane would hide the one control that explains why the left-hand list has
nothing in it.

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

Deduplication is on `(ModId, VersionId)`, and `FoundIn` records **every occurrence** — one per
source, with the path and length it was found at — rather than collapsing to the first. So a row
can say where it came from, and two sources disagreeing about the bytes stays visible. With a
single enabled source, naming it on every row is noise; show it once more than one is active.

That makes the sharper case detectable: **two files claiming the same mod id and version but
holding different bytes** — typically a re-uploaded build the author did not renumber. Only one of
them can ever be registered.

**The catalog's chip is a warning, not the answer.** `CatalogModVersion.HasSourceConflict` compares
**file lengths**, which is free and runs over every row of every scan — so it under-reports, because
equal sizes are not equal bytes. That is the wrong way round for a decision that ends with somebody's
file in the Recycle Bin, so it decides nothing. It exists to say "this may need answering" before
anybody presses Import.

**The answer is `ModOccurrenceResolver`, and it hashes.** It runs at import, over the versions
actually selected, and after the already-registered pass — so re-importing a folder the repo already
holds pays nothing, and the cost falls on genuine duplicates alone. It groups a version's
occurrences by SHA-256 into `ModFileCandidate`s:

- **One candidate** — every source holds the same bytes. This is the ordinary case, a mod sitting in
  both the mod folder and Downloads, and there is nothing to choose between them. One is taken,
  nothing is asked, nothing is removed.
- **Several candidates** — the files genuinely differ. The user picks, once for the whole run, in
  `ModSourceConflictDialog`. Dismissing skips exactly those versions and lets the rest of the batch
  finish, the same bargain the version arbitration dialog strikes.

Length is not used as a shortcut past hashing, even though a difference in it is conclusive. The
saving would land only on the rare case where two sources genuinely disagree, and it would leave
candidates with no hash sitting beside candidates that have one — two kinds of identity for the
dialog and its answer to keep straight, in exchange for not reading a file the user is about to be
asked about anyway.

**The chosen occurrence is what everything reads.** `ModImportService.Chosen` is the single accessor
for the bytes, the file name registered against them, the size a progress bar counts to, and the
archive imagery is extracted from. `CatalogModVersion.OpenStream` still withholds a stream while
the size heuristic fires, but the import no longer reads it — a caller with no way to ask cannot be
handed one of two files at random, and the import is the one caller that *can* ask.

#### The copies not chosen are recycled

Leaving them means being asked the same question on every future import, by two files that will
never stop disagreeing. So the rejected copies go to the Recycle Bin — said on the dialog that asks,
before the choice is made, rather than reported afterwards.

Two rules make that safe:

- **Only rejected bytes.** Copies byte-identical to the one imported are left alone. The user was
  asked which of several *different* files to keep; deleting their duplicates of the winner is
  tidying they did not ask for.
- **Only after the whole action succeeded.** `ModImportResult.Superseded` reports the files; the
  caller removes them. The import knows a version registered, but not whether the thing the user was
  actually doing has finished — in the profile editor the import is the first half of a save, and a
  file removed for a revision that was never written is a file removed for nothing. `RepoModsPage`
  recycles once the run reports; `ProfileModsEditorPage` waits until the revision commits.
  `Result()` drops the superseded files of any version that did not import, because a resolved
  version that then failed leaves the repo holding neither file and the copy on disk is all that is
  left of it.

Recycling is best-effort and never fatal: a file the game is holding open stays where it is, which
costs a duplicate on disk and nothing else, and every failure is in the log.

## The `ModCatalog` service

The import page used to do the folder scan, the dedupe and the
instance-source dictionary inline in `InitAsync`. The profile editor needs all three, so it is a
repo-scoped `ModCatalog` in `Client.Core/Services`, merging the source scans with
`GET repos/{repoId}/mods` — walked a page at a time, since the stated target is thousands of
registered versions per repo.

- **Cache per source, and compose on demand.** Not one cached catalog: a
  `Task<SourceScan>` per source, with the merged view built from the enabled ones. This is what
  makes the checkbox usable — toggling a source recomposes from memory and is instant, and
  adding one scans only the new folder rather than every folder again.
- **Cache the `Task`, not the result.** A second caller arriving during an in-flight scan joins
  it rather than starting a second `Parallel.For` over a thousand archives.
- **Invalidate explicitly** — on import, and on instance-settings change. Never silently. A
  stale catalog that quietly refreshes mid-interaction is worse than one the user re-triggers,
  so expose a Rescan action, per source and for all. The pages surface only the one that covers
  every source — a per-source button is one more control on every row for something the whole-list
  one already does, and the per-source call stays available for a caller that needs it.
- **The 150 ms scan delay and the cancellation behaviour moved into the service**, unchanged.
  They exist so that a page nobody stopped on never touches the disk; that reasoning is not
  specific to the import page. Note the delay predates the sidebar's drag-selection fix and stays
  on its own merits.
- **Report per-source progress and failure.** A source can vanish or be unreadable — an
  unplugged drive, a folder the user deleted. That marks one source as failed in the
  list rather than failing the whole catalog.

## Pages

### Manage

**Import was merged into Manage.** They were sibling menu items under `RepoModsPage`
showing overlapping data under different rules, which was the main thing about that area that
confused. Same rows, same templates, one service.

**It is laid out like the profile mod editor**, and for the same reason: both pages are one act —
deciding what a collection should hold, then writing it. Two lists. On the left, what the enabled
sources hold and the repo does not; under it, the source pane described above — instance folders,
Downloads, anything the user adds for the session — each with its checkbox, so the set of local
candidates is adjustable in place rather than being a fixed consequence of the repo's instances. On
the right, what the repo holds, plus whatever has been lined up to join it.

A mod is **never on both sides at once**, and the row that moves rightwards is the same row object,
so its icon and its per-row import marks come with it. Presence is therefore which list a row is in
rather than a chip on it, which is why the filter chips are gone — all but *Unused*, which the
lists cannot draw and a delete needs.

**The bulk moves sit under the list they read**, not in the bar at the bottom of the page: what "add
all shown" takes is what is on screen above it, and the search is how a subset of it is picked. The
bar keeps the two actions that write — Import, and the discard that throws the queue away.

Beside it, **"add N updates"** — the versions the repo's own ordering places after everything it
holds of that mod, which is the errand this page exists for and is otherwise picking six rows out of
a folder of five hundred. The count is on the button because that is the only place it would be
acted on, and those rows carry the existing `UpdateAvailable` chip so the count is findable. It
ignores the search: an update is a fact about the repo, not about the view.

It is a **split button**, like the editor's save. Behind the caret: *add N unregistered versions* —
every version the repo lacks of every mod it holds, including ones older than its newest and ones
the comparer could not place. That is a real thing to want (a profile can pin any version, and a
repo missing the one a teammate is on cannot be joined) but not the daily errand, so it costs the
extra click. Neutral chrome rather than the editor's accent: the accent on this page belongs to
Import. The caret carries **its own** enabled condition rather than following the primary, unlike
the editor's — there can be older versions to add when there is not a single update, which is
exactly when the menu is worth opening.

**Nothing is uploaded until Import**, exactly as nothing is uploaded until Save in the editor. That
is what makes taking a mod back free, and what the discard confirmation says rather than warns. What a run did not finish stays queued and marked, so the
button is also the retry, and the summary keeps the per-row results on screen until the user asks
for a fresh list.

**The right list is ordered by what wants an answer**, not alphabetically: failed, then skipped,
then still-queued, then what the repo already held. The top of a two thousand row list is the only
part anyone reads after an import, and a failure buried at "S" is a failure nobody sees. It is
ordered when the list is *built* and never live — rows changing rank mid-import would reshuffle the
list under the pointer watching it — so the import re-sorts exactly once, when it is over.

Two things this needed, and both now exist:

- **"Unused" cannot be computed client-side safely.** The mod list carries no
  usage information, and profile dependencies arrive one profile at a time. Deleting on a
  partial client view risks removing a version a teammate's profile just picked up.

  It got **its own endpoint**, `GET repos/{repoId}/mods/usage`, rather than a field on `ModDto`.
  The reason is the mod list's delta form: it is keyed on `ModVersion.Updated`, and usage changes
  when a *profile* is edited, not when a version is. Folding usage into the mod list would have
  meant either serving stale usage to every client that syncs incrementally, or restamping
  `Updated` on every version a profile save touches — two thousand rows a save, and a delta the
  size of a full listing. Two facts with different lifetimes, so two resources.

  The response is **sparse**: a version that does not appear is unused. Which means absence is
  only an answer once the whole listing has been read, so a client must exhaust the cursor before
  acting on it — acting on a partial view is the hazard the endpoint exists to remove. It is
  advisory in any case; the delete endpoints re-ask the database when it matters, and the
  dependency foreign key refuses underneath them.
- **There was no delete endpoint**, and the per-version delete refuses the last version, so
  "remove whole mod" needed its own path rather than a loop of version deletes. Both exist, and
  both delete the blob as well as the row — the database commit first, since a stranded blob is
  recoverable and a registration whose blob is gone is not.

### Profile mod list editor

Two lists: available on the left, in-profile on the right. The left is the union of registered
mods and local candidates, so a mod can be added to a profile *and imported* in one action
without a detour to another page. It carries the same source list and checkboxes as Manage —
adding a mod straight from Downloads while building a profile is the point of having sources at
all.

**One search box, over both lists.** It sits above the two columns rather than in the left one's
header, because a mod is only ever on one side: a box that reached only the left list answered
half the question, and the half it could not answer was "is this already in the profile?".
`ProfileModRowViewModel.Matches` delegates to its `Item`, so both sides answer the same question
the same way and the answer follows the version selector. Same shape as Manage, which has the
same two lists and the same question.

Each header then reads **"N of M mods"** while a search is narrowing it — a count that only ever
said "412 mods" could not distinguish a search that found nothing from an empty list. The right
list gains a second empty-state message for the same reason: with the search reaching it, "nothing
in this profile yet" would be a lie for a two-thousand-mod profile with no match.

**Updates belong on the right, not the left.** If a mod is already in the profile, also showing
it on the left as "update available" puts the same mod on both sides at once. Render it as an
update affordance on the right-hand row, plus a "N updates available" batch action in the
header. The left list stays a clean "not in the profile" set.

**That section stays on screen at zero**, reading "No updates available". "Are there updates?" is a
question people come to this page to answer, and a section that is absent when the answer is no
never answers it — it just leaves them looking. It also stops the list below moving as the count
changes.

The right list is keyed by `ModId` — the domain enforces one `ModDependency` per mod — so
moving a mod rightward also means choosing a version. That row needs a version selector and a
`Locked` toggle.

**The two bulk moves are split, and sit under the left list.** *Add all shown new* takes everything
on screen the profile has never held; *Restore removed* is an undo, so it puts back what this draft
took out at the version and lock the profile still holds rather than picking a default. One button
doing both would silently re-add a removal at the newest version — which is a different pin from the
one that was there.

**Both lists lead with what the draft has done to them.** On the right, the same ranking as Manage:
what could not be imported, then what is still pending, then the rest. On the left, mods this draft
has *taken out* of the profile — they are back on the left looking exactly like a mod that was never
in it, so they get a **"Taken out" chip**, the counterpart of the pending-import chip on the other
side, plus a count in the header. Caution-coloured rather than the accent a pending import gets: one
is a row about to gain something and the other a row about to lose it. It is a
`ModDisplayStatus`, which is where a per-page judgment about a row belongs. Neither is a live sort; the left re-sorts on every recount, and the right when a save stops at
a failed import.

### Import on save

Nothing is uploaded until Save. A local-only mod moved rightward is a **pending** row; Save
imports the files through `ModImportService` and only then writes the list. Importing on the way
in would make Cancel meaningless and litter the repo with mods nobody kept — and a save whose
import does not fully succeed writes nothing at all, because a profile pinning versions that
failed to upload is worse than a profile nobody saved.

Then **one request writes the whole list**: `PUT .../profiles/{profileId}/revisions`, carrying
every pin and the revision the page was read at. That used to be a delete, an upgrade batch and
an add-or-update per changed mod, which is why `ProfileModListDiff` existed. The diff is still
computed — but only to describe the save afterwards ("12 added · 3 changed"). What goes over the
wire is the list itself, because a revision is a snapshot and the server has to record exactly
what the page shows.

The batch upgrade endpoint went with the per-dependency writes, and nothing was lost with it.
What an update *is* is a question this page already answers from the mod list it has in hand, and
a whole-list save expresses "and these are now the newer versions" without a second endpoint that
can only express one shape of change.

## Version locking

`Locked` exists to stop version-sensitive mods being bumped by accident. The motivating
case is a Farming Simulator map: changing map versions partway through a save can
corrupt it, and the damage shows up long after the change that caused it.

### The adapter sets it once, at registration

An adapter can tell that a mod is version-sensitive — a Farming Simulator map mod declares its
maps in `modDesc`, so the adapter can spot one while it is already parsing that file.

It sets **`ModVersion.Locked`**, a real domain property, at registration — re-derived from each
file rather than inherited, which comes out consistent because every version of a map mod
declares its maps. There is no prompt.

Two properties, two scopes, described in full in
[02 — Domain model](02-domain-model.md#locking-in-two-places): `ModVersion.Locked` says the mod
itself is version-sensitive, `ModDependency.Locked` is the user's per-profile decision, and a
mod counts as locked when either is true. The adapter can only ever set the first.

Because the adapter re-derives it, there is **no repo-wide user override** — someone who
disagrees unlocks on the dependency, which is per-profile and survives version changes. That is
the price of collapsing `Mod` into the version, and a fair one at this scale.

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
`ModVersion.Locked` nor `ModDependency.Locked`. Locked mods are not candidates, and the action reports
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
This is network-bound, so a fixed count — five — not the `ProcessorCount` the scanner uses.

The slot is held for **a whole mod**, not a single version, which is what makes the sequential
registration below fall out for free.

### Importing several versions of one mod at once

Nothing stops a single import carrying two new versions of the same mod — one sitting in an
instance's mod folder, another in Downloads. A worked example, mod A:

| Version | State |
| --- | --- |
| v1 | registered, sequence 0 |
| v4 | registered, sequence 1 |
| v2 | unregistered, in the instance's mod folder |
| v3 | unregistered, in Downloads |

The intended result is `v1, v2, v3, v4`, which means **v4's sequence number moves too** — two
rows insert ahead of it. Source is irrelevant to ordering; only the version strings matter.

**Positions are computed against the final intended order, then applied one at a time.** Register
the new versions in ascending order, each as *insert before the next already-known version*: v2
before v4 gives `v1, v2, v4`; then v3 before v4 gives `v1, v2, v3, v4`. Each step is individually
valid, so no batch-placement API is needed.

The instruction is **relative on purpose** — *insert before v4*, not *take sequence 2*. Absolute
positions would collide outright under concurrent registration.

But relative alone is not enough, and it is worth being precise about why. Two members inserting
different new versions of the same mod, each computing against a state that does not yet include
the other's:

```
start        v1, v4
A lands      insert v3 before v4   ->  v1, v3, v4
B lands      insert v2 before v4   ->  v1, v3, v2, v4     <- wrong, and silent
```

No constraint is violated, and any higher sequence reads as newer — so a profile pinned to v3
would be offered v2 as an upgrade: a downgrade dressed as an update.

**So assert both neighbours, not one.** The client already knows where the version belongs in the
order it computed, so it can say *insert v2 between v1 and v4* and the server can check that v4
really does immediately follow v1. Above, B's assertion fails once A has landed; B refetches,
recomputes against `v1, v3, v4`, sends *insert v2 between v1 and v3*, and the result is correct.

That is optimistic concurrency using only what the client already has — no revision token, no
extra round trip in the common case, and the retry is the refetch-and-recompute loop the import
already needs for the *already present* responses. The first version of a mod asserts an empty
set; an append asserts what it believes the last version to be, so it also catches somebody else
appending first.

A spurious rejection is possible — someone appending v5 while you insert v2 invalidates an
assertion that would have been harmless. Retries are cheap and this is rare; precision is not
worth the complexity of narrowing it.

**A manual reorder is the backstop.** Optimistic concurrency handles races, but ordering can
also simply be wrong — a comparer that guessed badly, or an arbitration someone regrets. The
management page reorders a mod's versions by hand through
`PUT repos/{repoId}/mods/{modId}/versions/{versionId}/placement`, which asserts both neighbours
exactly as registration does, and returns the resulting order — because rewriting a hand-authored
order takes one move per version that actually shifted, and each of those placements has to be
computed against the order the previous move left behind. Unlike an import, which recomputes and
retries, a rejected move is a human's answer to a question the server cannot re-answer, so the
client refetches and asks again.

Note the non-obvious part, which only a real database shows: **a move cannot be done as a plain
renumber**, because it is a rotation and no order of single-row writes takes a rotation through
the unique index. See
[02 — Domain model](02-domain-model.md#a-move-is-a-rotation-and-a-rotation-cannot-be-renumbered-in-place).

That adds one constraint to the concurrency rule above: **versions of the same mod register
sequentially**, because each insert depends on the previous having landed. Concurrency stays at
the level of distinct mods, which is where it was anyway.

### Ordering a set is a partial order, not a sort

With a comparer allowed to abstain, `OrderBy` is the wrong tool — .NET's sort assumes a total
order and will happily produce nonsense, or throw, when comparisons are inconsistent.

Order the union of registered and incoming versions by building a **partial order** from the
pairwise comparisons and topologically sorting it. A mod has at most a few dozen versions, so
comparing every pair is free.

The useful consequence: **an abstention is not automatically a question.** If the comparer cannot
place `v1` against `v4` directly, but does know `v1 < v2` and `v2 < v4`, the order is settled
transitively. Only pairs left genuinely unordered — no path between them in either direction —
need a human.

### When abstention forces a prompt

The adapter is *not* required to parse everything. What abstention costs is a question, asked at
a specific moment:

- **Resolve before registering, never after.** A version registered at a provisional position
  would make the newest version wrong in the interim, and "latest" is what drives update
  detection — a mod appended past v4 would advertise itself as the newest and offer everyone a
  downgrade.
- **One dialog per import, covering every ambiguous mod**, showing each one's version list in the
  order that *was* derived, with the unplaceable versions floating and draggable into place. One
  interaction per mod, not one per unresolved pair.
- **Unambiguous mods never wait.** Compute ordering for the whole selection first; everything the
  comparer settled proceeds immediately, and only the remainder needs the dialog.
- **Cancelling the dialog skips those mods, it does not abort the import.** An unorderable mod is
  one mod's problem, and someone importing two thousand of them should not lose the batch over
  it. The skipped ones stay unregistered and can be imported again later.

### Retry is impossible without splitting the problem type

`CreateModUploadLinkV1Endpoint` used to have two guards returning **the same** problem type,
`ProblemType.AlreadyExists`: one for a registered version, one for an existing blob. The second
fires on exactly the orphan a failed import just created. So retrying that mod could
never obtain an upload link again, and the client could not tell which case it had hit.

The two cases need opposite responses. An orphaned blob is *recoverable*: `RegisterMod` only
requires the blob to exist, and it does, so the correct move is to skip the upload and register
anyway. With distinct problem types the per-mod flow is idempotent, and a retry is a no-op over
everything already done:

| Link response | Action |
| --- | --- |
| `200` | Upload, then register |
| `FileAlreadyPresent` + the blob's hash | Hash matches ours — skip the upload, register. Differs — this is an id/version collision, not our orphan; report it and register nothing. `null` — nothing is established, so register nothing |
| `AlreadyRegistered` | Skip both, count as success |

**`FileAlreadyPresent` has to carry the blob's hash.** Registering against a blob whose contents
you have not established writes a hash that describes a different file, which makes every future
download fail verification with no way to repair it — the blob exists, so no upload link can be
minted for it again. See
[07 — Mod sync design](07-mod-sync-design.md#hostile-or-wrong-hashes-have-to-be-unregisterable-not-just-undownloadable).

That last row also covers a teammate registering the same version concurrently, which from the
client's point of view is the same situation — and is a **success** for this flow, since the
bytes it wanted are present.

A useful consequence: orphans get adopted by the next import of the same mod version, because
identity is deterministic. A cleanup sweep for blobs nobody ever re-imports is worth doing
eventually, but the import flow does not have to solve it.

One thing to get right on the upload itself: a torn upload must not leave a visible blob, or
`CheckIfModExists` starts lying about completeness. Block-staged uploads only become visible on
commit, so this holds by default — just do not invent a resume scheme that commits partial
content. `BlockBlobModFileUploader` stages blocks and commits once, with the content hash written
as metadata in the same commit.

## A note on "update available"

On the management page, "this local version is not registered yet" is the right
definition, and it needs no version-string parsing at all — a local version either has a server
counterpart or it does not.

That is separate from the profile editor's question — whether a profile pins an older
*registered* version than the newest one — which depends on how the server orders versions. See [02 — Domain model](02-domain-model.md#version-ordering): ordering derives from the
version string, via a comparer the adapter supplies, and is stored in `SequenceNumber` rather
than recomputed on read.

The two positions reconcile: an earlier design pass argued against parsing version strings at
all, because `modDesc/version` is free-form. The settled design parses **best-effort and
abstains** — where the strings genuinely do not decide the order, such as `v1` against `1.0`,
the user arbitrates once in a batched dialog and the answer is persisted repo-wide. Parsing
where it is safe, refusing where it is not.
