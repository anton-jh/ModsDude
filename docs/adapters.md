# Game adapters

A game adapter is the client code that knows one game: what its mod files look like, where it keeps
mods and savegames, how its version strings compare, and which processes mean it is running. Adapters
are compiled into `ModsDude.Client.Core` under `GameAdapters/Implementations/`. The general engine does
every write; adapters only read and compute.

Two adapters exist:

| Adapter | Games | Mods | Savegames | Targets | Remote source |
| --- | --- | --- | --- | --- | --- |
| `_farming_simulator@1` | FS25 (the game is chosen in base settings) | ● | ● | `game` | ModHub |
| `_beamng_drive@1` | BeamNG.drive, with or without BeamMP | ● | | `game`, `client`, `server` | |

BeamNG.drive has no concept of savegames, so its adapter doesn't offer them.

## How it works

### Three stages

An adapter is built up in three stages. Each stage adds settings and capabilities.

| Stage | Interface | Settings | Where they live |
| --- | --- | --- | --- |
| Game | `IGameAdapter` | none | |
| Base | `IBaseGameAdapter` | **Base settings**: what the group agrees on, such as which game in a series | On the repo, on the server (`AdapterData`) |
| Local | `ILocalGameAdapter` | **Local settings**: what one machine decides, such as folder paths | In the client's local state, per game |

Both kinds of settings are described as a `DynamicForm` (attributes on a settings class). The general UI
renders the form, so an adapter needs no UI code. A field marked `[CanBeModified]` can be changed after
the repo is created. An adapter with no local fields connects on its own when the repo loads, if the game
is installed.

Capabilities hang off the base and local stages through `Get…CapabilityAdapterFactory<T>()`:

| Capability | Base interface | Local interface |
| --- | --- | --- |
| Mods | `IBaseModAdapter`: attributes, savegame compatibility policy, reading a mod file, likely-copy names | `ILocalModAdapter`: mod targets, installed mods, layout |
| Savegames | `IBaseSavegameAdapter` | `ILocalSavegameAdapter`: savegame targets, slots, what belongs in a packed save, rename edits |
| Remote mod sources | `IRemoteModSourcesAdapter` | |

Every capability is optional. The general code and UI adapt to what an adapter offers, never to which
adapter it is. For example, a repo whose game has no savegames shows no Savegames section.

### Game identity

A game on a machine is keyed by `GameIdentity` (`IBaseGameAdapter.Scope`), not by adapter ID or repo.
For an adapter serving one game, the identity is its adapter ID without the version suffix. An adapter
serving several games, like Farming Simulator's, adds the game chosen in its base settings. So a machine
configures FS25 once, and that one game serves every FS25 repo.

- The version suffix (`@1`) is left out on purpose. A repo on `@2` still matches games set up under `@1`.
- An identity field must never be `[CanBeModified]`. Changing it on a repo would silently detach every
  member's game from it.
- `GameDisplayName` and `GameShortName` name the game for the sidebar, which groups repos by game.

### Targets

A game reaches zero or more **targets**, each with a permanent `TargetKey`. Targets are derived from the
local settings, so clearing a folder setting simply removes the target. A copy of the list is kept in local
state (`PersistedGame.Targets`) for when the adapter can't be asked.

- `ModTargets` lists mod folders and `SavegameTargets` lists savegame folders. A key can appear in one or
  both.
- `ModTarget.SupportsHardlinks` says whether files there may be hardlinked to the content store. It is off
  by default (see [Sync](sync.md#hardlinks)).
- `ModTarget.Shared` marks a folder that other programs also keep files in. Sync only touches what it
  installed there, and it leaves later additions alone. BeamMP's launcher cache is one.
- Display names come from the adapter on every read and are never stored.

### Reading mods

`GetModsFromFolder` and `GetInstalledMods` read a folder into `LocalMod`s: mod ID, version string, file
name, attributes, images and the adapter's lock flag. `ReadModFileAsync` reads one file, and tells
"not a mod" (null) apart from "couldn't read it" (throws). `GetInstalledMods` is given the files the
manifest already describes, so planning opens only what changed since the last apply.

`ModArchives` holds the shared archive handling:

- Scans go in file-name order.
- Decompressed sizes are capped, against zip bombs.
- `OpenScanned` refuses a file whose length or modified time changed since it was scanned.
- Each adapter logs an unreadable archive as a warning and skips it.

Mod IDs are normalised in `ModKey` at the adapter boundary, so differences in casing never reach storage.

### Writing: layouts and file edits

Adapters never write. When applying, the engine asks for a layout:

- **`Layout(ModLayoutContext)`** gets the target and the full desired set of mods, and returns a
  `ModLayout`.
- **`Placements`**: the file name each mod gets directly in the target's mod folder. Mod folders stay
  flat.
- **`ManagedFiles`**: a list of `GameFileEdit`s the game also needs.

A `GameFileEdit` is a pure transform of one file:

- It takes the current content (or null) to the desired content, or returns null to leave the file
  alone.
- It is idempotent, and keeps any entries it doesn't manage.
- Its path is relative to the folder it concerns and may climb out with `..`.
- It flags whether the replaced content goes to the Recycle Bin.

BeamNG uses one to switch on the profile's mods in `mods/db.json`. Savegame renames use them too
(`RenameSavegame`).

### Version ordering

`IGameAdapter.VersionComparer` compares two version strings of one mod. `DefaultModVersionComparer`
handles dotted numbers with an optional `v` prefix and pre-release suffixes. A game that numbers mods
differently replaces it.

A comparer may **abstain** instead of guessing. Abstaining costs one question to the user, asked once and
stored for the whole repo. A wrong guess goes unnoticed until a profile pins the wrong build. How the
order is built and stored is described under [Mods](mods.md#version-order).

### Other adapter knowledge

- **`ProcessNames`**: every process that means the game is running, including launchers that stay
  open. Any file change to the game is refused while one is alive. An empty list would refuse every
  change.
- **`SavegameCompatibilityPolicy`** (mod adapter): how much each kind of mod list change weighs against
  a savegame, and the score above which checking out on the latest revision asks first. A changed or
  removed locked mod always asks. See [Savegames](savegames.md#compatibility-mode).
- **`LikelyCopyOf`**: recognises names like `x (1)` or `x - Copy` (including the Windows shell's
  localised copy name), so import can ask before registering an accidental copy.
- **`BelongsInPackedSave`**: leaves regenerated files out of a packed savegame. Farming Simulator leaves
  out `screenshot.png`.
- **`GetSlots`**: lists every slot, occupied or not. A slot it can't read comes back occupied and
  unnamed, never left out.
- **Attributes**: tags like map or category, for search and grouping. An adapter reports only the
  attribute keys it declares. A key can be declared ranked: its listed values then sort and compare in
  the order listed, ahead of any value not listed. Otherwise numbers sort first, as numbers, then text
  in natural order.

### The Farming Simulator adapter

- Base settings choose the game, which is part of the identity. Only FS25 is offered so far.
- There are no local settings. The data folder is found under `Documents\My Games` from the game's year,
  and connecting is refused if it doesn't exist. Mods live in its `mods` folder, unless the game's own
  `gameSettings.xml` points them elsewhere.
- The adapter reads `modDesc.xml` from each archive, and locks mods that declare maps.
- Attributes: `category`, `categoryGroup`, `brand`, `kind` and `multiplayer`. `kind` comes from each
  store item's root element, from `extraSourceFiles` (`script`) and from `maps` (`map`), so a mod can
  have several. It is ranked map, script, vehicle, placeable, handtool: vehicle and placeable mods
  register their code as specializations, so `extraSourceFiles` marks a mod that is a script first.
  `categoryGroup` is ranked in shop order.
- Hardlinks are on: the game's own updater renames over files instead of rewriting them in place.
- `ModHubModSource` offers newer ModHub versions through the server's crawler.

### The BeamNG adapter

- Three targets: the game's own mod folder, BeamMP's client cache (shared, files named with a hash
  stamp) and a dedicated server's resource folder.
- It reads `mod_info/<id>/info.json` from the archive, falling back to the newest entry's date as the
  version. Hardlinks are off.
- It keeps `mods/db.json` in step with a managed file edit. The edit only switches on the profile's own
  mods, and leaves mods the game hasn't listed yet alone.

### Adding a game

1. Create `Implementations/<Game>V1/` with a game adapter, base and local settings forms, and the
   capability adapters the game supports.
2. Choose the adapter ID (`_<game>@1`) and decide what goes in the game identity. Make sure no identity
   field is `[CanBeModified]`.
3. Fill in `ProcessNames`.
4. For mods: targets with permanent keys, archive reading, `Layout`, a version comparer if the default
   doesn't fit, a savegame compatibility policy and attribute definitions. Leave `SupportsHardlinks` off
   unless the game's updater has been tested.
5. For savegames: targets, slot reading, slot paths, the packed-save filter, rename edits if the game
   stores a name.
6. Add tests for archive reading, version comparison and any file edits.

Adapters are found by reflection over the assembly, so nothing needs registering.

If a game genuinely can't be expressed as placements and file edits, raise it before building; an
adapter-owned write step needs its own safety review.

## Decisions

**The engine writes; adapters describe.** All of the file safety (planning, staging, atomic moves,
recycling, recording) is implemented once. An adapter that wrote files would have to repeat it and could
get it wrong.

**Adapters see the whole operation.** `Layout` gets the full desired set and the target, not one mod at
a time, so a game whose files depend on each other (like BeamNG's `db.json`) can be laid out correctly.

**Base settings are shared, local settings are personal.** Which game a repo is for must be agreed by
everyone. Where the game is installed is each machine's business.

**Games are keyed by identity, not by repo or adapter.** One game configured twice is impossible to
represent, and one adapter can serve a whole game series.

**Target keys are permanent.** Manifests, savegame bindings and settings refer to them. Renaming one
would orphan all of them on every machine.

**Abstain rather than guess.** An unanswered comparison costs one question. A wrong answer silently offers
a downgrade as an upgrade.

**Hardlinks are opt-in per target.** If something rewrites a linked file in place, the shared store copy is
corrupted for every repo on that disk, and nothing notices. A slow copy is visible and recoverable.

**Every running process blocks changes.** Changing files under a running game risks corrupting its state,
and a game that doesn't declare its processes gets every change refused rather than none.
