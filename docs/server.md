# Server

The API is one ASP.NET Core process (`ModsDude.Server.Api`) over PostgreSQL and Azure Blob Storage.
Besides the API it hosts the admin pages, the Hangfire jobs and the ModHub crawler. How to set it up and
run it is in the [repository README](../README.md).

## How it works

### Endpoints

Each endpoint is one class implementing `IEndpoint`, under `Endpoints/<Feature>/`. It authorizes, uses
`ApplicationDbContext` directly, calls domain methods and returns typed `Results`.

- The class name minus `Endpoint` becomes the route name (`GetModsV1`), which NSwag turns into the client
  method name.
- Endpoints are registered in order of full type name, so the OpenAPI document comes out the same on
  every build.
- `openapi/v1.json` is generated during a build without a running server. It is checked in, and CI fails
  when it is out of date.

Every endpoint declares its authorization:

- Repo-scoped routes use `RequireRepoLevel(Guest|Member|Admin)`.
- Some endpoints check inside the endpoint instead, or as well:
  - deletes that must answer `Ok` on a repeat after the thing is gone (`DeleteRepo`, `KickMember`);
  - the file-link endpoints, whose repo is in the request body;
  - checks that depend on the subject, such as `CreateInvite`, `UpdateMembership`, `RevokeInvite`
    (creator or Admin), `RecordGameActivity` and `CreateRepo` (trusted users only).
- Routes about the caller's own data (user, changes, repo list, activity) need only an authenticated
  user. So do images and ModHub lookups, whose content is public and has no repo in its address.
  Uploading an image also needs the caller to be trusted or a member of some repo.

### Errors

An expected outcome, such as not found, forbidden, conflict or stale, is a typed result carrying a
problem type from `ErrorHandling/Problems.cs`. Each failure the client might need to react to has its
own problem type; the status code is secondary. Problem types carry both `[EnumMember]` and
`[JsonStringEnumMemberName]`, because System.Text.Json ignores the first.

Anything thrown goes through `ApiExceptionHandler`:

| Thrown | Answer |
| --- | --- |
| Unreadable request (`BadHttpRequestException`) | Its own status (400, or 413 when too large), `InvalidRequest` |
| `DomainValidationException` | 400 `InvalidRequest`, logged as a warning |
| Request aborted by the caller | No body, logged at Debug |
| Caller can't be identified | 401 |
| Anything else | 500 `Unexpected`, logged as an error |

Authorization failures are 403. 401 is only for a caller who can't be identified. Request values are
checked at the boundary by `RequestChecks`, which returns typed refusals. Enums are accepted by name only.

### Build match

Every API request carries `X-ModsDude-Build`. `ClientBuildMiddleware` runs before authentication and
answers anything but this server's own build with 412. The client then shows a "please update" overlay.
In Development, a request without the header is let through so Swagger UI works.

Builds are numbered by counting commits on `main`, and the client package version is `0.0.N`.
`GET /health` answers with the build. `GET /download` redirects to that build's installer.

### Authentication and users

The API validates Entra External ID tokens. `UserLoadingMiddleware` loads the caller, creates the user
on their first request, and refuses blocked users with 403 `UserBlocked`. If two first requests race,
the second one finds the user the first created.

Two more authentication schemes exist:

- **Admin** (basic auth): for the admin pages and the Hangfire dashboard. It answers only on the admin
  port, and the password compare takes fixed time.
- **Deploy** (`Authorization: Deploy <token>`, kept apart from Bearer on purpose): CI uses it for the
  update announcement.

### Blob storage

Three containers hold the files. The API never handles mod or savegame bytes; it issues short-lived
user-delegation SAS links scoped to one blob, and the client transfers directly.

| Container | Address | Notes |
| --- | --- | --- |
| `mods` | `{repoId}/{modId}/{versionId}` | Upload, then register. Registration refuses unless the blob exists and its recorded `sha256` matches |
| `savegames` | `{repoId}/{savegameId}/{contentHash}` | Content addressed. An address that already exists is reported as `AlreadyStored` |
| `mod-images` | `{hash[..2]}/{hash}` | Mod image renditions and avatars: small WebP files, uploaded and streamed through the API, cached by clients for a year |

The client records the file's SHA-256 in blob metadata while uploading, because Azure only computes MD5.
`CreateModUploadLink` tells two cases apart:

- **`FileAlreadyPresent`** (carries the recorded hash): the blob exists but isn't registered yet, so a
  failed import can be retried.
- **`AlreadyRegistered`**: the version is already in the repo.

The user-delegation key is cached for a day. Links are backdated five minutes to allow for clock skew.
Each issued link is recorded as a `FileTransfer` for the traffic statistics. The API authenticates to
Azure with a service principal from environment variables.

Deleting a mod version commits the database change first and deletes the blob after. A stranded blob is
harmless and reclamation picks it up; a registration without a blob would break every client that pins
it.

### Background jobs

Hangfire stores its state in the application database, in its own `hangfire` schema outside the EF
migrations. The reclamation and retention decisions live in pure, tested code; their Hangfire classes only
fetch and carry out. The ModHub crawl's sweep, poll and refresh logic still lives in its Hangfire class.

| Job | When | Does |
| --- | --- | --- |
| `blob-reclamation` | 04:00 UTC | Deletes blobs nothing refers to |
| `retention-schedule` | 05:00 Stockholm | Dates rows that [retention](domain.md#retention) made eligible, clears dates that no longer apply |
| `retention-delete` | 06:00 Stockholm | Deletes rows that are due and still eligible for the reason they were dated |
| `storage-statistics` | 03:30 UTC | Records stored bytes and blob counts per container and repo, replacing that day's earlier sample. Retried by Hangfire |
| `modhub-crawl` | 19:00 Stockholm | Crawls ModHub (see below) |

**Reclamation** is the only code apart from the delete endpoints that deletes blobs, so it is guarded
heavily:

- It lists blobs before reading registrations, so a registration made in between is already covered.
- It ignores blobs younger than a day, which is well past an upload link's lifetime.
- It never deletes a blob whose name doesn't parse.
- Each delete is conditional on the ETag the listing saw.
- Each container holds a `modsdude-storage-binding` marker blob carrying the GUID of the database's
  `StorageBinding` row. If any container belongs to another database, or carries a marker the database
  doesn't know, the whole sweep is refused (`ReclamationGate`). An unbound container also refuses the
  sweep if it would lose at least 20 blobs and more than half of them.
- A missed run is skipped, not caught up at startup, so a crash loop can't become a delete loop.

**Retention** deletion checks the policy again rather than trusting the date, and clears stale dates
before deciding what is due, so both jobs are idempotent. The writes that can make a row ineligible clear
its date right away (`RetentionUpkeep`). Neither job is retried, because a retry would shift what a
deletion date means. A failure in one repo is logged and doesn't stop the others.

### ModHub crawler

`ModsDude.Server.ModHub` reads the Farming Simulator ModHub website, so the FS adapter can offer newer
versions without the game installed (see [Mods](mods.md#remote-sources)). It belongs to the FS adapter,
and only adapter code talks to it.

- It runs daily. A run sweeps every listing if the last full sweep is a week old or unfinished, then polls
  the newest listings, then re-reads the 900 longest-unread mods. That last step is how mods removed from
  ModHub get deleted.
- It waits a second between requests.
- It stops at a listing page it doesn't recognise, or at the fifth unreadable mod page in a row.
- A run is skipped while another is still processing.

Clients look mods up through `LookUpModHubMods`. Downloads come from ModHub directly, not through the
server.

### Change counters

`GET changes` returns one small set of counters: one for the user, and six for each repo they are in
(repo, profiles, savegames, mods, members, activity), plus the current update announcement. Database triggers move the counters. The client polls it every minute
and re-reads only what moved (see [Client](client.md#keeping-data-current)).

The mod list is a change feed. `GET repos/{id}/mods` takes `after`, the last sequence number the client
has seen, and returns the versions changed since, plus tombstones for deleted ones
(`ModVersionDeletions`), in pages of up to 500. Usage
(which profiles pin what) is its own endpoint, because it changes at different times than the versions.

### Admin pages

Razor pages at `/admin`, behind the admin scheme and only on the admin port, which compose binds to
`127.0.0.1:8081`:

| Page | Shows and does |
| --- | --- |
| Overview | Users, storage trend, traffic, backups, largest items |
| Repos | Rename, archive, restore, delete, prune, invites, members and their levels |
| Users | Rename, remove avatar, block and unblock, grant and revoke trust, memberships |
| Trust codes | Issue and revoke |

The Hangfire dashboard is at `/admin/jobs`.

### Update announcement

At the start of a CI run on `main`, CI `PUT`s `/deploy/update-announcement`. If the deploy doesn't
succeed, CI `DELETE`s it again. The announcement is held in memory and lapses after 20 minutes. Clients see it through `GET changes` and warn the user that the
server is about to move on.

### Deploys and backups

The operational steps are in the [repository README](../README.md). The design:

- **Ports.** Only Caddy publishes public ports. The API's admin port is published on localhost only.
- **Deploy order.** `deploy/deploy.sh` checks its preconditions, loads the image, makes sure the backup
  timers, database and Caddy are up, then stops the API, takes a backup, and migrates in one transaction
  from the idempotent migration script. An `EXIT` trap restarts the old build if the stop, backup or
  migration fails. Only then does it start the new build.
- **Migrations.** Production migrates only in `deploy.sh`. Development migrates at startup.
- **CI.** Concurrency groups are never cancelled, so a deploy is never cut off halfway. Releasing is safe
  to re-run.
- **Backups** run hourly, daily and before every deploy:
  - each dump's table of contents is checked before upload;
  - the daily dump is test-restored into a scratch database;
  - backups are uploaded with a create-only SAS;
  - backup status shows on the admin Overview page.

## Decisions

**Endpoints use the DbContext directly.** There are no handler, mediator or repository layers. Queries
shared by several endpoints live in Persistence extension methods or `*Reads` classes beside the
endpoints.

**Typed problem types, not status codes.** The client branches on what went wrong. Two failures that need
different reactions never share a problem type, even when they share a status.

**Exact build match instead of compatibility.** Client and server of one build ship together. Refusing
any other pairing is simpler and safer than keeping old shapes working.

**The first request is the sign-up.** There is no registration step. The token proves identity, and the
display name is ModsDude's own after the first request.

**The API never handles mod or savegame bytes.** Files are large and many. SAS links let clients go
straight to storage, and the API stays small. Images are the exception: they're small, immutable and
fetched in bulk, so streaming them with long cache headers is simpler.

**Record the hash at upload, verify it at registration.** Azure only computes MD5. Recording SHA-256 in
metadata lets registration refuse a mismatch without the server reading the bytes.

**Repo-scoped composite keys.** The repo's content tables (mod versions, profiles, revisions, savegames,
snapshots) have primary keys that start with `RepoId`. A query can't forget to scope by repo, and a pin
or snapshot can't refer to another repo's rows.

**Reclamation and retention are built to be run by an attacker.** Anyone with dashboard access can trigger
them repeatedly or concurrently. Every run re-derives its decisions from the current data, deletes
conditionally, and refuses to sweep at all when storage looks like it belongs to another database.

**The crawler is a guest on someone else's site.** It is slow and polite, stops on anything unexpected,
and is scoped to the one game module that needs it.

**The OpenAPI document is checked in.** It shows API changes in review and lets the client be regenerated
without running the server.

**Migrate while the API is stopped, after a backup.** A failed migration then leaves a database the old
build can still run against, plus a backup taken just before.
