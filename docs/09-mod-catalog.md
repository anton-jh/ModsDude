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
public bool IsLocal { get; }      // found in one of the game's mod folders
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
`ModCatalog` recomposes whenever a source chip is toggled.

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
judgment (`New`, `UpdateAvailable`), and "New" meant different things on the old management page than
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

A mod does not only arrive via the game's mod folder. It is at least as common for it to be
sitting in Downloads, freshly fetched from wherever the group gets mods. The import surface —
the profile editor — scans a **set of sources**, not a fixed folder.

### Standing sources

Present automatically, without the user configuring anything:

| Source | Where |
| --- | --- |
| Each mod folder the game reaches | From the local settings, via `ILocalModAdapter.GetInstalledMods` — one source per target, named where the game has more than one |
| The system Downloads folder | Once per machine, not once per folder |

Downloads needs care to locate. .NET has no `SpecialFolder.Downloads`; the correct route on
Windows is `SHGetKnownFolderPath` with `FOLDERID_Downloads`, because the user may have
relocated it. Falling back to `%USERPROFILE%\Downloads` when that fails is fine, but do not
*start* there — a relocated Downloads is common and the fallback path will simply not exist.

### Ad-hoc sources

The user can add a folder with the system folder browser — `IDialogService.PickFolder` already
exists — and it appears alongside the standing ones. Ad-hoc sources are **view-scoped**: they
live as long as the page does and are not persisted. Someone importing from a USB stick or an
extracted archive should not have that folder haunting the UI for months.

### Another profile is a source too, in the profile editor

A profile's pins are registered versions by foreign key, so the catalog already holds every one of
them. Reading another profile as a source therefore **costs no scan at all**: what it contributes is
a membership set and a version per mod, composed by the editor and consumed by the editor's own
filter, exactly as the repo is. `ModCatalog` never sees one.

It is view-scoped like an ad-hoc folder, and added by picking a profile from a dialog of its own
rather than from *Copy from a profile…*. The two are different acts: a source only ever **offers**,
putting that profile's versions on the left where each of them is still a row somebody has to move,
while a copy writes into the draft and its *Replace* mode takes things out. A statement about the
whole list is not something a chip could express, which is why *Copy from a profile…* stays.

It does read the network, which no other source does. The rule it must not break is that
*navigating* touches nothing — and enabling a chip is not navigating.

**It carries the lock as well as the version.** A row that is there because a profile put it there
is added at that profile's version *and* lock. Copying a list already brings `Locked` across, and a
chip that brought one and not the other would disagree with the button next to it about what "what
that profile holds" means.

### The source list

Every currently available source is offered, each as a toggle chip. Disabling one removes its mods
from the merged list without removing the source, so a user can narrow to "just what is in
Downloads" without losing their game's own folders.

**Every enabled chip contributes to one union.** The left list is what the enabled sources offer
between them: every registered version while the repo's chip is on, whatever the enabled folders
hold, and whatever the enabled profiles pin. The repo chip used to mean something subtly different —
"and drop the registered rows" — which reads the same in the common case and is not the same thing
at all: a profile source's versions are all registered by construction, so under the subtractive
reading, switching the repo off would have left a profile chip with nothing to show. The visible
consequence of the change is that the repo off plus a folder on now lists what that folder holds
*including* what the repo already has, where it used to list only the unregistered half. The **New
to the repo** filter chip is what narrows it back, and it composes with everything else the way the
other chips do.

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
  drifted folder's `ModTargetRef` through to `ProfileModsEditorPageViewModel.ScanTarget`, which enables
  that one folder. The versions the game downloaded are sitting in it and looking at
  them is the whole reason the user was sent there; making them find and tick the source first
  would be answering a question with a chore. Nothing else pre-enables anything — navigating to
  Repo → Mods or opening the editor from the sidebar scans nothing.

Because the list otherwise starts empty, nothing about the profile editor's sources is behind a
click: they are a **row of chips** directly above the left list's filter chips, which is the one
shape that is both always visible and nearly free.

Disabling a mod folder as a *source* has no effect on syncing to it. The two roles are
independent; see below.

#### The source chips

Three shapes were tried. An expander, whose collapsed state hid the one control that explains an
empty list. A fixed pane, which cost a quarter of the left column permanently — about 270px, under
the bulk moves, where it was furthest from the list it feeds. And a chip row, which is what there is:
roughly 32px, and honest about what these things are, because every source *is* a filter over one
list and chips all looking alike say so.

`[This repo 1,204] [FS25 mods 540] [Downloads 12] [Co-op 87] [ModHub 7] [+ folder] [+ profile] [⟳]`

- **Above the filter chips, not among them.** Sources are independent toggles and the filters are
  mutually exclusive; one row of both would read as one control with two kinds of behaviour in it.
  Above rather than below, because a source decides what the list is *of* and a filter narrows what
  it then shows.
- **A count and nothing else on the chip.** The units are what the chip's own name is for, and at
  four chips wide there is no room to spell them twice.
- **Failure colours the chip.** A second line under one chip costs every chip in the row the height
  of the worst of them, so an unreadable folder turns red and says why in its tooltip, alongside the
  path.
- **Session-scoped chips carry their own ⨯.** A folder off a USB stick and a profile being diffed
  against both stop existing when the page does, and switching one off is not the same as being done
  with it.

**Repo → Mods draws the same row**, off the same view model and the same two styles, which live in
`View/Resources/Controls.xaml` rather than in either page. That page kept a pane for a while on the
grounds that it had the room; room turned out not to be the argument. Its chips are the scan
locations only — the repo's own registered list is the right-hand column there, not a source — so it
has no repo chip and no `+ profile`, and its ⨯ is on ad-hoc folders alone. The rescan moved onto the
row with them, and the pane's heading went with it.

#### The repo is a source too, in the profile editor

The profile editor's left list is the **union** of what the enabled sources offer, so the repo is
the one contributor to it that had no control — and turning it off is how to ask "what is on this
computer, or in that profile, that this repo does not have". It heads the chip row, starts on, and
is `ModSourceKind.Repo`.

**It is a filter, not a scan.** Nothing about it reaches `ModCatalog`: the catalog still merges the
registered half, and it is the editor's own composition of the left list that leaves those rows out.
That is not an implementation detail worth hiding, because it is load-bearing — the pinned rows and
every row's version selector are built from the same snapshot, and a profile pins registered
versions, so dropping them from the catalog would turn every pinned row into an unresolvable
placeholder. Composing the list instead is also why unticking it is instant and costs no round trip.

The catalog page has no such checkbox and needs none: its two lists already split on exactly this
line, and unregistered-and-local is what the left-hand one *is*.

### Scanning arbitrary folders already works

`IBaseModAdapter.GetModsFromFolder(path, ct)` takes any path, and the local variant is a
thin wrapper that supplies one of the game's own folders. Non-mods are already handled: `GetZip`
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
which means uninstalling things from it. A game's own mod folder is both. Downloads and ad-hoc
folders are **only ever sources** — nothing in sync will ever delete, move, or quarantine a file
in them.

### Remote sources point, and never supply

A game's adapter can name places outside the machine that know of newer versions — for Farming
Simulator 25, ModHub, which the server crawls (see [03 — Server](03-server.md#the-modhub-crawler)). In the
profile editor each is a chip at the end of the source row, `ModSourceKind.Remote`, and **it starts switched
on** — the one source besides the repo that does. Sources start off so that opening a page never reads a
disk; this reads the ModsDude server, which the page is reading anyway, and a chip nobody knows to click is
updates nobody sees.

**What it contributes is a link, not a version.** A version from ModHub has no bytes behind it here, so
letting it into the version index would expose it to everything that can pin a version — the row's
selector, *Update all*, import on save — and each would have to learn to refuse it. Instead an offer is a
chip on the mod's own row, *ModHub 1.0.1.2 ↗*, on either list, and it cannot be moved anywhere. Following it
opens the mod's page and switches Downloads on, since the file lands there; the rescan once the download
finishes is still the user's. When the scan finds the file, the version is known, and a known version is
never offered — so the link is replaced by an ordinary version in the same pass.

**On the right list they are updates**, because that is what they are. The **Updates** filter takes in a
pinned row with a link as well as one with an update here, and the updates band names them beside its own
count — "3 updates available · 5 more on ModHub · 2 locked", or "No updates here · 5 on ModHub" — as a link
to that filter. Beside it rather than added into it: *Update all* cannot move a pin to a version with no
file, so a single number would promise something the button next to it will not do. Locked pins are counted
apart, as they are for updates here.

**An offer has to be newer than every version known here** (`RemoteModOffers.Newer`): not already known,
placed after at least one known version and before or level with none. An abstention does not count, for
the same reason it does not count as an update.

The chip counts the rows carrying a link. It shows no count until the server has answered, `…` while it is
asked, and red with the reason if it could not be. **Every known mod is asked about, not only the pinned
ones**, incrementally, so switching a folder on later asks only about what it brought. An answer the server
cannot yet vouch for — it has not finished reading ModHub — says so on the chip's tooltip; switching the chip
off and on asks again.

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

Proving it exactly would *not* mean hashing every archive in every source, which an earlier version
of this document claimed. Only a version found in more than one source can conflict at all, and of
those only the unregistered ones are a genuine question — a registered version already has
`ContentHash`, so each local copy can be compared against it and decided rather than asked about.
The set worth hashing is bounded by duplicates, not by scan size, and is affordable now that it is
known to be small; it simply is not done on the row today, because a full file read for a question
that may never be asked is its own decision to make.

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
source dictionary inline in `InitAsync`. The profile editor needs all three, so it is a
repo-scoped `ModCatalog` in `Client.Core/Services`, merging the source scans with
`GET repos/{repoId}/mods` — walked a page at a time, since the stated target is thousands of
registered versions per repo.

- **Cache per source, and compose on demand.** Not one cached catalog: a
  `Task<SourceScan>` per source, with the merged view built from the enabled ones. This is what
  makes a source chip usable — toggling a source recomposes from memory and is instant, and
  adding one scans only the new folder rather than every folder again.
- **A source switched off goes on standby.** It leaves the merged view and keeps its scan, which a
  rescan still refreshes; only *removing* an ad-hoc folder forgets it. So `GetAsync` answers with two
  sets: `Versions`, what the enabled sources hold, which is what a list of mods is a list *of*, and
  `Known`, widened to the standby ones, which is what a version selector and an update planner read.
  A chip and a rescan are different events and a single set cannot serve both — unticking a source
  must not shrink a draft's selector, and a rescan that no longer finds a file must. The editor used
  to do this with an append-only dictionary of its own, which got the first right and the second
  exactly wrong: a deleted archive stayed offered and stayed counted as an update forever. Both sets
  are recomputed from the current scans, so neither can outlive what is on disk.
- **Cache the `Task`, not the result.** A second caller arriving during an in-flight scan joins
  it rather than starting a second `Parallel.For` over a thousand archives.
- **Invalidate explicitly** — on import, and on a change to the game's settings. Never silently. A
  stale catalog that quietly refreshes mid-interaction is worse than one the user re-triggers,
  so expose a Rescan action, per source and for all. The pages surface only the one that covers
  every source — a per-source button is one more control on every row for something the whole-list
  one already does, and the per-source call stays available for a caller that needs it.

  **An apply is the exception to "never silently"**, because it is not a refresh but a fact: it has
  just changed the folder the scan is a picture of. `ModSyncService` raises `ModFolderChanged` once
  the files have moved - and only where the plan had something to change, so a re-apply that finds
  nothing to do raises nothing - and the editor drops the cached scan of every source that is that
  folder (`ModCatalog.RescanFolder`, by path, **whether the chip is on or on standby**) and
  recomposes. Without it a mod the apply recycled stayed in the scan as an import candidate whose
  file was gone, and one it installed was missing from it. A recompose rather than a reload, so the
  draft survives, and one that lands mid-save waits for the save like any other. A folder the page
  never scanned has nothing cached and does not recompose.
- **The 150 ms scan delay and the cancellation behaviour moved into the service**, unchanged.
  They exist so that a page nobody stopped on never touches the disk; that reasoning is not
  specific to the import page. Note the delay predates the sidebar's drag-selection fix and stays
  on its own merits.
- **Report per-source progress and failure.** A source can vanish or be unreadable — an
  unplugged drive, a folder the user deleted. That marks one source as failed in the
  list rather than failing the whole catalog.

## Pages

### Manage

**Manage no longer imports.** It was once Import and Manage as sibling menu items, then one page
laid out like the profile editor - what the sources held on the left, what the repo held on the
right, and importing as the move between them. That left the repo's own list with a job to do
(reorder, delete, find out what is unused) sharing a page with a second job that already has a home:
the profile mod list editor registers whatever its draft pins that the repo does not hold, as part of
[Save](#import-on-save). So the page is **one list, the repo's**, with no source chips, no left
column and no bar of import buttons. It reads the repo and nothing else - `ModCatalog` is built with
no source switched on, so opening it never touches a disk.

**Its one filter is *Unused*.** Presence is no longer a question (everything on the page is
registered), and the rest is what the search box is for. *Unused* is the one thing the list cannot
draw and a delete needs.

**A guest reads the same list**, and Reorder versions, Delete version and Delete mod are refused
with a reason - see [the permission rules](05-client.md#what-a-level-closes-and-how-it-says-so).

**Ordered by name, then by the repo's own version order** - the arbitrated `SequenceNumber`, never
one re-derived from the version strings. A second opinion here would be free to disagree with the
one the whole repo shares.

**The row and the header carry the repo's numbers.** Each row shows the size of its file beside the
version chip and how many profiles use it (see below); a line under the title adds the whole repo up as versions,
mods and bytes (it does not narrow with the search, unlike the count beside it). The size comes from `ModDto.SizeBytes`, which every registered version has.
The same numbers are turned on for this page only (`ShowStatistics`): in the
profile editor the row is about what the draft pins, not about the repo's other profiles.

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

  **Two counts per row**, `currentProfileCount` and `pastProfileCount`: the profiles whose *newest*
  revision pins the version, and the profiles that have an *older* revision pinning it. A profile that
  holds a version in both is in both. Each is distinct profiles rather than revisions. The Mods page
  shows them on the row; "unused" is both being zero.
- **There was no delete endpoint**, and the per-version delete refuses the last version, so
  "remove whole mod" needed its own path rather than a loop of version deletes. Both exist, and
  both delete the blob as well as the row — the database commit first, since a stranded blob is
  recoverable and a registration whose blob is gone is not.

### Profile mod list editor

Two lists: available on the left, in-profile on the right. The left is the union of what the enabled
sources offer, so a mod can be added to a profile *and imported* in one action without a detour to
another page. Its sources are the chip row described above — adding a mod straight from Downloads
while building a profile is the point of having sources at all.

**A change to the catalog is never a reason to re-read the profile.** The load is split in two. The
server half — the dependency list, the revision the page is based on, and the baseline a save diffs
against — is read on init, on *Discard*, and after a save has committed. Everything that changes
what is merely *known* — a source chip, a rescan, a folder added or removed, a drift notice's scan
target — runs a **recompose**, which rebuilds the chips, the left list, the version selectors and
the update plan and leaves the draft, both selections, the pending removals, the search and the bulk
undo exactly where they were. The two used to be one method, which meant ticking a chip silently
discarded everything the user had built, dropped the unsaved-changes flag to false and released the
navigation lock. It was also a needless round trip: a recompose reads the catalog, which composes
from scans already in memory.

**What the draft holds is part of the merged set.** The version index is the union of everything the
catalog has read — `ModCatalogSnapshot.Known`, so the standby sources as well as the enabled ones —
and the versions the draft is pinning. So a pending row whose folder has just been switched off keeps
its pin, keeps the `FoundIn` occurrence that names the file on disk, stays reported as pending, and
still imports on save. Disabling a source is a statement about what is *looked at*, never about what
exists — and without this the row degraded to the unknown-version placeholder, which reports
`IsOnServer: true` and would have had the save write a dependency on a version the repo does not
hold. The index is still rebuilt from the catalog every compose rather than accumulated, which is
what keeps a rescan able to take a deleted file back out of it.

**One search box, over both lists.** It sits above the two columns rather than in the left one's
header, because a mod is only ever on one side: a box that reached only the left list answered
half the question, and the half it could not answer was "is this already in the profile?".
`ProfileModRowViewModel.Matches` delegates to its `Item`, so both sides answer the same question
the same way and the answer follows the version selector. 

Each header then reads **"N of M mods"** while a search is narrowing it — a count that only ever
said "412 mods" could not distinguish a search that found nothing from an empty list. The right
list gains a second empty-state message for the same reason: with the search reaching it, "nothing
in this profile yet" would be a lie for a two-thousand-mod profile with no match.

#### The left list is about versions, not mods

The hide rule is **"this version is what the profile pins"**, not "this mod is pinned". A new
version of a pinned mod is not in this profile, whatever else is — so it belongs on the left, and
under the old rule it had nowhere at all to be except that row's own version dropdown.

**The row carries its own version selector**, offering whatever the enabled chips offer of that mod
— the left side's counterpart of the right list's, and built the same way: `ProfileModRowViewModel`
wraps the shared list row and swaps it when the selection moves, which is what makes the two lists
structurally identical down to the template. Picking a version here is not yet a decision the
profile has made — nothing is committed until the row's own **+** or **⬆** is pressed — so unlike
the right list's selector, choosing a locked mod's version here never raises the confirmation; the
button click does.

**The default is the newest offered version, except on a removal.** A mod this draft has just taken
out defaults to the exact version the profile held, and that version is offered regardless of the
chips — a pending removal is draft state, not catalog state, so the chips must not be able to hide
it. Without this the row's own **+** would re-add at the newest offered version instead of undoing
the removal, which is precisely the hazard *Restore removed* exists to route around. A choice
already made on this row otherwise survives a recompose — the same "the draft outlives the catalog"
argument, one level down — so ticking an unrelated chip does not silently reset three selectors
somebody has just set.

Its action is one verb applied to the profile: **Pin** where the mod is absent, **SetVersion** where
it is already there, with the same locked-mod confirmation the version selector on the right raises —
it is the same act, so it asks the same question. **Which of the two the button shows is decided by
whether the profile holds the mod, not by whether this version is newer than the pin.** The row's own
selector reaches versions older than the pin, and versions the ordering cannot place against it at
all — the *New?* rows — and neither of those is an update while both still move the pin, so a row
reading its verb off "is this an update" showed a **+** labelled *Add to this profile* over an action
that silently changed an existing pin. ⬆ is kept for the move that genuinely goes up and a neutral
glyph carries the rest; the tooltip is the same sentence either way. The sort still reads taken out,
then updates, then versions the ordering could not settle, then alphabetical.

That does not put a mod on both sides at once, which was the original objection to updates on the
left: what is on the left is a version the profile does *not* pin, and what is on the right is the
one it does.

**The "N taken out" count is also the control that hides them.** Defaults to shown — a removal is
unsaved work, and hiding unsaved work by default is how people lose it — but clicking the count
toggles it off, the same shape as the updates band's skipped-locked count opening its own list.
Turning on the *Taken out* filter chip forces the toggle back on, since a filter that selects a set
the toggle is hiding would be an empty list with no explanation.

**A removal always has a row, whatever the chips offer.** The left list is composed from the mods the
enabled sources hold *and* the mods this draft has taken out, so a mod no enabled chip offers any
version of — take the repo chip off and the rest is whatever a folder happens to hold — still gets a
row the moment it leaves the profile. Without that the header counted a removal nothing rendered and
the *Taken out* filter selected an empty list. A bulk removal normally touches mods that already have
rows and costs only a re-offer; the full rebuild is reached when a row is genuinely missing.

#### Two kinds of chip: facts, and what you did

A row can wear two chips, and they are drawn differently so they cannot be mistaken for each other.
**A status is a fact about the version; a touch is something the draft did to the mod.** In the editor
a status is an *outline* — muted, on the trailing edge — and a touch is a *fill* with a stripe down
the row's leading edge. Filled means you did this; outlined means it is so. (The repo mods page has no
draft to tell apart from and keeps its filled chips.)

**The status chip** follows the selector: the row *is* the selected version, so the chip and the +/⬆
glyph move together when the selection does, exactly as they already do on the right when `Item` is
replaced. Its text says what the version means for **this profile**.

| Chip | Means |
| --- | --- |
| **Update** | A newer version of a mod this profile pins. Free where the repo holds it; saving imports it where only a folder does. |
| **New version** | Newer than anything the repo holds, of a mod this profile does *not* pin. An import candidate — which is what the management page calls an *Update* from its own point of view, and which is not one from here: nothing in this profile moves by taking it. |
| **New** | A version the repo does not hold, with nothing else to say. |
| **In repo** | Registered, and not pinned. |
| **Imports on save** | The review's version of *New*: a version the draft pins that saving has to upload first. |

**The version chip is not drawn in the editor.** Every row on both sides carries a selector that says
the same thing, and says it for the version the row is showing. A version that saving has to import
is starred in the selector (`1.3*`) with the words in a tooltip — a sentence in a 150px box was
clipped.

#### What the draft has done to a mod

`ProfileModTouch` is **derived, never recorded**: the saved profile's pin of a mod against the
draft's. A mod pinned at another version and pinned back is not touched, because saving it would
change nothing; a log of clicks would have called it touched twice. It applies the same rule as
`ProfileModListDiff` and `ProfileRevisionComparison` — a mod's version and the profile's own lock,
never the adapter's — and a test holds the three together.

| Mark | Fill | Means |
| --- | --- | --- |
| **Added** | green | The saved profile does not hold it. |
| **Version changed** | accent | Pinned at another version than the saved profile holds. |
| **Lock changed** | accent | Locked or unlocked in this profile since the saved one. |
| **Version & lock changed** | accent | Both. |
| **Taken out** | caution | The saved profile holds it and the draft does not. It wears the mark on the left, where it is back among the mods that were never in the profile; a taken-out mod's status chip is suppressed, since nothing is on offer of it. |

The tooltip carries the detail — *Was 1.2 in the saved profile, now 1.3* — and it is worded once, in
`ProfileModTouches.Describe`, for both lists and the review. **Ignoring is not a touch.** It is saved
with the rest, but it is an aid to editing rather than a result of it, so it never makes a row
"changed", does not count in the review, and keeps its own eye button.

#### Reviewing the draft

**Review changes (N)**, beside Save, swaps the two lists for what the draft would change: added,
changed, taken out — each row with the way it moved (`1.2 → 1.3 · locked`) and a **↺** that takes
that one change back (a mod is put out again if it was added, back in if it was taken out, and to its
saved version and lock if it was moved). The lists are hidden rather than dropped, so the search, the
selection and the scroll position are all there on the way back.

It is read **from the draft**, by the comparison the history page uses, so it is every change and only
those whatever the sources, filters and search are doing — a mod no enabled source offers is still
reviewed, because its original version comes from the version index rather than from the left list.
It is optional: a review somebody has to click through before every save is friction, and the one
consequence of a save that is dangerous, the re-apply, already has its own control.

**It is also where a save that imports is watched.** Pressing Save on a draft with something to
upload switches to the review, and each import reports on its row: *Imports on save*, then *Queued*,
*Uploading 42%*, *Imported* or a failure. A save with nothing to upload finishes before there would be
anything to watch, so it stays where it is and says what it did in a toast. What could not be imported
moves to a **Could not be imported** group at the top of the review once the save stops — not while it
runs, so the list does not reshuffle under the pointer that is watching it — and its ↺ is the way to
drop the mod and save again. A save that commits reloads and returns to the lists; an editor opened
in the middle of one lands on the review. The run's marks are held by the page and stamped onto rows
as they are built, because the review is rebuilt from the draft after a failed save and a rebuilt row
has to be told again how its import went.

#### A version nothing could compare

Planning an update already steps over a pair the comparer abstained on rather than guessing — see
[the note below](#a-note-on-update-available) — but stepping over it made the version disappear
entirely: no update, no chip, no count, just an entry in the selector sitting wherever the
topological sort happened to put it. That is the worst version to be silent about, because it may be
exactly the one somebody came here to add, and the only reason it was never offered as an update is
that the program could not tell.

**The definition is one sentence, used in five places**: a version an enabled source holds that the
ordering could not compare against what the repo holds — `ModVersionSet.CouldNotCompareToNewest`,
tested in `Client.Core`. It answers against the repo's own newest specifically, not against
whichever version this profile happens to pin, which is what makes it a single repo-level fact
usable identically whether or not the mod is pinned here — and it is exactly the pair the import-time
arbitration dialog exists to ask about, so a version this is true of is also a preview of "importing
this will ask a question".

- The selector label carries it: *"2024.03 — imports on save, order not settled"*.
- A green **New?** chip sits on the row, sharing the source-conflict chip's column — both mean "this
  row will ask you something at save", and the question mark is deliberate: the reader does not need
  to know a comparer abstained, they need to know this might be the version they came for.
- The **Conflicts** filter chip is gone; this took its slot. A source conflict is answered at save,
  one version at a time, in a dialog — nobody bulk-acts on a set of conflicts, so a filter for it
  never earned its place, and the row's own chip is the whole of what it needs. An uncompared
  version is worth isolating precisely because it is silent everywhere else.
- The updates band names a count beside the skipped-locked link: *"3 versions could not be
  compared"*, linking to the filter above. They are not counted as updates, and saying why is the
  whole job.
- It ranks in the left list's sort, after updates and before alphabetical, so the count is findable
  without opening the filter.

#### The updates band

**"7 updates available · 2 will be imported when you save"**, above the right list, counting both
kinds and saying the split — because the two cost differently: one is a pin moving and the other is
a file going up first.

**It stays on screen at zero**, and is honest there. "Are there updates?" is a question people come
to this page to answer, and a section that is absent when the answer is no never answers it — it
just leaves them looking. But an on-disk update only exists while its folder's chip is on, so with
no folder being read it says "No updates in this repo. No folders are being read." rather than
claiming to have looked.

***Update all* is a split button** carrying the same split: the primary takes everything that is
newer wherever the file is, and behind the caret is *Update the N already in the repo*, for somebody
who does not want to spend an upload right now. The caret carries its own enabled condition and
appears only where the two counts differ, unlike the save split's — a menu offering the same thing
as the button beside it is an invitation to nothing.

The existing **Updates** filter chip is the way into the list of them. No new region, and no third
copy of the rows.

The right list is keyed by `ModId` — the domain enforces one `ModDependency` per mod — so
moving a mod rightward also means choosing a version. That row needs a version selector and a
`Locked` toggle.

**The two bulk moves are split, and sit under the left list.** *Add all shown new* takes everything
on screen the profile has never held; *Restore removed* is an undo, so it puts back what this draft
took out at the version and lock the profile still holds rather than picking a default. One button
doing both would silently re-add a removal at the newest version — which is a different pin from the
one that was there.

**Adding and upgrading stay apart.** Now that an update row is on the left, *Add all shown new* and
the count on it exclude those rows: a bulk add that silently moved pins would be a different act
under the same label. A mixed *selection* does both, and says so — **"Add 12 and update 3"** — since
the set somebody assembled across several searches is theirs and splitting it would be worse than
labelling it honestly. Locked pins are left alone and counted, exactly as the batch update leaves
them.

**The bar says what it will not do, and will not pretend to.** A locked pin is left where it is by a
selection, so the button names it - **"Update 2 mods (1 locked)"** - and a selection of nothing but
locked pins reads **"Nothing to update (2 locked)"** and is *disabled*, with a tooltip saying how
to move one on purpose. It used to be an enabled "Update 1 mod" that did nothing and reported it
afterwards. What each row would do is `ProfileVersionMoves.Classify` - add, update, any
other move (an earlier version, or one the order will not place - worded as an update, since the left
list has no move verb of its own), locked, or already there - and the bar's wording and the command
both read it. Adding is never blocked by a lock, so a selection
of adds and locked pins still has something to do and reads **"Add 3 mods (2 locked)"**.

The right-hand selection bar's Update button says what it will skip in the same way - **"Update (skip 2 locked)"**, counting only locked pins that have an update to take.

The row's button is the up arrow for an update and the neutral glyph for any other move. The
profile-side update button on the right list is the same up arrow, not the refresh glyph it had.

**A chosen downgrade stays on the right.** A mod pinned below its newest version is an update on the
left as well as the right - that is right for a pin that merely fell behind. Once the user has picked
an *older* version on the right, offering the newer one back on the left is the list arguing with them,
so the mod is left out of the left list (`FindDowngraded`). It is measured against what the profile
held when the page read it, so it lasts as long as the draft: once saved, the older pin is the
profile's and the newer version is an update again, which is what a lock is for.

**The left list leads with what the draft has taken out; the right list is alphabetical unless it has been switched.** Mods
this draft has *taken out* of the profile are back on the left looking exactly like a mod that was
never in it, so they sort to the top, wear the **Taken out** mark (see
[the next section](#what-the-draft-has-done-to-a-mod)) and get a count in the header. The right list
used to lead with what could not be imported and what was pending; both are now the review's business
([below](#reviewing-the-draft)), and a list whose order never changes under the pointer is one somebody
can edit. The left re-sorts on every recount.

**The right list can be sorted by name or by date added**, with an arrow to
reverse it. Name is where the page opens and A to Z its direction; each date opens newest first, and
changing the sort always resets the direction to that sort's own default, since "descending" means
opposite things for a name and for a date. It is a way of looking rather than a setting, so it is not
remembered between visits. Ties fall back to the name, always ascending, so reversing reverses the list
rather than shuffling the mods one save added together.

- **Date added** is when the mod entered the profile or last moved to another version - the server's
  `ModDependency.Added` for a pin the server holds. A row the draft has added or moved has no server
  date yet and takes the moment the draft did it, so it is the newest thing in the list and sorts to the
  top under the default direction; putting a mod back at the version it was saved at gives its saved date
  back. Taking a mod out and adding it again is a new event. This is the one sort that moves rows under
  the pointer, which is why the name sort stays the default. A page that rejoins a save already running
  reads the dates of the revision that save started from.
There is deliberately no sort by registration date. It could only be derived - the earliest `Created`
among the versions the catalog happens to hold - and it drifts when old revisions are pruned and the
versions only they pinned are deleted; storing it would mean a per-version copy of a per-mod fact. It
is a repo-management question anyway, and *Date added* answers the one a profile editor asks.

Under the date sort each row says the date it is ordered by (*Added 3 Sep*, with the year only where it
is not this one) and carries it in full as a tooltip. Under the name sort the row has nothing extra to
say. See `ProfileModSorting`.

#### Picking mods in bulk

A profile is dozens to hundreds of mods. Building one by clicking **+** on every row, and unbuilding
one by clicking **−** on every row, is not a workflow — so both lists carry a real selection, and
every bulk action on the page is stated against **what the list is showing**.

**Selection lives on the rows, not in the list control.** `ModListItemViewModel.IsSelected` is the
flag; `ProfileModRowViewModel` forwards to the item inside it, so a mod stays picked through a
version change and reads the same on both sides. It is not the `ListBox`'s own selection, and that is
the whole point: a list control drops from its selection whatever the collection view filters out, so
binding to it would mean **one more character in the search box silently discarding the set being
assembled**. Carrying a selection across several searches — narrow, pick, clear, narrow again, act on
the union — is precisely what makes the selection worth having.

The consequence is that some picked rows are off screen, and the page must never quietly act on rows
nobody can see. So everything is counted twice, against the rows and against the view:
`ModListSelection` reports **"47 selected, 12 of them are not shown"** and offers **Deselect hidden**
next to it. The bar is worded as a fact rather than a warning — those rows were picked on purpose —
and its only job is to make sure the count on the button is never a surprise.

`ListSelection` (an attached behaviour) turns gestures into calls on that selection: click,
ctrl-click, shift-click a range in view order, arrow keys, shift-arrow, `Ctrl+A`, space, Escape,
Enter and double-click to move. The `ListBox` keeps `SelectionMode="Single"` and its selection means
only **which row is current** — rendered as a focus ring, never as a fill. That is the same
distinction Explorer draws between the focus rectangle and the picked set, and it exists for the same
reason: the arrow keys have to be able to move without that meaning the set has changed. A press on a
row that is *already* picked defers its click to the mouse-up, because that press is also how a drag
of the whole selection starts.

**Filter chips compose with the search** rather than replacing it, which is what keeps "everything
shown" a single well-defined set for the counts, the bulk buttons and the header's three-state box to
be stated against. They are deliberately few, and each names a set somebody would want to act on all
of — a filter nobody would bulk-move is a filter that earns nothing.

**Both directions cost the same.** *Take out everything shown* is the mirror of *Add all shown new*,
and the right-hand selection bar carries Update, Lock and Unlock beside it. A page where adding forty
mods is one click and removing forty is forty clicks has not solved the problem, it has picked a
side.

**The right list's *Not in the sources* chip is the mirror of the left list's diff view**, and the
half of it that was missing: the mods this profile pins that no enabled source offers any version
of. With another profile as the only enabled source, that is exactly what this list holds and that
one does not — and with *Take out everything shown* under it, "make this profile match that one" is
two clicks.

It is **mod-level rather than version-level**, deliberately: a mod the other profile holds at a
different version is an update, not a removal, and the left list already says so. It is disabled
while nothing at all is enabled, where it would select the whole profile and mean nothing, and the
filter falls back to *All* rather than staying checked on a chip that has just gone dead.

**Every bulk move is undoable, and the undo is the whole draft.** `RunBulk` snapshots the pins,
runs the change, and offers what it turned out to do — "Added 47 mods", counted after the fact,
because how many were skipped as already-present is only known once it has run. A snapshot rather
than a reverse replay means one mechanism covers adds, removals, a copied list and a batch of version
changes alike, and cannot half-succeed. The offer retires itself: **any** subsequent change clears it
(`Recount`), because the draft it holds stops being an undo the moment something is built on top of
it, and a 15-second timer catches the rest.

**Two ways of not picking mods one at a time at all**, which between them beat any selection UI for
the cases they cover:

- **Copy from a profile** takes another profile's list at its head — *add what is missing* by
  default, *replace* as a deliberate second choice, since the two read almost the same in a sentence
  and are very different in effect. Nothing is written until Save, so even the destructive one is
  recoverable by discarding, by the undo, or by not saving.
- **Paste a list** takes text off a forum post or a modpack manifest. `ModListPaste` is forgiving
  about shape — bullets, numbering, quotes, commas — and the matching that follows is *exact*, ids
  before names: a fuzzy match would quietly pin the wrong mod, and "3 not found: Foo, Bar, Baz" is a
  far better outcome than three plausible mistakes nobody notices until the game does. It **selects
  and reports; it never adds**. Somebody else's list is a suggestion, and the step between reading it
  and committing to it is exactly where a person wants to see what matched, what is already here and
  what is missing.

Dragging between the panes is offered too, and is never the only way to do anything: a two-pane drag
is undiscoverable, awkward over a long list and impossible without a pointer. The payload is only the
name of the side it started on — what moves is whatever that side has selected, which the view model
already knows — which makes the rule for a valid drop simply that the two sides differ.

#### The left list can hide what is ignored

The left list is mostly noise for anybody with a large folder or a large repo: things that will never
be in this profile, and other versions of mods whose pin is not going to move. Two things are hidden
by default and shown together by one **eye toggle** beside the list's count:

- **Ignored mods** — mods somebody ignored in this profile (`ProfileIgnoredMod`, see
  [02 — Ignored mods](02-domain-model.md#ignored-mods)). The row's own crossed-out eye ignores it,
  the open eye on a shown row stops ignoring it, and the selection bar has a bulk **Ignore** and
  **Stop ignoring** that say how many of the picked rows they will take.
- **Other versions of a locked pin.** A mod the profile pins *and holds in place* offers no update
  that the profile is going to take, so its other versions are ignored for as long as the lock
  stands. Nobody decided this, so the row's eye is greyed and says why; unlocking the pin turns the
  row back into the update it is.

**The toggle is a filter, not a source.** It narrows what the enabled sources offer and never adds a
row they did not, so it composes with the search and the filter chips and sits with the count rather
than in the source chips. Its count is taken against everything else the list applies, so it says how
many rows clicking it would reveal; and the list's own total leaves ignored rows out while they are
hidden, because hidden by a toggle is not hidden by a search.

**A pinned mod is never ignored, and the draft decides.** `ProfileIgnoring.Classify` answers the pin
first: a mod the server lists as ignored and this draft has pinned is an ordinary update row until a
save says otherwise, which is what lets discarding undo the pin without the ignore list having moved.

**Ignoring is part of the draft, and is saved with it.** The eye edits a list on the page, which counts as an
unsaved change, is reverted by Discard, and is written by Save - as the whole list, to its own route, *after*
the revision and only if the revision was written. A failed ignore write does not undo the revision; the
save says which half did not land. A pin never mutates the list: what is written is the ignored mods minus
the draft's pins (`ProfileIgnoring.WithoutPinned`), so pinning an ignored mod and then discarding puts it
back.

**A save that only changes what is ignored** mints no revision, imports nothing, does not re-apply the
profile even where it is the active one, and does not check for drift - nothing a folder was built from
has moved. Its button reads *Save changes* rather than *Save and apply*, and the *Save only* variant is
not offered (`WillApply`).

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

#### A save is a gesture, not a page

The whole of it — the import, the revision, the re-apply, the drift check — is `ProfileSaveService`,
sibling to `ProfileApplyService` and `ModImportCoordinator` and shaped like them. It claims the
**profile** exclusively, with the repo lease the import already takes underneath it; it owns the
gesture's strip entry and its Cancel; it raises the stale-revision and import-problem dialogs through
the shell's modal host rather than through a page that may be gone by the time they are needed; and
it reports progress **per version** rather than writing into row view models it does not own.

It had to move. A save belonged to the page that started it, so navigating away disposed the editor
and its catalog mid-flight: the files finished registering on the background strip, the revision was
never written, and nothing said so. Phase 13's rule is what settles where the claim goes — a save
writes a revision of one profile and imports into one repo, and both of those outlive whatever
started them.

**A save writes what was on screen when Save was pressed.** The request carries the desired pins,
the baseline, the revision, the label and the pending versions, all taken before the import starts
rather than read back out of the draft after it. Before that, a row added during an upload was saved
without having been imported, and a source toggled during one replaced the draft with the server's
own list — which the save then wrote back as a revision that changed nothing. A scan target arriving
from a drift notice mid-save now defers its recompose until the save is over.

**Coming back rejoins the save in progress.** An editor built for a profile that is being saved asks
the service before it asks the server: it draws the draft the service is holding — the server has
not been told about it yet — opens on the review with its rows marked from the run's own progress, and
stays read-only until it finishes, at which point it does the post-save reload it would have done anyway. What is retained
is the draft, not the page instance: keeping the view model alive would need a show/hide lifecycle
the page has never had, since the notice suppression, the catalog, the games subscription and the
navigation lock are all acquired on construction and released on dispose, and a retained page holds
every one of them while somebody is three screens away. The price is the scroll position, the
selection, the undo bar and the version description.

**A save that finished while you were elsewhere says so.** The strip is enough while it runs. Once
it is over with no editor there to show the summary, the outcome goes to the notice column — a
failed import above all, which was previously a modal raised by a page that no longer existed and
therefore a modal nobody ever saw. The next editor for that profile takes the outcome back and shows
it as its own summary, so it is said once.

**The editor is read-only while its own profile is being saved, per control rather than per list.**
`IsEnabled` on a `ListBox` stops the mouse wheel along with everything else, so the flag binds to
what can change the draft: the row buttons, the selection checkboxes, the version selectors, the
lock toggles, the review's ↺, drag-and-drop and the source chips. The lists, their scrolling and the mod name that
opens the details dialog stay live — reading is not writing — and so do the search and the filter
chips, which change the view and nothing else.

**Another profile in the same repo stays editable, and only its save waits.** The refusal is exactly
as wide as the lease. A draft with nothing pending is a revision write and is safe beside any
import, so it saves; a draft with mods to import is greyed with the reason named — "3 mods here need
importing, and 'Co-op' is already importing into this repo". Being unable to write for a few minutes
is not a reason to be unable to think for a few minutes.

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

**One padlock per row.** The right-hand list used to draw two of the same glyph a few pixels apart -
the shared row's, for the adapter's *version-sensitive*, and the toggle's, for the user's own lock -
and they were read as one thing. The shared row's is now off on that list (`ShowAdapterLock`), and
the toggle says what holds the pin: an open padlock while nothing does, a closed one while something
does, and the accent on it while the *adapter* is what does, since unticking cannot release that. The
left list keeps the shared row's padlock, where there is no toggle to carry it.
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
the game's mod folder, another in Downloads. A worked example, mod A:

| Version | State |
| --- | --- |
| v1 | registered, sequence 0 |
| v4 | registered, sequence 1 |
| v2 | unregistered, in the game's mod folder |
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

On the old management page, "this local version is not registered yet" was the right
definition, and it needs no version-string parsing at all — a local version either has a server
counterpart or it does not.

That is separate from the profile editor's question — whether anything the enabled sources hold of a
pinned mod comes *after* what the profile pins — which depends on how versions are ordered. See
[02 — Domain model](02-domain-model.md#version-ordering): ordering derives from the version string,
via a comparer the adapter supplies, and once the repo has settled it, it is stored in
`SequenceNumber` rather than recomputed on read.

**An update is an update whether or not the repo has it yet.** The editor plans against the derived
partial order over registered and unregistered versions together, not against the registered half
alone. The version somebody who has just downloaded a mod came here to find is precisely the one the
repo has not registered, and it used to appear in exactly one place: that row's own version
dropdown, as "1.2.0 — imports on save". It was in no count, raised no affordance, and did not match
the **Updates** filter.

Two rules keep that from becoming a guess:

- **The repo still settles ordering.** Registered versions keep their `SequenceNumber`, which is
  handed to the derivation as fact and never re-derived. Clients on different adapter versions
  recomputing it would disagree about what an update is, which is the one thing a batch action must
  not do.
- **An abstention is not an update.** An unregistered version counts only where the partial order
  places it unambiguously after the pin. A topological sort has to put every version *somewhere*, so
  two versions the comparer could not place still come out one after the other — and treating that
  accidental adjacency as "newer" is how a possible downgrade gets offered as an update. Offering
  one is worse than saying nothing. The pairs the comparer abstained on are therefore kept beside
  the order rather than discarded, which is what makes the question answerable at all.

This is the same rule the old management page applied against the repo's own newest,
arrived at from the other end.

**An abstention against the repo's newest is not nothing, though — it is a question of its own.**
Stepping over an uncomparable pair correctly keeps it from being counted as an update, but the first
version of this left it invisible *as such*: no update, no chip, no count, just an entry in the
selector wherever the topological sort happened to place it. `ModVersionSet.CouldNotCompareToNewest`
names that third case directly, and the editor surfaces it as its own thing — a **New?** chip, a
label suffix, a filter, and a count — precisely because it may be the version somebody came here to
add, and the only reason it was not offered as one is that the program could not tell. See
[above](#a-version-nothing-could-compare).

The two positions reconcile: an earlier design pass argued against parsing version strings at
all, because `modDesc/version` is free-form. The settled design parses **best-effort and
abstains** — where the strings genuinely do not decide the order, such as `v1` against `1.0`,
the user arbitrates once in a batched dialog and the answer is persisted repo-wide. Parsing
where it is safe, refusing where it is not.
