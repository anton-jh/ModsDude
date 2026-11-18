# ModsDude documentation

These pages explain how ModsDude works and why it is built the way it is. They are written for people
working on the code: they cover what the code does not say on its own, such as how the pieces fit
together, the decisions behind them and the alternatives that were turned down.

Each area page has two parts. **How it works** describes the features and mechanisms as they are now.
**Decisions** lists the choices that shaped them, each with its reason.

| Page | What it covers |
| --- | --- |
| [Overview](overview.md) | What ModsDude is, the mental model, and what the client and the server each own |
| [Domain](domain.md) | Server entities and their invariants, access levels, archiving, retention, repeatable requests, concurrency |
| [Server](server.md) | Endpoints, errors, authentication, blob storage, background jobs, admin pages, deploys and backups |
| [Game adapters](adapters.md) | What an adapter supplies, game identity, targets, version ordering, adding a game |
| [Mods](mods.md) | The catalog, mod sources, importing and registering, version order, imagery, locking, updates |
| [Sync](sync.md) | The content store, applying a profile, file safety, drift, leases |
| [Savegames](savegames.md) | Publishing, check-out and check-in, compatibility mode, play attribution, slots |
| [Client](client.md) | The app shell, feedback channels, offline state, keeping data current, sessions, local state, updates |
| [Known issues](known-issues.md) | What is still wrong or unfinished |
| [Roadmap](roadmap.md) | What is planned next |

Setup, running locally, deploying and shipping are covered in the [repository README](../README.md).
Coding rules and conventions live in [CLAUDE.md](../CLAUDE.md). These pages don't repeat either.

## Glossary

| Term | Meaning |
| --- | --- |
| **Repo** | A group's shared collection of mod versions, profiles and savegames for one game, hosted on the server |
| **Member** | A user with access to a repo, at the level Guest, Member or Admin |
| **Mod version** | One file of one mod, registered in a repo. A "mod" is all the versions that share a mod ID |
| **Profile** | A named mod list in a repo |
| **Revision** | One saved state of a profile's mod list. Immutable; every save makes a new one |
| **Pin** | One entry of a revision: a mod at an exact version, optionally locked |
| **Game** | One game configured on one machine. Personal, never sent to the server except as presence |
| **Target** | One folder set a game reaches, such as BeamNG's game, BeamMP client and dedicated server folders |
| **Active profile** | The profile a game is set to follow |
| **Apply** | Making a game's mod folders match its active profile |
| **Drift** | A game's folders no longer matching what was last applied, or its profile having moved on |
| **Content store** | A per-disk, hash-addressed cache of mod files that game folders are linked or copied from |
| **Savegame** | A shared save slot, with a history of snapshots and a check-out log |
| **Snapshot** | One checked-in state of a savegame |
| **Game adapter** | The client code that knows one game: its files, folders, versions and savegames |
