# Client

The client is a WPF app (`ModsDude.Client.Wpf`) on top of a UI-free core (`ModsDude.Client.Core`).
Game adapters, stores, sync, import and savegames are covered on their own pages. This page covers the
app around them.

## How it works

### Installs and startup

Velopack installs the app per user. `Program.Main` runs Velopack's hooks before WPF starts.

- Each environment gets its own data folder (`ModsDude.<Env>`, with production as plain `ModsDude`).
  Settings, state, logs, caches and the token cache live there. The environment comes from the build
  configuration, because WPF has no launch profiles.
- The Velopack package ID (`ModsDude.Client`) differs from the data folder on purpose, so uninstalling
  keeps the user's data. The uninstall hook removes only what the app left outside its folders, such as
  the Start Menu shortcut.
- Startup sweeps crash leftovers (staging folders, temporary files) and signs in silently. If startup
  fails, a plain message box explains it, because the window may not exist yet.
- Logs go to rolling files in the data folder.

### Updates

`AppUpdater` checks for updates 20 seconds after start and every 4 hours, and downloads them on its own.
It never restarts the app by itself: an "update ready" notice offers to restart. Once the server is on a
newer build, the client checks every minute. When CI posts an update announcement, open clients show a
notice that the server is about to move on. A client on the wrong build gets a full-window "please
update" overlay (see [Server](server.md#build-match)).

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

A repo has sections (Overview, Profiles, Mods, Savegames, Members), and the ones its adapter doesn't
support are left out. Connect game and Configure game are sub-pages of Overview. A profile has tabs:
Overview, Mods (the editor) and History.

- `NavigationManager` builds pages lazily when they are selected. A page can refuse to be left, for
  example while it has unsaved changes. Sub-pages use `Parent` and `GoBack`.
- Entries the user's level doesn't allow are **disabled with a tooltip, not hidden**
  (`MenuItemViewModel.Restrict`).
- Home shows the user's repos, with friends' activity grouped by profile.
- Names sort naturally (`Mod 2` before `Mod 10`), but only names people read; machine data sorts
  ordinally.

### Kept-alive pages

A page implementing `IRetainablePage` is kept alive, hidden, while it holds unsaved work or a save in
flight (`RetainedPages`). The profile mod list editor is the main one. Coming back finds it as it was.
The tab and the rail entry show an attention dot. Kept pages are listed when quitting or switching user.

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

**Notices** are built by `NoticeBuilder`, a pure function of the current state, so they are easy to
test. Notices cover:

- drift;
- savegame problems;
- background failures;
- sign-in retries;
- an update ready, or an update announcement;
- friend activity.

The column shows up to six cards:

- One notice is one problem, with one remedy.
- Dismissing a notice is per notice and remembered against what the notice says (`DismissalLedger`,
  per user). If the problem changes, the notice comes back.
- Collapsing the column is not dismissing. New, unseen notices reopen it.
- Background failures that would otherwise be swallowed are counted into one notice
  (`BackgroundProblemSource`), with a 10-minute cooldown after dismissal.

**Modals** stack. Escape declines, even for a destructive confirmation. Enter presses the dialog's own
command.

**The progress strip** shows one task at a time, paged, without jumping to new tasks on its own. Cancel
ignores clicks for the first second, so a double-click doesn't cancel what just started. Rows may also
show their own progress.

### Running in the background

Closing the window hides it to the tray, but only when the tray icon exists; a hidden window with no
icon would be unreachable. In the tray:

- `DriftBackstop` keeps drift checks going.
- `PlaySessionWatch` notices a game exiting.

Windows toasts are sent per kind, each switchable in Settings: drift, check-in reminders, finished
actions, friend activity. Drift toasts are for critical and warning drift only, once per notice. Clicking
a toast never applies anything; it opens the app.

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
read stores, and never call the API to refresh themselves. A store load is a promise (`StoreLoads`),
so several readers share one request.

`ChangePoll` polls `GET changes` every minute, whether or not the window is visible. It re-reads only
the stores whose counters moved. A poll where any read failed keeps the old counters, so the next poll
tries again. It also reads the profiles and savegames that local drift checks depend on, even when no
page holds them. That is how a user hears that their save was taken over.

`ObservableCollectionSynchronizer` updates bound collections in place, so selection and scroll survive
a refresh. `LatestLoad` drops results from superseded selection-driven loads.

Server errors are matched by problem type, never by status code.

### Sessions, quitting and switching user

`SessionWork` owns one cancellation for all long-running work in the current session.

- **Quitting.** The window's close button, the tray menu, restarting for an update and the build
  mismatch overlay all go through `AppLifetime.QuitAsync`. If work is running, unsaved changes exist or
  pages are kept alive, one confirmation lists them. Confirming stops all work (waiting up to 15 seconds),
  reports this user as offline, and exits. While work is at stake, Windows shutdown is blocked with a
  reason.
- **Switching user.** `AccountSwitcher` opens the browser sign-in first. Only once the new account is
  confirmed does it ask the same question, stop the work, and adopt the new account. The old session
  is never dropped before the new one exists. Per-user state (`IUserScopedState`) is rebuilt.
- **Sign-in.** Tokens are refreshed silently. A browser opens only when the user asks to sign in. There
  is no sign-out.

### Local state

`Store<T>` persists one JSON file under one lock:

| File | Holds |
| --- | --- |
| `ClientSettings` | Machine-wide settings: content stores and limits, image cache size, transfer speed limits, notification switches |
| `LocalState` | Per-user state, including the items below |

`LocalState` holds:

- the games, keyed by `GameIdentity`, with local settings, active profile, pinned revision and recorded
  targets;
- savegame bindings and pending publishes;
- transfers and background state;
- what friend activity has been seen.

Sync manifests are separate files, one per target.

- Files are **versioned and migrated**. The original is copied aside, then carried forward one version
  at a time.
- A file this build can't read (newer, too old, or corrupt) is moved aside and logged, never written
  over.
- A failed write is dropped from memory too, so memory never runs ahead of disk.
- Adapter display names are never stored, because a stored copy goes stale.

### Diagnostics

Every caught failure is logged with context. Failures the user doesn't need to act on are absorbed into
the background-problem notice, not swallowed. The error modal never shows raw exception text.

## Decisions

**The UI is built from derived state.** Pages read stores; notices are a pure function of state; the poll
moves stores. Nothing calls "refresh" by hand, which is where "doesn't update until I click something"
bugs come from.

**One poll of change counters, not watchers per page.** One cheap request a minute keeps every store
current. Pages that aren't open cost nothing.

**Disabled, not hidden.** A missing button leaves people wondering where it went. A disabled one with a
tooltip says why.

**Kept-alive pages instead of navigation locks.** Locking navigation trapped people on a page. Keeping the
page alive lets them leave and come back, and quitting still warns them.

**One exit path.** Every way of quitting asks the same question and stops work the same way, so nothing is
cut off halfway without warning.

**New sign-in before dropping the old.** A cancelled or failed browser sign-in leaves the current user
signed in, with nothing lost.

**The updater never restarts by itself.** Restarting mid-sync or mid-save would cut off work. The user
picks the moment, and the build-mismatch overlay forces it only when the old build can no longer talk to
the server.

**Uninstalling keeps user data.** The data folder isn't the install folder, so a reinstall picks up where
it left off.

**Local state migrates, never resets.** Bindings and active profiles are local truth that can't be
rebuilt from the server. Losing them can mean losing unsaved play.

**No offline fallback.** Cached server data shown as current would let people act on a stale picture. The
offline state is honest and blocks what can't work.

**Natural sort only for names people read.** Machine data, such as IDs and hashes, sorts ordinally, so
the order is the same everywhere and never depends on culture.
