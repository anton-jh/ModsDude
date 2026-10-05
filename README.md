# ModsDude

A shared mod repository for moddable games. A group creates a **repo**, uploads mod files to it and
defines **profiles**: named, pinned mod lists. Each member connects their own game and syncs a profile into
it, so everyone runs the same mods at the same versions. Savegames are shared the same way.

The server stores metadata and hands out short-lived links to blob storage. Everything game-specific lives
in client-side game adapters.

> **Alpha.** One developer, no users. Data, the API and client state formats change without migrations.

- [Repository layout](#repository-layout)
- [Cloud setup](#cloud-setup)
- [Local development](#local-development)
- [Production](#production)
- [CI and releases](#ci-and-releases)

## Repository layout

```
ModsDude.Server/
  ModsDude.Server.Api           ASP.NET Core API, admin pages, Hangfire jobs
  ModsDude.Server.Application   Authorization, and the interfaces storage and persistence implement
  ModsDude.Server.Domain        Entities and invariants (no framework references)
  ModsDude.Server.Persistence   EF Core, PostgreSQL, migrations
  ModsDude.Server.Storage       Azure Blob Storage and SAS links
  ModsDude.Server.ModHub        Farming Simulator ModHub crawler
  *.Tests                       Domain, persistence and ModHub parser tests
ModsDude.Client/
  ModsDude.Client.Core          Game adapters, generated API client, sync, content store
  ModsDude.Client.Wpf           The desktop app (Windows only)
  ModsDude.Client.Core.Tests
openapi/v1.json                 The API's OpenAPI document, checked in
scripts/openapi.ps1             Regenerates or verifies openapi/v1.json
deploy/                         The production server: compose.yml, Caddy, deploy scripts
.github/workflows/ci.yml        Tests on every push; ships from main
```

## Cloud setup

Both development and production need an Entra External ID tenant for sign-in and an Azure storage account
for files. One tenant can serve both. Use one storage account per environment.

### Entra External ID

1. Create an external tenant. Note its subdomain (`<tenant>.ciamlogin.com`) and tenant ID.
2. Create a sign-up and sign-in user flow that collects **Display Name**. The server takes a user's name
   from the token's `name` claim and identifies them by `sub`.
3. Register three applications:

   | Registration | Platform and redirect URI | Notes |
   | --- | --- | --- |
   | API | none | Under Expose an API, set an Application ID URI (such as `api://modsdude-server`) and add a delegated scope (such as `act_as_user`). |
   | Client | Mobile and desktop applications: `http://localhost` | Allow public client flows. Grant it the API's scope. |
   | Swagger UI | Single-page application: `https://localhost:7035/swagger/oauth2-redirect.html` | Development only. Grant it the API's scope. |

4. Add the Client and Swagger UI registrations to the user flow.

### Storage account

- A standard general-purpose v2 account. The server creates the `mods`, `mod-images` and `savegames`
  containers on startup.
- Whoever the server runs as needs **Storage Blob Data Contributor** on the account. The server signs its
  SAS links with a user delegation key, so no account keys are involved.
- Never point two databases at one storage account. The nightly blob reclamation job deletes blobs its
  database doesn't reference.

### Settings

These committed settings name the tenant, the storage account and the GitHub repository. Change them when
deploying your own copy.

| File | Setting | Value |
| --- | --- | --- |
| `ModsDude.Server.Api/appsettings.json` | `EntraExternalId:Domain` | `<tenant>.onmicrosoft.com` |
| | `EntraExternalId:ClientId`, `Audience` | The API's application (client) ID |
| | `EntraExternalId:Authority` | `https://<tenant>.ciamlogin.com/<tenant ID>` |
| | `SwaggerAuthentication:ClientId` | The Swagger UI's application (client) ID |
| | `SwaggerAuthentication:Scope` | The API's scope, `<Application ID URI>/<scope>` |
| | `ClientDownload:GithubRepository` | `https://github.com/<owner>/<repo>` |
| `ModsDude.Server.Api/appsettings.Development.json` | `Storage:StorageAccountName` | The development storage account |
| `ModsDude.Client.Wpf/appsettings.json` | `Authentication:ClientId` | The client's application (client) ID |
| | `Authentication:Authority` | Same as `EntraExternalId:Authority` |
| | `Authentication:Scope` | Same as `SwaggerAuthentication:Scope` |
| | `Updates:GithubRepository` | Same as `ClientDownload:GithubRepository` |
| `ModsDude.Client.Wpf/appsettings.Production.json` | `ModsDudeServer:BaseUrl` | `https://<SITE_ADDRESS>` (see [Production](#production)) |

## Local development

### Prerequisites

- Windows, for the client. The server also builds and runs on Linux and macOS.
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/), for PostgreSQL. A local PostgreSQL 17 works too.
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell)
- The NSwag CLI: `dotnet tool install --global NSwag.ConsoleCore`

### One-time setup

1. Trust the ASP.NET Core development certificate. The client talks to the API over HTTPS.
   ```bash
   dotnet dev-certs https --trust
   ```
2. Start PostgreSQL with the credentials `appsettings.Development.json` expects:
   ```bash
   docker run -d --name modsdude-postgres --restart unless-stopped -e POSTGRES_PASSWORD=postgres -p 5432:5432 -v modsdude-postgres:/var/lib/postgresql/data postgres:17
   ```
   The API creates the `modsdude-dev` database and applies migrations when it starts.
3. Sign in to Azure as a user with Storage Blob Data Contributor on the development storage account. The
   API uses this login through `DefaultAzureCredential`.
   ```bash
   az login
   ```
4. Set a password for the admin pages. The username is `admin`. Without a password nobody gets in.
   ```bash
   dotnet user-secrets set "Admin:Password" "<password>" --project ModsDude.Server/ModsDude.Server.Api
   ```
5. Optional: stop the dev API from crawling farming-simulator.com every hour.
   ```bash
   dotnet user-secrets set "ModHub:Enabled" "false" --project ModsDude.Server/ModsDude.Server.Api
   ```

### Running

Start the API, then the client:

```bash
dotnet run --project ModsDude.Server/ModsDude.Server.Api
```

```bash
dotnet run --project ModsDude.Client/ModsDude.Client.Wpf
```

The API listens on `https://localhost:7035`:

| Path | What |
| --- | --- |
| `/swagger` | Swagger UI. Development only; sign in with the Authorize button. |
| `/admin` | Admin page: trust codes. |
| `/admin/jobs` | Hangfire dashboard. |
| `/health` | The running build number. |

A Debug build of the client runs as the **Development** install, separate from an installed copy:

- Settings, sign-in and state: `%LocalAppData%\ModsDude.Development`
- Logs: `%LocalAppData%\ModsDude.Development\logs`. Set `Logging:MinimumLevel` in the client's
  `appsettings.json` for more detail.
- Set `DOTNET_ENVIRONMENT` to run a build as another install.

A local build of the API and the client is build 0. Each refuses to talk to any other build number, so a
local client can't reach the production server.

### First use

Creating a repo requires a trusted user. Create a trust code on `/admin` and redeem it in the client. Other
users join through repo invites.

### Tests

```bash
dotnet test ModsDude.Server/ModsDude.Server.Domain.Tests
dotnet test ModsDude.Server/ModsDude.Server.ModHub.Tests
dotnet test ModsDude.Server/ModsDude.Server.Persistence.Tests
dotnet test ModsDude.Client/ModsDude.Client.Core.Tests
```

- The persistence tests need PostgreSQL. They **drop and recreate** the database in
  `MODSDUDE_TEST_DATABASE`, which defaults to `modsdude-tests` on the local server above.
- The client tests need Windows: the content store uses hardlinks and the Recycle Bin.

### Changing the API

The client's API code (`ModsDude.Client.Core/ModsDudeServer/Generated.cs`) is generated from the
checked-in OpenAPI document. Never edit it by hand. After a change to the API:

```bash
pwsh scripts/openapi.ps1 -Update
cd ModsDude.Client/ModsDude.Client.Core
nswag run nswag-config.nswag
```

The script builds the API to write the document. Nothing needs to be running. CI fails when
`openapi/v1.json` doesn't match the API.

### Database migrations

Add a new migration for every model change:

```bash
dotnet ef migrations add <Name> --project ModsDude.Server/ModsDude.Server.Persistence --startup-project ModsDude.Server/ModsDude.Server.Api
```

Install the tool with `dotnet tool install --global dotnet-ef`. The dev API applies migrations on startup;
production applies them during the deploy.

## Production

One Linux server runs three containers from `deploy/compose.yml`:

- **api**: the image CI builds.
- **db**: PostgreSQL 17.
- **caddy**: serves `SITE_ADDRESS` over HTTPS with a Let's Encrypt certificate it gets by itself, proxies
  to the API and serves the landing page in `deploy/caddy/site`.

Blobs live in the production storage account. Clients install and update from the repository's GitHub
Releases.

Everything on the server lives in `~/modsdude` of the `modsdude` user:

| File | Written by |
| --- | --- |
| `.env` | You, once. The server's settings and secrets; see `deploy/.env.example`. |
| `compose.yml`, `caddy/`, `systemd/`, `deploy.sh`, `prune-images.sh`, `backup.sh` | Every deploy, from `deploy/`. |
| `migrate.sql` | Every deploy, generated by CI. |

Data lives in the Docker volumes `modsdude_db` (the database) and `modsdude_caddy-data` (the
certificates). The database is backed up every hour and every day at 03:00 Swedish time to the backup
storage account, which keeps hourly backups for 2 days and daily ones for 30. The admin page shows the
newest of each.

### Azure

The server signs in to storage as a service principal:

1. In the tenant that owns the storage account (not the External ID tenant), register an application such
   as `modsdude-vps`.
2. Give it Storage Blob Data Contributor on the production storage account.
3. Create a client secret. Its value, the application (client) ID and the directory (tenant) ID go in
   `.env`.

Client secrets expire. When it has, uploads fail and the API logs that it could not reach the containers
at startup. Create a new secret, put it in `.env` and run `docker compose up --detach api`.

### Backups

A restored database refers to blobs that may have been deleted since, so the production storage account
can be put back to the same moment. On the production storage account, under Data protection:

1. Turn on soft delete for blobs, kept for 31 days.
2. Turn on versioning, the change feed, and point-in-time restore for 30 days.
3. Under Lifecycle management, add a rule that deletes previous versions 31 days after they became
   previous. Without it, every deleted blob is kept forever as a version.

The backups go to a storage account of their own, so the server's credentials, which may delete anything
in the production account, can't touch them:

1. Create a storage account such as `modsdudebackups`, and in it a container named `db-backups`. Its name
   is `BACKUP_STORAGE_ACCOUNT_NAME` in `.env`.
2. Under Lifecycle management, add two rules for block blobs: delete blobs with the prefix
   `db-backups/hourly/` 2 days after they were created, and with `db-backups/daily/` after 30 days.
3. On the container, under Access policy, add a stored access policy with only Create, expiring years
   from now. Under Shared access tokens, generate a SAS signed with an account key and that policy. Its
   Blob SAS URL is `BACKUP_SAS_URL` in `.env`. Create can't overwrite or delete, and deleting the policy
   revokes the SAS.
4. Give `modsdude-vps` Storage Blob Data Reader on the backup account, for the admin page.

At [healthchecks.io](https://healthchecks.io), add two checks that email when a backup is overdue or fails:

| Check | Period | Grace | `.env` |
| --- | --- | --- | --- |
| Hourly | 1 hour | 1 hour | Its ping URL is `BACKUP_PING_HOURLY` |
| Daily | 1 day | 2 hours | Its ping URL is `BACKUP_PING_DAILY` |

A deploy refuses to start while any of these settings is missing from `.env`.

### Setting up the server

Debian. As root:

1. Update, and let only SSH and the web in:
   ```bash
   apt update && apt upgrade -y
   apt install -y ufw
   ufw allow OpenSSH
   ufw allow 80,443/tcp
   ufw allow 443/udp
   ufw enable
   ```
   Docker opens published ports past ufw, which is why only Caddy publishes any.
2. Install Docker from Docker's repository. These commands are from
   <https://docs.docker.com/engine/install/debian/>; check there if they fail.
   ```bash
   apt install -y ca-certificates curl
   install -m 0755 -d /etc/apt/keyrings
   curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
   chmod a+r /etc/apt/keyrings/docker.asc
   echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian $(. /etc/os-release && echo "$VERSION_CODENAME") stable" > /etc/apt/sources.list.d/docker.list
   apt update
   apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
   ```
3. Make a deploy key pair on your own machine. It needs no passphrase, because CI uses it.
   ```bash
   ssh-keygen -t ed25519 -f deploy_key -N ""
   ```
4. Create the user CI deploys as. Membership of the `docker` group is as good as root on this machine.
   ```bash
   adduser --disabled-password --gecos "" modsdude
   usermod -aG docker modsdude
   mkdir -p /home/modsdude/.ssh
   echo '<contents of deploy_key.pub>' > /home/modsdude/.ssh/authorized_keys
   chmod 700 /home/modsdude/.ssh
   chmod 600 /home/modsdude/.ssh/authorized_keys
   chown -R modsdude:modsdude /home/modsdude/.ssh
   loginctl enable-linger modsdude
   ```
   Linger keeps `modsdude`'s systemd units, the backup timers, running while nobody is logged in.
5. As `modsdude` (`su - modsdude`), create `.env` from `deploy/.env.example` and fill it in:
   ```bash
   mkdir ~/modsdude
   nano ~/modsdude/.env
   chmod 600 ~/modsdude/.env
   ```
6. Print the server's host key as a `known_hosts` line, for `DEPLOY_KNOWN_HOSTS` below. Use the server's
   address exactly as it will be in `DEPLOY_HOST`:
   ```bash
   awk -v host=<DEPLOY_HOST> '{print host, $1, $2}' /etc/ssh/ssh_host_ed25519_key.pub
   ```

Then, outside the server:

7. Point `SITE_ADDRESS` at the server in DNS. Open 22, 80 and 443 (TCP, and UDP for 443) in the hosting
   provider's firewall if it has one.
8. Set `ModsDudeServer:BaseUrl` in `ModsDude.Client.Wpf/appsettings.Production.json` to
   `https://<SITE_ADDRESS>` and commit it. CI refuses to release a client that points at localhost.
9. In the GitHub repository's Settings → Secrets and variables → Actions, add these variables:

   | Variable | Value |
   | --- | --- |
   | `SITE_ADDRESS` | The same domain as in `.env` |
   | `DEPLOY_HOST` | The server's address |
   | `DEPLOY_USER` | `modsdude` |
   | `DEPLOY_KNOWN_HOSTS` | The line from step 6 |

   And one secret: `DEPLOY_SSH_KEY`, the contents of `deploy_key`, the private half. Make a new pair if it
   is lost.
10. Push to main, or re-run the latest CI run on main. The first deploy creates the database and gets the
    certificate.

The repository must be public: installed clients read releases from it without a token, and the API's
`/download` route redirects browsers to the installer of the release built with it.

### Day to day

As `modsdude`, in `~/modsdude`. After `su - modsdude`, rather than signing in over SSH, `systemctl --user`
and `journalctl --user` first need `export XDG_RUNTIME_DIR=/run/user/$(id -u)`.

| What | Command |
| --- | --- |
| What is running | `docker compose ps` |
| Follow the API's log | `docker compose logs --follow api` |
| Restart the API | `docker compose restart api` |
| A database prompt | `docker compose exec db psql --username modsdude` |
| Back up the database now, as an hourly backup | `systemctl --user start modsdude-backup@hourly` |
| When the backups last ran and run next | `systemctl --user list-timers` |
| The backups' log | `journalctl --user --unit 'modsdude-backup@*'` |
| Update PostgreSQL and Caddy within their major versions | `docker compose pull db caddy && docker compose up --detach db caddy` |

The admin page is at `https://<SITE_ADDRESS>/admin` and the Hangfire dashboard at
`https://<SITE_ADDRESS>/admin/jobs`. Sign in as `admin` with `ADMIN_PASSWORD`.

### Moving to another server

1. Set up the new server up to and including step 6.
2. On the old server, stop the API and dump the database:
   ```bash
   cd ~/modsdude
   docker compose stop api
   docker compose exec -T db pg_dump --username modsdude --format custom modsdude > ~/modsdude.dump
   ```
3. Copy `modsdude.dump` to `/home/modsdude/` on the new server.
4. Do steps 7, 9 and 10 for the new server. Its database is then migrated but empty.
5. On the new server, replace it with the dump:
   ```bash
   cd ~/modsdude
   docker compose stop api
   docker compose exec -T db pg_restore --username modsdude --dbname modsdude --clean --if-exists --no-owner < ~/modsdude.dump
   docker compose start api
   ```
6. Once everything works, stop the old server's backups and services. Its volumes stay until
   `docker compose down --volumes`.
   ```bash
   systemctl --user disable --now modsdude-backup-hourly.timer modsdude-backup-daily.timer
   docker compose down
   ```

### Restoring from a backup

1. Stop the API, so nothing changes while the database and the blobs are put back:
   ```bash
   cd ~/modsdude
   docker compose stop api
   ```
2. In the backup account's `db-backups` container, pick the backup and download it to `/home/modsdude/`
   as `modsdude.dump`. Note its Last modified time: the dump was complete by then.
3. On the production storage account, under Data protection, restore all containers to that Last modified
   time with point-in-time restore. Not earlier: blobs uploaded while the dump ran would be missing. Later
   is safe, because the blob reclamation job removes what the database doesn't refer to. Wait for the
   restore to finish.
4. Restore the database and start the API:
   ```bash
   docker compose exec -T db pg_restore --username modsdude --dbname modsdude --clean --if-exists --no-owner < ~/modsdude.dump
   docker compose start api
   ```

### Upgrading PostgreSQL

A new major version can't read the old one's data:

1. Stop the API and dump the database, as in step 2 of moving.
2. Change the image version in `deploy/compose.yml`, and the volume's path in the container if the image
   moved its data folder (as `postgres:18` did).
3. Remove the `modsdude_db` volume.
4. Deploy, then restore the dump as when moving.

## CI and releases

`.github/workflows/ci.yml` runs on every push and pull request:

- **Server** (Linux): builds the API, runs the domain, ModHub and persistence tests against a PostgreSQL
  service container, and checks that `openapi/v1.json` matches the API.
- **Client** (Windows): builds the whole solution and runs the client tests.

A push to main also ships, each step only after the previous one succeeded:

1. **Build number**: N is the number of commits on main. It refuses to ship a build older than the latest
   release.
2. **Publish the API**: a container image stamped with N, and an idempotent migration script.
3. **Deploy the API**: uploads both to the server, migrates the database, swaps the API and waits until
   `/health` reports build N. A failed migration leaves the running API untouched.
4. **Release the client**: a self-contained Velopack package published as GitHub release `bN`. Installed
   clients update to it.

The API and the client from the same commit share N, and refuse to talk to any other build.

## Licence

See [LICENSE.txt](LICENSE.txt).
