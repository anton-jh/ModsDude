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

> **Status: in development.** Most of the surface exists; the central feature — applying a
> profile to a game installation — does not yet. See [docs/PLAN.md](docs/PLAN.md).

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
| [Mod sync design](docs/07-mod-sync-design.md) | The designed-but-unbuilt core feature |
| [Known issues](docs/08-known-issues.md) | Bugs, stubs, and scaling limits |
| [Plan](docs/PLAN.md) | Roadmap |

## Layout

```
ModsDude.Server/    ASP.NET Core API, PostgreSQL, Azure Blob Storage
ModsDude.Client/    ModsDude.Client.Core (adapters, server client, state)
                    ModsDude.Client.Wpf  (the desktop app)
ModsDude.Shared/    Helpers used by both client projects
```

## Running it

**Server** — needs PostgreSQL matching `appsettings.Development.json`
(`localhost:5432`, database `modsdude-dev`) and Azure credentials with access to the
storage account. Migrations apply on startup; Swagger UI is served in Development.

```bash
dotnet run --project ModsDude.Server/ModsDude.Server.Api
```

**Client** — Windows only (WPF). Sign-in goes through Microsoft Entra External ID.

```bash
dotnet run --project ModsDude.Client/ModsDude.Client.Wpf
```

Creating a repo requires `User.IsTrusted`, which is set by hand in the database. See
[docs/02-domain-model.md](docs/02-domain-model.md#user).

**Regenerating the typed API client** — start the server, then run
`ModsDude.Client/ModsDude.Client.Core/nswag-config.nswag`. Details in
[docs/03-server.md](docs/03-server.md#regenerating-the-client).

## Licence

See [LICENSE.txt](LICENSE.txt).
