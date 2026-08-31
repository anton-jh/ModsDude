# ModsDude

A shared mod repository for moddable games.

A group creates a **repo**, uploads mod files to it, and defines **profiles** — named,
pinned mod lists. Each member connects their own game installation as an **instance** and
syncs a profile into it, so everyone runs the same mods at the same versions without
passing archives around by hand.

The server stores metadata and issues short-lived links to blob storage; it never inspects
a mod file. Everything game-specific lives in a client-side **game adapter**, so supporting
another game means writing an adapter, not changing the system. Farming Simulator 25 is the
reference implementation.

> **Status: in development.** The core loop works end to end — import mods, pin them in a
> profile, sync that profile into a game's mod folder. Making drift unmissable is being built
> now, and savegames are untouched. See [docs/PLAN.md](docs/PLAN.md).

## Documentation

Start at **[docs/README.md](docs/README.md)**.

| | |
| --- | --- |
| [Overview](docs/01-overview.md) | What it is and how the pieces fit together |
| [Domain model](docs/02-domain-model.md) | Entities, identity, invariants |
| [Server](docs/03-server.md) | Architecture, authorization, persistence, API reference |
| [Game adapters](docs/04-game-adapters.md) | The adapter model, and how to add a game |
| [Client](docs/05-client.md) | WPF architecture and page inventory |
| [Flows](docs/06-flows.md) | End-to-end walkthroughs |
| [Mod sync design](docs/07-mod-sync-design.md) | The content store, reconciliation, drift |
| [Known issues](docs/08-known-issues.md) | What is still wrong or unbuilt |
| [Plan](docs/PLAN.md) | Roadmap |

## Layout

```
ModsDude.Server/    ASP.NET Core API, PostgreSQL, Azure Blob Storage
                    + ModsDude.Server.Domain.Tests, ModsDude.Server.Persistence.Tests
ModsDude.Client/    ModsDude.Client.Core (adapters, server client, state, catalog, import, sync)
                    ModsDude.Client.Wpf  (the desktop app)
                    + ModsDude.Client.Core.Tests
ModsDude.Shared/    Helpers used by both client projects
openapi/v1.json     The API's OpenAPI document, checked in so a stale client shows as a diff
scripts/            openapi.ps1 — regenerate or verify that document
```

## Running it

**Server** — needs:

- PostgreSQL matching `appsettings.Development.json` (`localhost:5432`, database
  `modsdude-dev`). Migrations apply on startup.
- Azure credentials with access to the storage account, which needs two containers: `mods` for
  mod files and `mod-images` for image derivatives.

```bash
dotnet run --project ModsDude.Server/ModsDude.Server.Api
```

Swagger UI is served in Development.

**Client** — Windows only (WPF). Sign-in goes through Microsoft Entra External ID. The server
URL comes from `ModsDude.Client.Wpf/appsettings.json` (`ModsDudeServer:BaseUrl`).

```bash
dotnet run --project ModsDude.Client/ModsDude.Client.Wpf
```

Two things to know about a fresh install:

- **Creating a repo requires `User.IsTrusted`**, which is set by hand in the database. See
  [docs/02-domain-model.md](docs/02-domain-model.md#user).
- **Sync writes to a per-volume content store**, configured under Settings: a path and a size cap
  per volume, plus which disk's store serves each one. An unconfigured volume falls back to
  `{volume}\ModsDude\store` with a 100 GB cap — the same defaults the settings page offers —
  rather than refusing to sync, so it works out of the box and the settings are worth visiting
  before the first large sync rather than after. See
  [docs/07-mod-sync-design.md](docs/07-mod-sync-design.md#where-the-store-lives).

## Tests

Three projects. Two run anywhere; the persistence one needs a real PostgreSQL.

```bash
dotnet test ModsDude.Server/ModsDude.Server.Domain.Tests
dotnet test ModsDude.Client/ModsDude.Client.Core.Tests      # Windows — hardlinks, Recycle Bin

# Drops and recreates the database it is pointed at. Give it one of its own.
MODSDUDE_TEST_DATABASE='Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=modsdude-tests' \
  dotnet test ModsDude.Server/ModsDude.Server.Persistence.Tests
```

`MODSDUDE_TEST_DATABASE` defaults to exactly that connection string, so locally it can be
omitted.

`.github/workflows/ci.yml` builds and tests on every push, in two jobs: Linux for the server,
because the persistence tests need a PostgreSQL service container, and Windows for the client,
because that is where WPF builds.

## Regenerating the typed API client

Start the server, then run `ModsDude.Client/ModsDude.Client.Core/nswag-config.nswag`.
**Then update the checked-in OpenAPI document**, or CI will fail:

```bash
pwsh scripts/openapi.ps1 -Update
```

Details in [docs/03-server.md](docs/03-server.md#regenerating-the-client).

## Licence

See [LICENSE.txt](LICENSE.txt).
