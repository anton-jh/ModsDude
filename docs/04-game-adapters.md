# Game adapters

The adapter layer is what makes ModsDude game-agnostic. Everything that knows what a mod
file looks like, where a game keeps its mods, or how to read a mod's name lives here — on
the **client**. The server stores an adapter id and an opaque configuration blob and never
looks inside either.

Adapters live in `ModsDude.Client.Core/GameAdapters/`.

## The three stages

An adapter is not one object. It is a chain of three, each produced by adding a layer of
settings to the previous one:

```
IGameAdapter              catalogue entry — id, display name, what it can do
   │  .WithBaseSettings(repo's AdapterConfiguration)
   ▼
IBaseGameAdapter          hydrated with the repo-wide settings, shared by everyone
   │  .WithInstanceSettings(instance's settings)
   ▼
IInstanceGameAdapter      bound to one game installation on this machine
```

The reason for the split is the repo/instance divide from
[01 — Overview](01-overview.md):

- **Base settings** are stored on the server in `Repo.AdapterData.Configuration` and are the
  same for every member. Things the group agrees on — for Farming Simulator, the `GameVersion`
  the repo targets, which is also what its [instance scope](#instance-scope) keys on.
- **Instance settings** are per-machine and never leave it. For Farming Simulator, the path
  to the game data folder.

You can go from any stage to the next, and each stage is a subtype of the previous
(`IInstanceGameAdapter : IBaseGameAdapter : IGameAdapter`), so an instance adapter still
answers `DisplayName` and still exposes its base settings.

## Capabilities

Not every game supports every feature. Rather than a fat interface full of methods that
throw, capabilities are **optional factories fetched by type**:

```csharp
var modAdapter = repo.Adapter.GetBaseCapabilityAdapterFactory<IBaseModAdapter>()?.Invoke()
    ?? throw UserFriendlyException.RepoNoModSupport();
```

`null` means "this game does not do that". Two capability families exist today:

| Capability | Base stage | Instance stage |
| --- | --- | --- |
| Mods | `IBaseModAdapter.GetModsFromFolder(path, ct)` | `IInstanceModAdapter` — `GetInstalledMods(ct)`, plus `ModFolder`, `GetModFilePath` and `GetInstalledModPath` |
| Savegames | `IBaseSavegameAdapter.CanCreateSlots` | `IInstanceSavegameAdapter` — `GetSlots(ct)`, plus `GetSlotPath`, `CreateSlot` and `BelongsInPackedSave` |

`IBaseGameAdapter` carries `CanSupportMods` / `CanSupportSavegames` booleans for the UI to
consult before offering a feature, so a page can grey out an option without constructing an
instance adapter to find out. They sit on the **base** stage, not the catalogue stage, because
the answer can depend on how a repo configured the adapter: for a scripted adapter, one script
implements savegames and another does not. That is the same layering mistake as keying instances
on the adapter id, one stage further up; see [Instance scope](#instance-scope).

The capability adapters mirror the same base-then-instance shape: `IBaseModAdapter` can scan
an arbitrary folder, and `WithInstanceSettings` turns it into an `IInstanceModAdapter` that
knows the folder the game actually uses.

The instance stage is where the **write side** lives, and it is deliberately only paths:

```csharp
string ModFolder { get; }                                   // no two instances may own the same one
string GetModFilePath(ModKey modId, ModVersionKey versionId, ModFileName? fileName);
string GetInstalledModPath(LocalMod installed) => installed.FilePath;
```

There is no `InstallMod` taking a stream. Materialising is a hardlink on one disk and a copy on
another, and that decision depends on the store assignment and the filesystem rather than on the
game — so it belongs in the sync engine, once, instead of in every adapter. Adapters supply
paths; the engine performs the filesystem operations. `GetInstalledModPath` is separate from
`GetModFilePath` because what is already on disk is not necessarily where this adapter version
would put it.

`fileName` is what the repo registered the file as being called, and an adapter that honours it
gives every member the folder the importer had rather than one renamed to the normalized id. It
is already checked to be a bare name belonging to `modId`, so an adapter uses it as it stands and
falls back to the id only where it is null. See
[09 — Mod catalog](09-mod-catalog.md#the-other-half-normalizing-the-id-must-not-rename-the-file).

## What the sync engine and registration read off an adapter

```csharp
bool SupportsHardlinks { get; }   // on IBaseModAdapter, default false
```

Whether the game's mod files are safe to hardlink into the content store. False when the game
or its updater may **rewrite a mod file in place**, which through a hardlink would corrupt the
store blob shared with every other repo and instance on that volume. False also means "nobody
has checked yet" — it is deliberately the default, because the failure is silent and the
blast radius is every repo on the disk. `_farming_simulator@1` declares `false` explicitly, to
say the answer is unknown rather than assumed. See
[07 — Mod sync design](07-mod-sync-design.md#hardlink-support-is-an-adapter-property).

The adapter is also responsible for deciding whether a newly registered mod is
**version-sensitive**, setting `LocalMod.Locked` — which becomes `ModVersion.Locked` at
registration. A Farming Simulator map mod declares its maps in `modDesc`, which the adapter is
parsing anyway. It re-derives the answer
from each file rather than inheriting it, which comes out consistent because every version of a
map mod declares maps. It never sets `ModDependency.Locked`; profile-level locking is a human
decision. See [02 — Domain model](02-domain-model.md#locking-in-two-places).

## Version ordering

Deciding whether one mod version is newer than another is game knowledge, so it belongs here.
But most games write versions the same handful of ways, so it is an **optional override** with
a working default rather than something every adapter must implement:

```csharp
public interface IGameAdapter
{
    // ...
    IModVersionComparer VersionComparer => DefaultModVersionComparer.Instance;
}
```

A default interface member, so an adapter that says nothing gets the shared parser — dotted
numerics of any depth, optional `v` prefix, zero-padded segments, pre-release suffixes — and one
whose game does something unusual replaces it wholesale.

The contract is **three-way and allowed to abstain**: ordered, equal, or *cannot compare
confidently*. Abstaining is a normal outcome, not a failure. A game whose versions are dates,
or build numbers, or anything the shared parser would mangle, overrides; an adapter that
overrides is still expected to abstain rather than guess.

Where `DefaultModVersionComparer` draws that line is worth knowing, because it is not "abstain
whenever the notation differs": **numbers decide wherever both strings carry them, and notation
only gets a vote once the numbers tie.** So `v1.2` against `1.3` orders, while `v1` against
`1.0` does not — there the numbers agree once the trailing zero is padded away, leaving only a
change of notation, and an author who changes notation is as likely to have done it for the next
release as for a rewrite of the same one. Leading segments of wildly different magnitude — a
date-like `2024.03` beside a semantic `1.4` — are two schemes rather than two thousand releases,
so those abstain too; one digit of difference stays decidable, because that is `9` against `10`.

**Ordering is computed client-side and sent to the server.** The server has no adapters and
cannot parse a version string — see [Where the comparison runs](#where-the-comparison-runs)
under *Adding a new game*.

## Adapter identity and versioning

```csharp
public readonly record struct GameAdapterId(string Id, int CompatibilityVersion)
```

Serialized as `id@version` — `_farming_simulator@1`. The `@` is reserved and rejected in the
`Id`.

**Adapters ship with the client, and the server does not validate the identifier it stores.** A
repo pinned to `_farming_simulator@1` cannot be opened by a client build that no longer carries
that adapter version, and nothing anywhere warns about it. Keep old versions shipping, or accept
that dropping one strands the repos using it. See
[01 — Overview](01-overview.md#three-consequences-worth-knowing).

The compatibility version exists so an adapter can make a **breaking change to its settings
shape** without stranding existing repos. Ship `_farming_simulator@2` alongside `@1`; repos
created against `@1` keep resolving to the old adapter, which can still deserialize its own
settings. `GameAdapterIndex` supports both lookups:

- `GetById(id)` — exact, including version. This is what `Repo` uses, because a repo is
  pinned to the adapter version it was created with.
- `GetLatestByPartialId(id)` / `GetAllLatest()` — highest compatibility version per id. This
  is what the "create repo" UI offers.

Note the leading underscore in `_farming_simulator`: built-in adapters are namespaced apart
from any future third-party ones.

## Instance scope

An instance is not scoped to a repo. One Farming Simulator installation should be configured
once and offered under every Farming Simulator repo you belong to, which is why instances move
out from under repos. The obvious key for that is the adapter id — and it is not quite right,
because **one adapter can serve more than one game**.

Farming Simulator 22 and 25 read the same `modDesc.xml` out of the same kind of archive and
differ only in where the folder is and which mods belong in it. One adapter handles both. But an
FS22 install and an FS25 install are not interchangeable sync targets: offering one under the
other's repo points a profile at the wrong folder and fills it with the wrong mods. A generic
scripted adapter — one Lua adapter driving a dozen games from a script the repo supplies — makes
it starker, since every one of those games reports the same `GameAdapterId`.

The key is therefore not the adapter but **the identity of the game the adapter is configured
for**, and base settings are what configure it. So `IBaseGameAdapter` produces it:

```csharp
public interface IBaseGameAdapter : IGameAdapter
{
    // ...
    InstanceScope Scope => new(Id.Id);
}
```

A default interface member, the same pattern as `VersionComparer` above: an adapter serving one
game says nothing and gets the adapter id alone. One serving several overrides.

```csharp
// FarmingSimulatorBaseGameAdapter
public InstanceScope Scope => new(Id.Id, BaseSettings.GameVersion switch
{
    { } gameVersion => gameVersion.ToString().ToLowerInvariant(),
    null => throw new InvalidOperationException("...")
});
```

`InstanceScope` is a record struct over `(AdapterId, Discriminator?)`, rendering as
`_farming_simulator#fs25`, or plain `_farming_simulator` where there is no discriminator. It is a
type rather than a bare string because `_farming_simulator#fs25` and `_farming_simulator@1` are
both plausible-looking strings, and comparing the wrong pair fails as a **silently empty instance
list** rather than as a compile error — the same reason `GameAdapterId` exists.

Note `Id.Id`, not `Id`: **the compatibility version is deliberately not part of the scope.** A
repo on `_farming_simulator@2` still matches instances created under `@1`, which is the standing
rule that a newer adapter must be able to read settings authored by an older one.

### What it changed

| | Keyed on the adapter | Keyed on the scope |
| --- | --- | --- |
| Persisted on the instance | `GameAdapterId` | `InstanceScope`, plus the `GameAdapterId` that authored the settings |
| A repo offers | instances whose adapter `Id` matches | instances whose scope equals `Adapter.Scope` |
| Farming Simulator base settings | empty | `GameVersion`, required, not modifiable |

Everything downstream is unchanged. The sidebar still lists instances under each repo,
activation eligibility is still an equality test, and `CreateLocalInstancePage` still renders
`GetInstanceSettingsTemplate()` from the repo it was opened under. Only the value being compared
is different.

### Two rules for the discriminator

Neither is cheap to enforce in code, so they are written down here instead.

**Only base-settings fields that lack `[CanBeModified]` may feed it.** Those are the identity
fields — the attribute's whole meaning is that a game path can change and a game identity cannot.
An adapter deriving its scope from a modifiable field turns an admin editing base settings into a
silent orphaning of every instance on every member's machine.

**A scripted adapter takes the discriminator from inside the script, not from the reference to
it.** Two repos pointing at the same Lua script by different paths or URLs must land on the same
scope, so the script declares its own game id. Keying on how it was referenced produces
accidental non-sharing that looks exactly like a bug.

### Consequences

**Game identity is immutable, so an FS22 repo cannot become an FS25 repo.** That follows from the
first rule, and it is the right answer — the mods are different files, so it is a new repo rather
than an edit — but treat it as a decision rather than a side effect. If it is ever wanted, it is
an admin-level *re-scope repo* operation that has to re-point or orphan every member's instances,
not a field on a form.

**This is not what compatibility versions are for.** `@2` means the adapter's settings shape
broke and existing repos stay on `@1`. FS22 and FS25 coexist indefinitely and neither succeeds
the other; conflating the two axes would strand every FS22 repo the day FS26 ships.

**Scope resolution can become asynchronous.** A scripted adapter cannot report a scope until the
client holds the script, so a repo whose script has not been fetched cannot list its instances
yet — and `Repo` hydrates its adapter synchronously in its constructor today. Farming Simulator
resolves synchronously, so nothing is blocked on this now. It is the one place where a generic
adapter costs more than an override.

**Folder collision has to be checked globally.** Adapter scope was what stopped two instances
claiming the same directory; splitting it by game reopens the possibility, since two scopes can
name the same folder. The check runs across all instances regardless of scope, using
`IInstanceModAdapter.ModFolder` — and the answer is also recorded on the persisted instance, so
an instance whose scope no repo on this machine serves still participates in the check even
though it cannot hydrate an adapter to be asked.

**The instance settings template genuinely varies with base settings now.**
`FarmingSimulatorInstanceSettings` used to probe `My Documents\My Games\FarmingSimulator2025`
with the year hardcoded; `CreateTemplate(gameVersion)` now probes for the year the repo actually
targets, trying both spellings the installer has used. The shape does not change, only a default
value — which is worth noticing, because the
mechanism gets exercised by the dullest possible case before anything exotic depends on it.
`GetInstanceSettingsTemplate()` and `DeserializeInstanceSettings()` have always been on
`IBaseGameAdapter` rather than `IGameAdapter`, so the interface allowed this all along; nothing
used it.

## Registration

`AddGameAdapters(assembly)` reflects over the assembly and registers every non-abstract type
that implements `IGameAdapter` **but not** `IBaseGameAdapter`:

```csharp
.Where(x => !x.IsAbstract
         && x.IsAssignableTo(typeof(IGameAdapter))
         && !x.IsAssignableTo(typeof(IBaseGameAdapter)))
```

That exclusion is what stops the hydrated stages from being picked up as catalogue entries —
`FarmingSimulatorBaseGameAdapter` inherits from `FarmingSimulatorGameAdapter`, so without it
the index would contain duplicates. `GameAdapterIndex` throws at construction if two adapters
share an id, so a duplicate is a startup failure rather than a mystery later.

Adding an adapter class to `ModsDude.Client.Core` is therefore the entire registration
step — there is no list to update.

## Dynamic forms

Adapters need settings UI, but the UI project must not know about individual games. The
answer is `DynamicForm`: a settings class that describes itself through attributes, which
the WPF layer renders generically via `DynamicFormEditor`.

```csharp
public class FarmingSimulatorInstanceSettings : DynamicForm<FarmingSimulatorInstanceSettings>
{
    [Required, CanBeModified, Title("Game data folder"), FolderPath]
    public string? GameDataFolder { get; set; }

    protected override IEnumerable<DynamicFormValidationError<FarmingSimulatorInstanceSettings>> PerformValidation()
    {
        if (!Directory.Exists(GameDataFolder))
        {
            yield return new("Folder does not exist.", nameof(GameDataFolder));
        }
    }
}
```

| Attribute | Effect |
| --- | --- |
| `[Title("...")]` | Label shown; falls back to the property name |
| `[Required]` | Marks the field as mandatory |
| `[CanBeModified]` | Whether the field is editable after creation — a game path can change, a game identity cannot |
| `[FolderPath]` | Renders a folder picker rather than a text box |

The base class provides:

- `Validate()` / `EnsureValid()` — the latter throws, and is called at every deserialization
  boundary so an adapter never hands out a half-valid settings object.
- `Copy()` — a reflection-based property copy. The editor edits a copy so that cancelling
  navigation leaves the live settings untouched.
- `Serialize()` — `JsonSerializer.Serialize(this, GetType())`, passing the runtime type so a
  subclass serializes fully.

`DynamicFormValidationError<TForm>` binds an error to one or more `PropertyInfo` on the form
and validates at construction that the properties actually belong to that form — a typo'd
property name is an exception, not a silently ignored error.

Constructors can seed sensible defaults. `FarmingSimulatorInstanceSettings` probes
`My Documents\My Games\` for both `FarmingSimulator2025` and `Farming Simulator 2025`,
because the installer has used both spellings and neither is derivable from the other.

## The Farming Simulator adapter

`GameAdapters/Implementations/FarmingSimulatorV1/`. This is the reference implementation and
the only one that exists.

| Type | Role |
| --- | --- |
| `FarmingSimulatorGameAdapter` | Catalogue entry, `_farming_simulator@1` |
| `FarmingSimulatorBaseGameAdapter` | + base settings, exposes base capability factories |
| `FarmingSimulatorInstanceGameAdapter` | + instance settings, exposes instance capability factories |
| `FarmingSimulatorBaseSettings` | `GameVersion` (FS22 or FS25) — required, not `[CanBeModified]`, and what feeds the [instance scope](#instance-scope) |
| `FarmingSimulatorInstanceSettings` | `GameDataFolder`, auto-detected for the repo's `GameVersion` |
| `FarmingSimulatorBaseModAdapter` | Scans a folder of `.zip` mods. Declares `SupportsHardlinks => false` |
| `FarmingSimulatorInstanceModAdapter` | `{GameDataFolder}/mods` — scans it, and answers where a mod file belongs in it |
| `FarmingSimulator*SavegameAdapter` | Twenty fixed `savegameN` slots under `{GameDataFolder}`, each named and described from its own `careerSavegame.xml` and `farms.xml` — see [How a savegame is described](#how-a-savegame-is-described). `CanCreateSlots => false` |

### How a savegame is described

`SavegameSlot.Details` is an **adapter-supplied list of `(Id, Label, Value)`** — free-form, in the
adapter's own order, already worded for a person to read. Nothing above the adapter knows what a
map is, which is the point: the games do not agree on what is worth saying about a save, and a
schema with a column per game is a schema with a hole in it for every game nobody has written yet.

`Id` is stable, lowercase and never rendered; `Label` is prose and safe to reword. That split is
what makes a fact findable later — if one turns out to be worth promoting to a real property, the
recorded values can be migrated rather than parsed back out of a sentence. **Nothing may depend on
one**, exactly as with `ModAttribute`.

**The order is the priority order.** A slot row shows the first few and puts the rest on its
tooltip, so an adapter that says the most useful thing first gets the most useful row.

What the Farming Simulator adapter reads, from `careerSavegame.xml` unless noted:

| Id | From |
| --- | --- |
| `map` | `settings/mapTitle` — the title, not `mapId` |
| `last-played` | `settings/saveDate`, falling back to the career file's own timestamp |
| `started` | `settings/creationDate` |
| `playtime` | **`statistics/playTime`**, in minutes, rendered as hours past the first one |
| `money` | `statistics/money` |
| `difficulty` | `settings/economicDifficulty`, un-shouted |
| `multiplayer` | `farms.xml` — distinct `player/@uniqueUserId` across every farm |

Two of those are worth knowing about. **Playtime is under `statistics`, not `settings`**, which is
where this used to look — so every slot silently reported no playtime at all until it was checked
against a real save. And **multiplayer is a heuristic**: the career file records nothing about it,
so it is counted from the players who have connected to each farm. More than one is a save that has
been shared; one is a save somebody has only ever played alone. It cannot tell a game that was
hosted and never joined from a singleplayer one, and the wording does not pretend to.

**The adapter is handed an `ILoggerFactory`.** It is a DI singleton like every other `IGameAdapter`,
and it passes the factory down to the mod and savegame adapters its capability factories build - so
the capability lists are instance fields rather than static ones. That matters here more than
anywhere: every read in this file degrades rather than throws, and a degraded slot is
indistinguishable on screen from an ordinary one. A career file that will not parse is a `Warning`;
a field that is merely absent says nothing, and one that is present and unreadable is a `Debug`,
because the difference between "this save records no playtime" and "this adapter no longer
understands the layout" is exactly what a silent fallback hides.

Every line is optional and independent. A field this adapter cannot read costs that line and
nothing else, and a career file that will not parse at all still leaves the slot **occupied** — the
one mistake that matters, since an empty slot is the one the engine writes into without asking.


### How a mod is read

A Farming Simulator mod is a zip containing `modDesc.xml`. `GetModsFromFolder` opens every
archive in the folder and extracts:

| From | To |
| --- | --- |
| Filename without extension | `LocalMod.Id`, a `ModKey` — this becomes the server's `ModId` |
| `modDesc/version` | `LocalMod.Version`, a `ModVersionKey` — becomes `ModVersionId` |
| `modDesc/title/en` (or first child, or filename) | `Name` |
| `modDesc/description/en` (or first child) | `Description`, normalised |
| `modDesc/author` | `Author` |
| Whether `modDesc` declares maps | `Locked` — the mod is version-sensitive |

**Three outcomes, and only one is a fault.** A mod is a `.zip`, so anything else in the folder is
not a mod that failed to read — it is not a candidate, and is ignored without being opened. A mod
folder legitimately holds files that are none of the app's business: Farming Simulator keeps a
`mods.json` beside the archives. A zip that opens and carries no `modDesc.xml` is a zip that is not
a mod, which is a determination and equally silent. Only an archive that will not open, or whose
`modDesc` will not parse, is worth reporting — in a mod folder that is a mod which has silently left
the catalog — and even then the scan skips it rather than letting one bad archive take a thousand
good ones down.
| `modDesc/iconFilename`, or any `icon_*` image | `Icon` |
| Any `store_*` image | `Images` |

Several details in this code are load-bearing and worth preserving if you touch it:

- **The mod id is normalized here, where it is produced**, not at each use site — which is how
  one gets missed. `ModKey.From` is the only way to build one. Before it, a mod found as
  `FS25_Foo` and `fs25_foo` was one file on disk and two mods on the server.
- **Parallelism is capped at `Environment.ProcessorCount`.** Each archive gets its own handle
  so parallel reads are safe, but the work is disk-bound — a handful at a time is as fast as
  hundreds, and one work item per file would hand the whole shared thread pool to the scan.
- **Results are written into an indexed array**, not a concurrent collection, so the output
  keeps folder order.
- **XML is read with `DtdProcessing.Prohibit` and `XmlResolver = null`.** Mod archives are
  untrusted input; this closes off XXE and entity-expansion attacks.
- **Icons are matched by name, not extension.** `modDesc.xml` routinely declares `.png` when
  the archive ships `.dds`, so an exact lookup fails and the code falls back to comparing
  names with the extension stripped.
- **Descriptions are re-indented.** They arrive as CDATA, so every line carries the
  surrounding element's indentation. The normaliser strips the common indent and collapses
  runs of blank lines.
- **Images are lazy and identified by a stable cache key** —
  `{modPath}|{entryName}|{length}|{crc32}`. The archive handle is closed before the image is
  ever needed, so `ModImage.Load` reopens the file. The CRC in the key means the key
  changes whenever the underlying file does, which is what makes the disk image cache safe.
- **An unreadable archive is skipped, not reported**, and its file handle is closed. Both
  matter once a source can be any folder the user points at: `ZipArchive` checks the central
  directory lazily rather than in its constructor, so a malformed archive used to fail the whole
  source scan, and `leaveOpen: false` only starts applying once the archive exists, so a throwing
  constructor leaked a handle per non-zip — which in a Downloads folder is most of the files.
  Both surfaced the first time a real Downloads folder was scanned.

## Adding a new game

1. Create a folder under `GameAdapters/Implementations/{Game}V1/`.
2. Write `{Game}BaseSettings : DynamicForm<{Game}BaseSettings>` and
   `{Game}InstanceSettings : DynamicForm<{Game}InstanceSettings>`, annotated with `Title`,
   `Required`, `CanBeModified`, `FolderPath` as appropriate, with `PerformValidation`
   overridden where a value can be wrong in a way attributes cannot express.
3. Write the three adapter stages. The Farming Simulator trio is the template; the
   inheritance chain (`Instance : Base : Catalogue`) is what makes the stage subtyping work.
4. Implement `IBaseModAdapter.GetModsFromFolder` to produce `LocalMod` records, and
   `IInstanceModAdapter.GetInstalledMods` to point it at the game's actual mod folder.
   Respect the cancellation token — `ModCatalog` cancels the scan when the page goes away.
   Build every id through `ModKey.From` / `ModVersionKey.From`.
5. Implement the write side — `ModFolder` and `GetModFilePath` — or sync has nowhere to put a
   file.
6. Register the capability factories in the base and instance adapters' `_capabilities`
   lists.
7. Nothing else. Registration is by reflection, and the UI is driven by the dynamic forms.

Set `CanSupportSavegames = false` unless you implement it — the flag is what the UI checks
before offering the feature. Implementing it means `GetSlots` above all: a slot list rather than a
slot *count*, so that a game with twenty numbered folders and one with freely named saves are the
same model, and nothing above the adapter ever learns a number. Read each occupied slot far enough
to name it the way the game does; a picker that shows `savegame3` is the memory test the feature
exists to remove. A slot you cannot read is **occupied and unnamed**, never empty — empty is the one
the engine overwrites without asking.

Leave `SupportsHardlinks` alone unless somebody has actually tested what the game's updater does
to a mod file. The default is `false` because the failure is silent and takes every repo on the
volume with it.

Override `VersionComparer` only if the shared parser genuinely cannot read your game's version
strings. Most cannot benefit from an override, and an incorrect one silently mis-orders
releases.

Override `Scope` only if the adapter serves more than one game — see
[Instance scope](#instance-scope). The base-settings field it reads must not be
`[CanBeModified]`.

### Where the comparison runs

Worth understanding before you write an adapter that overrides ordering: **adapters are a
client-side concept and the server has none.** `AdapterData.Configuration` is an opaque string
the server never parses, and that opacity is exactly what lets a new game ship without a server
deployment.

So version comparison cannot happen inside `RegisterMod`. The client compares using its own
adapter, works out where the new version belongs, and **sends the position with the
registration** — appended, or inserted before a named existing version. The server validates and
stores it, and `SequenceNumber` remains the shared, persisted ordering that every member reads.

Two consequences:

- The ordering a repo has is the ordering whichever client registered each version computed.
  Because it is stored rather than recomputed on read, two clients running different adapter
  compatibility versions cannot disagree after the fact — the first writer settles it.
- User arbitration of an ambiguous pair works the same way: the answer is a position, sent to
  the server, and nobody is asked again.
