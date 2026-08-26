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
  same for every member. Things the group agrees on — for Farming Simulator, currently
  nothing, but conceptually the game version the repo targets.
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
| Mods | `IBaseModAdapter.GetModsFromFolder(path, ct)` | `IInstanceModAdapter.GetInstalledMods(ct)` |
| Savegames | `IBaseSavegameAdapter` | `IInstanceSavegameAdapter` — both currently empty |

`IGameAdapter` also carries `CanSupportMods` / `CanSupportSavegames` booleans for the UI to
consult before offering a feature, so a page can grey out an option without constructing an
adapter to find out.

The capability adapters mirror the same base-then-instance shape: `IBaseModAdapter` can scan
an arbitrary folder, and `WithInstanceSettings` turns it into an `IInstanceModAdapter` that
knows the folder the game actually uses.

## Two flags the sync engine reads

*Planned.*

```csharp
bool SupportsHardlinks { get; }   // default false
```

Whether the game's mod files are safe to hardlink into the content store. False when the game
or its updater may **rewrite a mod file in place**, which through a hardlink would corrupt the
store blob shared with every other repo and instance on that volume. False also means "nobody
has checked yet" — it is deliberately the default, because the failure is silent and the
blast radius is every repo on the disk. See
[07 — Mod sync design](07-mod-sync-design.md#hardlink-support-is-an-adapter-property).

The adapter is also responsible for deciding whether a newly registered mod is
**version-sensitive**, setting `Mod.Locked` when a completely new mod is registered — a Farming
Simulator map mod declares its maps in `modDesc`, which the adapter is parsing anyway. It never
sets `ModDependency.Locked`; profile-level locking is a human decision. See
[02 — Domain model](02-domain-model.md#locking-in-two-places).

## Version ordering

*Planned — see [PLAN.md](PLAN.md).*

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

**Ordering is computed client-side and sent to the server.** The server has no adapters and
cannot parse a version string — see [Where the comparison runs](#where-the-comparison-runs)
under *Adding a new game*.

## Adapter identity and versioning

```csharp
public readonly record struct GameAdapterId(string Id, int CompatibilityVersion)
```

Serialized as `id@version` — `_farming_simulator@1`. The `@` is reserved and rejected in the
`Id`.

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
| `FarmingSimulatorBaseSettings` | Empty today |
| `FarmingSimulatorInstanceSettings` | `GameDataFolder`, auto-detected |
| `FarmingSimulatorBaseModAdapter` | Scans a folder of `.zip` mods |
| `FarmingSimulatorInstanceModAdapter` | Scans `{GameDataFolder}/mods` |
| `FarmingSimulator*SavegameAdapter` | Placeholders, no members |

### How a mod is read

A Farming Simulator mod is a zip containing `modDesc.xml`. `GetModsFromFolder` opens every
archive in the folder and extracts:

| From | To |
| --- | --- |
| Filename without extension | `LocalMod.Id` — this becomes the server's `ModId` |
| `modDesc/version` | `LocalMod.Version` — becomes `ModVersionId` |
| `modDesc/title/en` (or first child, or filename) | `Name` |
| `modDesc/description/en` (or first child) | `Description`, normalised |
| `modDesc/author` | `Author` |
| `modDesc/iconFilename`, or any `icon_*` image | `Icon` |
| Any `store_*` image | `Images` |

Several details in this code are load-bearing and worth preserving if you touch it:

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
  ever needed, so `LocalModImage.Load` reopens the file. The CRC in the key means the key
  changes whenever the underlying file does, which is what makes the disk image cache safe.
- **An unreadable archive is skipped, not reported.** `InvalidDataException` returns `null`;
  a folder with a stray non-zip file still scans cleanly.

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
   Respect the cancellation token — the import page cancels the scan when the user navigates
   away.
5. Register the capability factories in the base and instance adapters' `_capabilities`
   lists.
6. Nothing else. Registration is by reflection, and the UI is driven by the dynamic forms.

Set `CanSupportSavegames = false` unless you implement it — the flag is what the UI checks
before offering the feature.

Override `VersionComparer` only if the shared parser genuinely cannot read your game's version
strings. Most cannot benefit from an override, and an incorrect one silently mis-orders
releases.

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
