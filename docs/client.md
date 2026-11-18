# Client

The client is a WPF app (`ModsDude.Client.Wpf`) on top of a UI-free core (`ModsDude.Client.Core`).
Game adapters, stores, sync, import and savegames are covered on their own pages. This page covers the
app around them.

## How it works

### Installs and startup

Velopack installs the app per user. `Program.Main` runs Velopack's hooks before WPF starts.

- Each environment gets its own data folder (`ModsDude.<Env>`, with production as plain `ModsDude`).
  State, logs, the image cache and the token cache live there. Each disk's default content store is
  `<disk>\ModsDude[.<Env>]\store`. The environment comes from `DOTNET_ENVIRONMENT`, falling back to the
  build configuration, because WPF has no launch profiles.
- The Velopack package ID (`ModsDude.Client`) differs from the data folder on purpose, so uninstalling
  keeps the user's data. The uninstall hook removes only what the app left outside its folders: the
  start-with-Windows entries, the AUMID registry key and the Start Menu shortcut.
- Startup sweeps crash leftovers (staging folders, temporary files) and signs in silently. If startup
  fails, a plain message box explains it, because the window may not exist yet.
- Logs go to rolling files in the data folder.

### Updates

`AppUpdater` checks for updates 20 seconds after start and every 4 hours, and downloads them on its own.
It never restarts a running app by itself: an "update ready" notice offers to restart, and otherwise the
update is applied at the next start, before any window shows. Once the server is on a newer build, the
client checks every minute. When CI posts an update announcement, open clients show a notice that the
server is about to move on. A client on another build than the server gets a full-window overlay (see
[Server](server.md#build-match)): "please update" when the client is behind, or "the server is out of
date" when the server is.

Releases are unsigned, because code signing isn't available to an individual in the EU. Windows toasts
need a Start Menu shortcut carrying the app's AUMID (`ModsDude`), which the installer creates.

### Shell and navigation

The main window has an icon **rail**:

- Home
- repos, grouped by game
- Join or create
- Archive
- Settings
- Account

A repo has sections (Overview, Admin, Members, Mods, Saves, Archive) and a list of its profiles. Mods
and Saves are left out when the adapter doesn't support them. Connect game and Configure game are
sub-pages of Overview. A profile has tabs: Overview, Mods (the editor, or a read-only list for guests)
and History.

- `NavigationManager` builds pages lazily when they are selected. Sub-pages use `Parent` and `GoBack`.
- A form page with unsaved changes takes a navigation lock (`INavigationLockService`). Leaving it asks
  whether to discard the changes. Settings, repo Admin, Members, Connect game, game settings, Manage
  profile and Create repo work this way.
- Entries the user's level doesn't allow are **disabled with a tooltip, not hidden**
  (`MenuItemViewModel.Restrict`).
- Home shows the user's repos, with friends' activity grouped by profile. Each repo row offers one next
  step: Connect game, Apply (nothing applied yet), Review (drifted; opens the mod editor like the
  notice), "Update to rev N" (every drifted folder only behind its profile, `RevisionGap`) or Open
  profile.
- Names sort naturally (`Mod 2` before `Mod 10`), but only names people read; machine data sorts
  ordinally.

### Kept-alive pages

The profile mod list editor is the one page that implements `IRetainablePage`. Instead of locking
navigation, it is kept alive, hidden, while it holds unsaved work or a save in flight (`RetainedPages`).
Coming back finds it as it was. The profile's Mods tab and its row in the repo's profile list show an
attention dot. Kept pages are listed when quitting or switching user.

### Feedback channels

Each kind of feedback has one channel, as set out in [CLAUDE.md](../CLAUDE.md#uiux):

| Channel | Used for | Implementation |
| --- | --- | --- |
| Inline | Expected failures where the user acted (validation, conflict) | On the form or page |
| Error modal | Something broke while the user was doing it | `IErrorReporter`. Showing it also logs it |
| Notice | A problem in the background that lasts until resolved | Notice column (`NoticeBuilder`, `NoticeCenterViewModel`) |
| Toast | A finished action, foreground or background, with optional undo | `IToastService` |
| Confirmation | Destructive actions, naming what is lost | `ConfirmLossModal` and friends |
| Offline state | Server unreachable | Full-window overlay |
| Progress strip | Long operations | `BackgroundTaskViewModel` |

**Notices** are built from the current state by small pure builders, which `NoticeCenterViewModel`
combines:

| Builder | Notices |
| --- | --- |
| `NoticeBuilder` | Drift, locked mods, savegames, unreachable games and repos, content store corruption |
| `ConnectionRetry` | Sign-in retries |
| `BackgroundProblemSource` | Background failures (image upload, image display, rows) |
| `UpdateNotice`, `UpdateAnnouncementNotice` | An update ready, the server about to move on |
| `FriendActivityRules` | Friend activity |

The column shows up to six cards, with the rest behind "N more":

- One notice is one problem, with one remedy.
- Dismissing a notice is per notice and remembered, in memory, against what the notice says
  (`DismissalLedger`). If the notice changes, or goes away and comes back, it shows again. Dismissals
  are cleared on restart and user switch.
- Collapsing the column is not dismissing. New, unseen notices reopen it.
- Background failures that would otherwise be swallowed are counted, one notice per kind. Dismissing
  them gives a 10-minute cooldown, shared by all of them.

**Modals** stack. Escape declines, even for a destructive confirmation. Enter presses the dialog's own
command.

**The progress strip** shows one task at a time, paged, without jumping to new tasks on its own. When the
Cancel button on screen starts belonging to another task, it ignores clicks for a second, so a
double-click doesn't cancel the wrong task. Rows may also show their own progress.

### Running in the background

Closing the window hides it to the tray when the close-to-tray setting is on (the default) and the tray
icon exists; a hidden window with no icon would be unreachable. In the tray:

- `DriftBackstop` keeps drift checks going.
- `PlaySessionWatch` notices a game exiting.

Windows toasts are sent per kind, each switchable in Settings: drift, check-in reminders, finished
actions, friend activity. Critical and warning notices become one digest toast, replaced whenever a new
one appears, and only while the window isn't in front. Clicking a toast never applies anything; it opens
the app.

### Offline state and reconnecting

The app is useless without the server, so it never falls back to cached data.

- `ConnectionRetry` keeps retrying sign-in and the first load, with backoff. A sign-in outage is a
  notice that can't be dismissed. A server outage is the offline state.
- The offline overlay blocks server-backed actions. `ServerReconnect` and `ReconnectWatcher` notice
  when the server answers again, and rebuild the open page.
- While offline, connection failures don't open the error modal.
- Other full-window overlays cover the build mismatch, a blocked account and a sign-in that needs the
  user.

### Keeping data current

Server data lives in **stores** (`RepoStore`, `ProfileStore`, `ModStore`, `SavegameStore`, ...). Pages
read lists from stores rather than calling the API to refresh themselves. A store load is a promise
(`StoreLoads`), so several readers share one request. Some detail data is still read straight from the
API (see [Known issues](known-issues.md#hygiene)).

`ChangePoll` polls `GET changes` a minute after the previous poll finished, whether or not the window is
visible, and not while offline. It re-reads only the loaded stores whose counters moved. A poll where any
read failed keeps the old counters, so the next poll tries again. It also reads the profiles and
savegames that local drift checks depend on, even when no page holds them. That is how a user hears that
their save was taken over.

`ObservableCollectionSynchronizer` updates bound collections in place, so selection and scroll survive
a refresh. `LatestLoad` drops results from superseded selection-driven loads.

Server errors are matched by problem type, never by status code.

### Sessions, quitting and switching user

`SessionWork` owns one cancellation for all long-running work in the current session.

- **Quitting.** The close button (when not hiding to the tray), the tray menu, the Settings Quit and
  Restart buttons and restarting for an update all go through one exit path (`AppLifetime`). If work is
  running, unsaved changes exist or pages are kept alive, one confirmation lists them. Confirming stops
  all work (waiting up to 15 seconds), reports this user as offline, and exits. While work is at stake,
  Windows shutdown is blocked with a reason.
- **Switching user.** `AccountSwitcher` opens the browser sign-in first. Only once the new account is
  confirmed does it ask the same question and stop the work. If the work doesn't stop within 15 seconds,
  the switch is refused. The old session is never dropped before the new one exists. User-scoped services
  (`IUserScopedState`) are rebuilt.
- **Sign-in.** Tokens are refreshed silently. A browser opens only when the user asks to sign in. There
  is no sign-out.

### Local state

Local state is one file, `state.json`, holding `LocalState`. It is machine-wide: it survives switching
user, and only the friend-activity marker is kept per user. It holds:

- `Settings` (`ClientSettings`): content stores and limits, image cache size, transfer speed limits,
  close-to-tray, notification switches;
- the games, keyed by `GameIdentity`, with name, local settings, active profile, pinned revision and
  recorded targets;
- savegame bindings and pending publishes;
- what friend activity has been seen.

`Store<T>` reads and writes it under one lock:

- The file is **versioned and migrated**. The original is copied aside, then carried forward one version
  at a time. (It is at version 7 with no migrations yet.)
- A file this build can't read (newer, too old, or corrupt) is moved aside and logged, never written
  over.
- A failed write is dropped from memory too, so memory never runs ahead of disk.
- Target display names are never stored, because a stored copy goes stale.

Sync manifests are separate files, one per target, handled by `SyncManifestStore`. An unreadable or
unknown-version manifest reads as missing, which shows as `NeverSynced` drift.

### Diagnostics

Every caught failure is logged with context. Failures the user doesn't need to act on are absorbed into
the background-problem notice, not swallowed. The error modal never shows raw exception text.

## Decisions

**The UI is built from derived state.** Lists come from stores; notices are pure functions of state; the
poll moves stores. Nothing calls "refresh" by hand, which is where "doesn't update until I click something"
bugs come from.

**One poll of change counters, not watchers per page.** One cheap request a minute keeps every store
current. Pages that aren't open cost nothing.

**Disabled, not hidden.** A missing button leaves people wondering where it went. A disabled one with a
tooltip says why.

**The editor is kept alive instead of locking navigation.** A mod list edit can be long, and a lock
would trap people on the page. Keeping it alive lets them leave and come back, and quitting still warns
them. Short forms use a navigation lock instead.

**One exit path.** Every way of quitting asks the same question and stops work the same way, so nothing is
cut off halfway without warning.

**New sign-in before dropping the old.** A cancelled or failed browser sign-in leaves the current user
signed in, with nothing lost.

**The updater never restarts a running app by itself.** Restarting mid-sync or mid-save would cut off
work. The user picks the moment, or it happens at the next start. The build-mismatch overlay forces it
only when the old build can no longer talk to the server.

**Uninstalling keeps user data.** The data folder isn't the install folder, so a reinstall picks up where
it left off.

**Local state migrates, never resets.** Bindings and active profiles are local truth that can't be
rebuilt from the server. Losing them can mean losing unsaved play.

**No offline fallback.** Cached server data shown as current would let people act on a stale picture. The
offline state is honest and blocks what can't work.

**Natural sort only for names people read.** Machine data, such as IDs and hashes, sorts ordinally, so
the order is the same everywhere and never depends on culture.
