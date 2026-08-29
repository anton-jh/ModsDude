# Overview

## The problem

A group of people play the same modded game. Keeping everyone on the same set of mods, at
the same versions, is manual and error-prone: someone updates a mod, someone else does
not, and the multiplayer session breaks. Sharing a folder of archives over Discord works
until there are two thousand of them.

## The idea

ModsDude gives the group a **repo**: a shared, server-hosted collection of mod files. Inside
the repo they define **profiles** — named, pinned mod lists ("Season 4 map", "vanilla+
lite"). Each member connects their own game installation as an **instance**, picks a
profile, and the client makes the installation match it.

The server never runs the game and never inspects a mod file. It stores metadata, decides
who may do what, and hands out short-lived links to blob storage. All of the
game-specific knowledge — what a mod file looks like, where the game keeps its mods, how
to read a mod's name out of an archive — lives in a **game adapter** on the client.

## Mental model

```
User ──member of──▶ Repo ──has──▶ Profile ──pins──▶ ModVersion
                     │                                  │
                     ├──contains──▶ Mod ────has────────▶ ┘
                     │
                     └──configured by──▶ Game adapter (base settings)

                                              ▲
Machine ──has──▶ Instance ────configured by───┘  (instance settings)
                     │
                     └──active profile──▶ Profile   (from one repo at a time)
```

The split that matters: **a repo is shared and lives on the server; an instance is
personal and never leaves the machine.** The repo says "this profile needs these mods at
these versions". The instance says "and the mods go in this folder". Neither knows about
the other's half until the client puts them together.

An **instance is one mod folder** — a sync target. It is scoped to a *game*, not to a repo, so
one Farming Simulator 25 installation is configured once and appears under every FS25 repo you
belong to. The scope is the adapter plus whatever its base settings say about which game it was
configured for, because one adapter can serve several — FS22 and FS25 share one, and a scripted
adapter could serve a dozen. See [04 — Game adapters](04-game-adapters.md#instance-scope).
Games that keep mods in more than one place get one
instance per folder: BeamNG.drive with BeamMP needs three, since singleplayer, the MP
client, and a dedicated server each read from a different directory. The model deliberately
does not care whether those folders belong to the same installation, or whether a game is
installed at all — only where the mods go.

Because sync makes a folder match a profile exactly, an instance has **one active profile
at a time, from one repo**.

## Components

| Component | Project | Role |
| --- | --- | --- |
| API | `ModsDude.Server.Api` | ASP.NET Core minimal API, one class per endpoint, versioned under `api/v1` |
| Application | `ModsDude.Server.Application` | Authorization primitives, service abstractions the API depends on |
| Domain | `ModsDude.Server.Domain` | Entities and invariants. No framework dependencies |
| Persistence | `ModsDude.Server.Persistence` | EF Core over PostgreSQL, entity configuration, migrations |
| Storage | `ModsDude.Server.Storage` | Azure Blob Storage, SAS link issuance |
| Client core | `ModsDude.Client.Core` | Game adapters, server client, local state, models. UI-framework agnostic |
| Client WPF | `ModsDude.Client.Wpf` | The desktop app: views, view models, navigation, imaging |
| Shared | `ModsDude.Shared` | Small helpers used by both sides of the client |

## Where work happens

One rule decides nearly every placement question in this system:

> **The server owns what members must agree on. The client owns everything derived from files.**

The server never opens a mod archive, never parses a version string, never reads
`AdapterData.Configuration`. That is what lets a new game ship as a client-side adapter with no
server deployment — and it is why so much apparently server-shaped work is done client-side and
sent up as a result.

| | Server | Client |
| --- | --- | --- |
| Identity, membership, authorization | ● | |
| Repos, profiles, mod dependencies | ● | |
| Name uniqueness, one-dependency-per-mod | ● | |
| Mod version rows, sequence, `ContentHash`, `Locked`, image refs | ● stores | ● derives |
| Blob storage and SAS issuance | ● | |
| Verifying the blob exists before registering | ● | |
| Reading mod archives, `modDesc`, icons | | ● |
| Comparing version strings, computing order | | ● |
| Deciding a mod is version-sensitive | | ● |
| Hashing files, generating image derivatives | | ● |
| Instances, sources, content store, sync, drift | | ● |

### What the server does and does not validate

It enforces **structural** invariants — the caller is authorized, the name is free, a sequence
number is unique within a mod, the blob exists before metadata is written. It does not check
**semantic** ones: that a declared hash matches the bytes, that `1.2` really is newer than `1.1`,
or that a mod flagged version-sensitive actually is.

That is a deliberate trust posture, not an oversight. Members are authenticated and few, and the
places where being wrong would matter are caught elsewhere: a wrong `ContentHash` fails
verification on download, so it breaks only the publisher's own mod
([07](07-mod-sync-design.md#cache-isolation)). Everything else is cosmetic or repairable.

### Three consequences worth knowing

**Adapters ship with the client, so the server cannot help a client that lacks one.** A repo
pins `_farming_simulator@1`; a client build that no longer carries that adapter version cannot
open the repo at all. The server stores the identifier without validating it, so nothing warns
anyone. There is no mitigation today — worth knowing before dropping an old adapter.

**Placement instructions are relative, and assert their neighbours.** Registration says *insert
version X between A and B*, not *take sequence number 2*. Absolute positions collide under
concurrent registration; relative ones do not, but relative alone still allows a silently wrong
order when two members insert against a state neither has seen the other change. Asserting both
neighbours turns that into a rejection, a refetch and a recompute. See
[09](09-mod-catalog.md#importing-several-versions-of-one-mod-at-once).

**The client trusts the server's stored ordering rather than recomputing it.** Recomputing on
read would let two clients on different adapter compatibility versions disagree about which mod
version is newest. Stored once, read by everyone.

## Technology

- **.NET 10**, C# with nullable reference types and file-scoped namespaces throughout.
- **PostgreSQL** via EF Core. Migrations run automatically at startup (`Program.cs`).
- **Azure Blob Storage** for mod files. The API never proxies file bytes; it issues
  user-delegation SAS links and the client talks to blob storage directly.
- **Microsoft Entra External ID** (CIAM) for identity. The desktop client uses MSAL as a
  public client with a cached token; the API validates the resulting JWT.
- **NSwag** generates the typed C# server client from the running API's OpenAPI document
  into `ModsDude.Client.Core/ModsDudeServer/Generated.cs`.
- **WPF** with CommunityToolkit.Mvvm for the desktop UI.

## Scale targets

The system is built for a small, trusted group, but the data volumes are not small. Design
decisions should assume:

- **1,000–2,000 mods in a single profile.** Farming Simulator installs of this size are
  routine.
- **Thousands of mod versions registered in a repo overall.**
- **Multiple instances per machine** sharing the same repo.

This is why mod imagery is decoded lazily and cached to disk, why folder scanning is
parallelised, and why the sync design in [07](07-mod-sync-design.md) avoids copying file
bytes wherever it can. It is also why the current `GET /repos/{id}/mods` endpoint, which
returns every mod and every version in one unpaged response, is flagged as a scaling
problem.
