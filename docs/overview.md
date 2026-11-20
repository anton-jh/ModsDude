# Overview

## What ModsDude is

A group playing the same modded game needs everyone on the same mods at the same versions. Doing that
by hand breaks down quickly, and with Farming Simulator a single install routinely holds one to two
thousand mods.

ModsDude gives the group a **repo** on a server. Members register mod files into it and define
**profiles**, which are named mod lists that pin exact versions. Every save of a profile makes a new
**revision** (a save that changes nothing makes none), so earlier lists can still be read, restored or
branched into a new profile. Each member
connects their own copy of the game, sets it to follow a profile, and the client makes the game's mod
folders match that profile. Savegames are shared too: one person at a time holds a save, plays it, and
checks it back in. Anyone can take a save over, with a warning that names the holder.

## Mental model

```
User ──member of──▶ Repo ──has──▶ Profile ──has──▶ Revision ──pins──▶ ModVersion
                     │                                                    ▲
                     ├──contains──────────────────────────────────────────┘
                     ├──has──▶ Savegame ──has──▶ Snapshot (played on a revision)
                     └──configured by──▶ Game adapter + base settings

Machine ──has──▶ Game (adapter + local settings)
                  ├──follows──▶ one Profile, at its head or at a pinned revision
                  ├──holds──▶ at most one Savegame tied to a profile
                  └──reaches──▶ Targets ──▶ mod folder and/or savegame folder
```

The split that matters most: **a repo is shared and lives on the server; a game is personal and stays
on the machine.** The repo says "this profile needs these mod versions". The game says "and this
machine keeps them in these folders". The client puts the two together when it applies a profile.

A game is keyed by *which game it is*, not by repo. A machine configures Farming Simulator 25 once, and
that game shows up under every FS25 repo the user belongs to. The game carries the policy: it follows
one profile and holds at most one savegame tied to a profile. That is because a game process loads every
folder the game reaches, so they all have to agree.

A game can reach several **targets**. Farming Simulator has one. BeamNG.drive has up to three, one for
each folder set in its local settings: the game, the BeamMP client cache and a dedicated server. Each target has a permanent key and can have a mod folder,
a savegame folder, or both.

## Components

| Project | Role |
| --- | --- |
| `ModsDude.Server.Api` | ASP.NET Core minimal API (one class per endpoint), Razor admin pages, Hangfire jobs, deploy hooks |
| `ModsDude.Server.Application` | Authorization primitives, the time service, and the interfaces that storage, persistence and the ModHub module implement |
| `ModsDude.Server.Domain` | Entities and invariants, free of framework packages |
| `ModsDude.Server.Persistence` | EF Core over PostgreSQL: configuration, queries, migrations, change counters |
| `ModsDude.Server.Storage` | Azure Blob Storage: containers, SAS links, blob metadata |
| `ModsDude.Server.ModHub` | The Farming Simulator ModHub crawler. Part of the FS adapter, server side |
| `ModsDude.Client.Core` | Game adapters, generated API client, stores, import, sync, content store, savegames, notices |
| `ModsDude.Client.Wpf` | The Windows desktop app |

Test projects sit next to the projects they cover. The domain and client core suites are pure. The
persistence suite runs against a real PostgreSQL, because many invariants live in the database.

**Technology:** .NET 10, PostgreSQL with EF Core, Azure Blob Storage, ASP.NET Core Identity and
OpenIddict (the server is its own sign-in provider), Resend for account emails, Hangfire, NSwag, WPF with
CommunityToolkit.Mvvm, Velopack for client installs and updates. Production runs in Docker Compose behind
Caddy on one VPS.

## Where work happens

> **The server owns what members must agree on. The client owns everything derived from files.**

| | Server | Client |
| --- | --- | --- |
| Identity, membership, authorization | stores and checks | |
| Repos, profiles, revisions, savegames, check-outs | stores and checks | |
| Mod version rows: order, content hash, lock flag, images | stores | derives |
| Blob storage, SAS links | issues | uses |
| Reading mod archives and savegame folders | | ● |
| Comparing version strings, computing order | | ● |
| Hashing mod and savegame files, making image renditions | | ● |
| Games, targets, content store, applying, drift | | ● |

The server never opens a mod file, never parses a version string and never reads an adapter's settings.
The client sends results up, such as a hash, a position in the version order or a lock flag, and the
server stores them.

## Scale targets

- 1,000–2,000 mods in one profile.
- Thousands of mod versions in one repo.
- A game reaching several folders.

These numbers are why the client links files from a content store instead of copying them, decodes
images lazily, scans folders in parallel, and keeps the mod list current through a change feed instead
of re-reading it.

## Decisions

**Game knowledge lives on the client.** Adapters are compiled into the client, so a new game ships
without a server deploy, and the server never needs to understand a game's files. The cost is that a
client build without a repo's adapter can't open that repo. The server stores the adapter ID without
checking it, so dropping an old adapter version strands its repos.

**The server checks structure; correctness is checked where it matters.** The server enforces
authorization, unique names, a unique position for each version, and that an uploaded blob exists with
the hash its uploader recorded. It never hashes mod or savegame bytes itself. It does not check that `1.2` really is newer than `1.1`, or that a mod
flagged as locked really needs to be. A wrong hash is caught because every client verifies bytes against
it before use, so it only breaks the publisher's own mod.

**Order is stored once, never recomputed.** The client that registers a version computes its position,
and the server stores it. Every client reads the stored order, so clients on different adapter versions
can't disagree about which version is newest.

**The client and the server ship together.** One build's client talks only to the same build's API,
which refuses anything else (see [Server](server.md#build-match)). The API can change freely, with no
versioning or compatibility shims.
