# Client

Two projects:

- **`ModsDude.Client.Core`** — game adapters, the generated server client, local state,
  domain models, and the three services that do the real work: `ModCatalog`,
  `ModImportService` and `ModSyncService`. No UI framework reference. This is where a future CLI
  or a different shell would plug in.
- **`ModsDude.Client.Wpf`** — the desktop app. Views, view models, navigation, imaging.

`ModsDude.Client.Core.Tests` covers the parts with no UI in them — the version comparer and the
partial order, import, imagery, the content store, the sync planner and drift. It is Windows-only
in CI, because the content store's hardlinks and Recycle Bin are P/Invoke.

## MVVM conventions

CommunityToolkit.Mvvm throughout: `[ObservableProperty]` on private fields,
`[RelayCommand]` on methods, `[NotifyPropertyChangedFor]` / `[NotifyCanExecuteChangedFor]`
for derived state. View models are `partial` so the generator can extend them.

The folder split inside the WPF project is by role, not by feature:

```
View/          XAML, behaviors, imaging, value converters, WPF-only services
ViewModel/     Pages, ViewModels, Windows, and the interfaces the View layer implements
Services/      AuthenticationService (MSAL)
Diagnostics/   The file log sink
```

`ViewModel/Services` holds **interfaces** (`IDialogService`, `IModalService`,
`IModImageProvider`) whose implementations live in `View/`. That keeps the view models
free of `System.Windows` types they do not need — `IDialogService` is just
`string? PickFolder(string? hint)`.

### Nested factories

View models that need runtime arguments expose a nested `Factory`:

```csharp
public class Factory(IServiceProvider serviceProvider)
{
    public RepoPageViewModel Create(Repo repo)
        => ActivatorUtilities.CreateInstance<RepoPageViewModel>(serviceProvider, repo);
}
```

`ActivatorUtilities.CreateInstance` fills the DI-resolvable constructor parameters and takes
the rest positionally, so adding a dependency to a view model does not mean editing its
factory. Factories are registered as singletons in `App.xaml.cs`; the view models
themselves are never registered.

## Composition root

`App.xaml.cs` builds the `ServiceCollection` by hand in `ConfigureServices`. Registration
lifetimes worth knowing:

| Registration | Lifetime | Why |
| --- | --- | --- |
| `MainWindow`, `MainWindowViewModel` | Singleton | The shell |
| `RepoRepository`, `ProfileService`, `MembershipService`, `InviteService`, `LocalInstanceRepository`, `ClientSettingsRepository`, `LastSelectionRepository`, `StateStore` | Singleton | They hold the app's live collections and the persisted state |
| `IModImageProvider`, `ModImageCache`, `IModImageStore`, `IModImagerySource` | Singleton | So decoded thumbnails survive navigating away and back, and one disk cache serves the machine |
| `ModImagePublisher` | Singleton, and registered as both `IModImagePublisher` and `IModImageBackfill` | One object, two roles: publishing at import and backfilling on demand |
| `NavigationLockService` | Singleton | One global "there are unsaved changes" flag |
| `NavigationManager` | **Transient** | Each nesting level owns its own |
| `AuthenticationService` | Singleton, and registered as `IAccessTokenAccessor` | |
| `AccountViewModel` | Singleton | Switching user replaces the shell it is drawn in |
| `IUserScopedState` | Two singleton aliases, onto `RepoRepository` and `ProfileService` | What the shell drops when the signed-in user changes |
| `IModalService` | Resolves to `MainWindowViewModel` | The shell owns the modal slot |

`OnStartup` shows the window first and only then awaits the first token acquisition, so the
UI is up while MSAL possibly opens a browser.

`Application_DispatcherUnhandledException` is the global net: it marks the exception handled,
wraps anything that is not already a `UserFriendlyException`, and shows an error modal. This
is why view models can let exceptions propagate rather than try/catching everywhere. It logs
first: the wrapping loses the original stack, so what reaches the log is what actually arrived.

## Diagnostics

A WPF app has no console, so an `ILogger` with nothing behind it is the same as no logger at all.
`ConfigureServices` registers `FileLoggerProvider` before anything that might want one.

**The log.** One file per day at `%LocalAppData%\ModsDude\logs\client-yyyyMMdd.log`, appended
under a lock, files older than fourteen days dropped at startup. A write that fails is dropped:
logging must never be the thing that takes the app down. The minimum level comes from
`Logging:MinimumLevel` in `appsettings.json` and defaults to `Information`, so chasing something
that only speaks at `Debug` is an edit rather than a rebuild. `System.Net.Http` is held at
`Warning` — the typed clients log a line per request and would bury everything worth reading.

It is a few dozen lines, not a logging framework. A desktop client writing a few hundred lines a
session does not need one, and a dependency that has to be configured before it works is a
dependency that ships misconfigured.

### Nothing reaches the user without reaching the log

**`IErrorReporter` is the one way a failure becomes a dialog.** It writes the exception to the
log, stamps the moment it did, and returns an `ErrorDialogViewModel` carrying that stamp. Showing
and logging used to be separate acts, so they could disagree — and did: half a dozen catch blocks
built an error dialog and wrote nothing, which meant the failures a user was most likely to ask
about were the ones with no record behind them.

Every caller passes a short `context` — "saving the profile", "checking a savegame in" — because
it is the log's only clue about which of a page's several operations this was.

**`ErrorDialogViewModel` is separate from `ConfirmationDialogViewModel` on purpose.** The
confirmation dialog is generic: it still asks questions, reports refusals and lists validation
errors. None of those are faults, none has a log line to point at, and offering "Open log folder"
on "Really delete this?" would be offering a dead end. The error dialog has one button, the
timestamp, and the link — `LogFolder.TryOpen`, shared with the background-problem notice so there
is one place that knows where the folder is.

The timestamp is what makes the folder worth opening. It is written in the same format as a log
line's own prefix, so it can be searched for; by the time somebody reads a dialog, decides to
report it and finds the folder, "just now" has stopped being an answer.

**Three ways out of the process, all covered.** `Application_DispatcherUnhandledException` goes
through the reporter. `TaskScheduler.UnobservedTaskException` and
`AppDomain.CurrentDomain.UnhandledException` are wired in `OnStartup` and log only — the first
fires on a finalizer thread long after the fact, and the second while the runtime is on its way
down, so neither has anything left to interrupt. The unobserved one is marked observed:
otherwise a fire-and-forget continuation that already cost whatever it was going to cost takes
the process with it.

**Core logs its own absorbed failures.** `Client.Core` had no logger anywhere, so a discarded
`state.json`, an unreadable sync manifest, a mod source that could not be scanned and every mod
an import refused were all invisible. The rule is: **an exception that is neither rethrown nor
wrapped-and-rethrown is logged before it is discarded**, at the level its consequence deserves —
`Warning` where something the user can see is now wrong or missing, `Debug` where the cost is a
re-fetch or a leftover temp file. `ModImportService` logs at `Record`, the one funnel every
unfinished item passes through, which is why the four catch sites above it write nothing
themselves.

Two deliberate exceptions, both OS capability probes rather than swallowed failures: `FileLinks`
and `KnownFolders` asking for APIs that may not exist, and `ModSyncPlanner` failing to hash a file
the running game holds open. Where the *outcome* of one matters it is logged once by the caller
instead — `ModSyncService` records falling back from hardlinks to copying.

**The game adapters log too.** They were the awkward case: they are built by capability factories
rather than by the container, so there was no obvious seam for a logger. There is one — the
Farming Simulator adapter is a DI singleton like every other `IGameAdapter`, so it takes an
`ILoggerFactory` and hands it down through `WithBaseSettings` and `WithInstanceSettings` to the mod
and savegame adapters it builds. No interface changed; the capability lists stopped being static,
which is the whole cost.

It was worth it because those adapters degrade rather than throw, by design, and a degraded result
is indistinguishable from an ordinary one. A slot whose career file will not parse reads as
"a save this game will not name" — a `Warning`, because the user can see something is wrong and had
no other way to find out why. A folder scan tells three outcomes apart, which is what makes
its one `Warning` meaningful: a file that is not shaped like a mod archive is never opened, a zip
that carries no `modDesc` is a determination rather than a fault, and only an archive that *should*
have been readable and was not gets logged - in a mod folder that is a mod which has silently left
the catalog.

### Absorbed is not hidden

Several paths swallow failures on purpose, and the reasoning is sound in every case: an error
modal per row during an import of 2,000 mods is unusable, and imagery is decoration. What does
not follow is that the user should never find out. Absorbed failures are logged, and the ones
with a consequence somebody can see are counted into a shell notice.

| Site | Log | Notice |
| --- | --- | --- |
| `ModImagePublisher` — a version's imagery did not reach the server | `Error`, or `Warning` with the HTTP status for a refused upload | `ImageUpload` |
| `ModImagePublisher` — the archive yielded no readable imagery | `Debug` | — |
| `ModImageProvider` — an image will not load or decode | `Warning` | `ImageDisplay` |
| `ModImageProvider` — unreadable cache entry, or a cache write that failed | `Debug` | — |
| `ModListItemViewModel` — a row's imagery could not be resolved | `Warning` | `ImageDisplay` |
| `LazyLoad` — a deferred load threw | `Warning` | `DeferredLoad` |
| `AccountViewModel` — the identity fetch behind the tag and avatar colour | `Debug` | — |

The `Debug` rows are the ones that cost nothing: a re-decode, or a label that was already correct.
They stay out of the notice deliberately, and a mod that simply ships no pictures has to be
distinguishable in the log from one whose upload failed — which is the whole reason it is logged
at all.

**`BackgroundProblemViewModel`** is the notice, and `IBackgroundProblemReporter` is what the
absorbing sites talk to; the container registers one object under both. It counts reports by kind
and draws one card in the shell, under the drift notice and deliberately quieter than it — drift
risks a savegame and offers to fix it, this one has nothing to offer but the truth and a button
that opens the log folder. Aggregated rather than raised per failure: a storage container that
does not exist is one problem seen 2,000 times, not 2,000 problems. Dismissal starts a
ten-minute cooldown rather than silencing the session; counting continues underneath, so a
problem that is still happening comes back with the full total.

`LazyLoad` is the one service-locator seam in the app: an attached behaviour is constructed by
XAML and has no constructor for the container to reach, so `App.OnStartup` hands it a logger and
the reporter through `LazyLoad.UseDiagnostics`. Both are null in a designer, and every use is
conditional.

## Navigation

The app is a sidebar app, nested up to three levels deep:

```
MainWindow
└─ MainPage                    Home │ Create repo │ Join repo │ Settings │ ...repos
   └─ RepoPage                 Overview │ Admin │ Members │ Mods │ Create profile │ Connect game │ ...profiles │ ...instances
      ├─ RepoModsPage          (one page — the catalog)
      ├─ ProfilePage           Overview │ Mods │ Manage
      └─ InstancePage          Sync │ Manage
```

`RepoModsPage` used to be a shell over Import and Manage. They were sibling pages showing
overlapping data under different rules, which is the main thing about that area that confused;
they are now one page laid out like the profile mod editor - what the sources hold on the left,
what the repo holds on the right, and importing as the move between them rather than a separate
destination. See [09 — Mod catalog](09-mod-catalog.md#manage).

**Both the profile list and the instance list belong to `RepoPageViewModel`**, so they only
appear once a repo is selected and everything in them is scoped to that repo — and therefore to
its adapter. Only one repo's menu is current at a time, since navigating to another repo
disposes the previous `RepoPage`. Anything operating between two entries of that menu can take
compatibility for granted rather than re-checking it.

Each level owns a `NavigationManager` and a collection of `MenuItemViewModel`. A menu item is
a title plus a `Func<PageViewModel>` — **the page is constructed on selection, not up front**,
which is what makes a sidebar of twenty repos cheap.

`MenuItemViewModel` can optionally track its title from a source object's
`PropertyChanged`. Only `InstanceItemViewModel` uses this, following `LocalInstance.Name`,
because instances live entirely client-side and have no server refresh to rebuild them.
Repos and profiles instead get their menu entries rebuilt when the service refreshes from
the server.

`NavigationManager.Selected` is a manual property rather than `[ObservableProperty]` because
selection has to be *refusable*:

1. If `NavigationLockService` holds a lock, ask the user whether to discard changes.
2. If they decline, push the selection back to the previous item — done by setting `null`
   then the previous value, so WPF's `ListBox` actually re-renders the selection rather than
   deciding nothing changed.
3. Otherwise dispose the outgoing page, set the new one, and call `TriggerInit()`.

**Disposing the outgoing page matters.** A page constructed and then navigated away from keeps
its initialization running unless it is disposed. `ProfilePageViewModel.Dispose` and
`InstancePageViewModel.Dispose` exist solely to propagate disposal to the sub-page their own
`NavigationManager` owns.

### Drag-selection was a `ListBox` default, not a feature

A WPF `ListBox` extends selection to whatever
the pointer passes over while the button is held, and since selection drives navigation here,
dragging through the sidebar navigated to every item on the way — constructing and discarding a
page each time. The `DragSelection` behavior suppresses it.

The 150 ms scan delay in `ModCatalog` and the dispose-on-navigate discipline both predate that
fix and both stay: the delay because a page nobody stopped on should still never touch the disk,
and disposal because it is correct regardless.

Suppressing pointer-following selection is also the prerequisite for any drag *gesture* in the
sidebar — once a drag passes the threshold and `DragDrop.DoDragDrop` captures the mouse, the
`ListBox` stops receiving move events and selection stops following. Nothing uses that yet; see
[PLAN.md](PLAN.md#phase-5--fill-in-the-shell).

### The navigation lock

`NavigationLockService` is a single global slot holding whichever page has unsaved changes.
Pages acquire it from their `OnXChanged` partial methods and release it on save. It throws if
a second page tries to acquire while another holds it — an assertion that only one editor can
be dirty at a time, which the nesting model guarantees.

"Unsaved changes" is not only a text box. `RepoMembersPageViewModel` takes the lock while any
member's level picker shows something the server has not been told about, and releases it the
moment nothing is pending — which a save, a **Discard** and a reload all reach through the same
`RecountPendingLevelChanges`. Without it, picking a new level and clicking to another repo threw
the change away silently.

## Page lifecycle

`PageViewModel.TriggerInit()` starts two independent initializations:

```csharp
// Quick, UI-related work on the dispatcher at DispatcherPriority.Loaded
Application.Current.Dispatcher.BeginInvoke(() => Init());

// Everything expensive off the UI thread
Task.Run(async () =>
{
    await InitAsync().ConfigureAwait(false);
    Application.Current.Dispatcher.BeginInvoke(OnInitCompleted);
});
```

| Override | Runs on | For |
| --- | --- | --- |
| `Init()` | UI thread | Kicking off commands, cheap setup |
| `InitAsync()` | Thread pool | Network calls, disk scans |
| `OnInitCompleted()` | UI thread | Publishing what `InitAsync` produced — anything that must touch WPF objects |
| `OnInitFailed(ex)` | UI thread | Defaults to rethrowing on the dispatcher, which reaches the global handler and shows the error modal |

The three-part split exists because of `ICollectionView`: it must be created on the UI
thread, but only after the collection it wraps is complete. `RepoModsPageViewModel`
pulls the catalog in `InitAsync` and builds the view once the rows exist.

Overriding `OnInitFailed` without calling `base` suppresses the modal — `RepoModsPageViewModel`
does exactly this for `OperationCanceledException`, because a cancelled scan is the *expected*
result of navigating away, not an error.

## Local state

`Store<T>` (`Client.Core/Persistence/`) is a lazily-loaded, lock-guarded JSON file in the
app data directory. `StateStore` is `Store<LocalState>` over `state.json`.

```
LocalState
├─ Version                 schema version, currently 2
├─ LastSelectedRepos       restores which repo you were on
├─ LastSelectedProfiles    and which profile
├─ Settings                machine-wide, not per repo, instance or adapter
│   ├─ Stores: { volumeRoot → { Path, MaxSizeBytes } }
│   ├─ StoreAssignments: { volumeRoot → servingVolumeRoot }
│   └─ ImageCache: { Path, MaxSizeBytes }   one per machine, not per volume
└─ Instances: { instanceId → { Scope, GameAdapterId, Name,
                               AdapterInstanceSettings,
                               ModFolder,
                               ActiveProfile: (RepoId, ProfileId)? } }

manifests/{instanceId}.json                 what the last sync installed
```

There is no `LocalRepoState` and nothing is keyed by repo. **Instances moved out from under
repos** and key on an `InstanceScope`, so one game
installation is configured once and listed under every repo targeting the same game. The scope
is the adapter id plus — for an adapter serving more than one game — a discriminator its base
settings decide: `_farming_simulator#fs25`. A repo offers the instances whose scope equals its
own. The `GameAdapterId` is stored alongside, recording which adapter version authored the
settings; it is deliberately not part of the scope, so where the compatibility versions differ
the repo's adapter has to be able to read settings authored by the older one, which is what
compatibility versions are for. See
[04 — Game adapters](04-game-adapters.md#instance-scope).

`ModFolder` is recorded rather than asked of the adapter every time, because the
no-two-instances-own-one-folder check has to run across *every* scope — including an instance
whose scope no repo on this machine serves, which cannot hydrate an adapter and still owns its
folder.

`ActiveProfile` has to be persisted: a mod folder cannot tell you which profile it was meant to
match, so nothing can reconstruct it once the contents change. The sync manifest is a separate
file per instance rather than part of `LocalState`, which is loaded eagerly and rewritten on
every instance change — a manifest for 2,000 mods is a few hundred kilobytes. See
[07](07-mod-sync-design.md#what-sync-records-and-why-it-has-to).

**Content store settings are machine-wide.** The store is content-addressed, so it does not
care which game or which repo a file belongs to — a Farming Simulator archive and a BeamNG
archive are both just bytes at an address. Keeping the configuration out of instance and
repo settings is what stops the "same thing configured in several places, then drifting"
problem from reappearing. `LocalState.Settings` was the first genuinely global client setting;
`SettingsPage`, reached from the top-level sidebar, is where it is edited.

There is no migration. The system has no users yet, so `Version` gets bumped and old state
is discarded. `Store<T>` takes an optional compatibility predicate for exactly this, and
`StateStore` passes `state => state.Version == LocalState.CurrentVersion` — without it a bumped
version would silently deserialize old JSON into the new shape rather than discarding it.

Neither a corrupt file nor an incompatible one is fatal: both move the file aside as
`{name}_discarded_{unixMillis}.json` and start fresh, so the cost is the user's instance list
rather than the ability to launch. Saves go through a temp file and an atomic move, so an
interrupted write cannot be the thing that produces a corrupt file in the first place.

`LocalInstanceRepository` owns one `ObservableCollection<LocalInstance>` for the whole machine,
across every scope, and each mutating method — create, update, set the active profile, delete —
saves explicitly. It used to hand out a per-repo collection and subscribe `store.Save()` to
`CollectionChanged`, which persisted adds and removes but silently dropped an edit to an
existing instance's fields.

It also implements `IInstanceModFolders`, which is how store eviction learns which blobs a live
mod folder is relying on — across *every* scope, since a store serves a disk rather than a game.

Alongside `state.json` in the same directory sits `msal_cache.dat`, MSAL's encrypted token
cache, configured in `AuthenticationService.ConfigureTokenCacheAsync`.

## Keeping collections in sync

`ObservableCollectionSynchronizer<TSource, TTarget, TKey>` projects one observable collection
into another — models into view models — and keeps the target **sorted by a key selector**
given as an expression:

```csharp
_reposSynchronizer = new(_repoService.Repos, Repos, MapRepoToVm, x => x.Title, NaturalOrder.Comparer);
```

The expression is both compiled into a key selector and inspected for its property name, so
the synchronizer can subscribe to `PropertyChanged` on each target and re-sort when that one
property changes. Renaming a repo moves it to its new alphabetical position with no
intervention.

`Repo` uses it to project the machine's instances down to the ones matching its own
`InstanceScope`, so the sidebar lists them under each repo without any repo owning them.

Re-sorting **moves** a row rather than removing and reinserting it, and the synchronizer disposes
the target view models it created — `MenuItemViewModel` subscribes to its source's
`PropertyChanged` through the base constructor's title tracking, and nothing else would ever
unsubscribe it.

Every holder of a synchronizer must dispose it — it subscribes to `CollectionChanged` on a
long-lived source, and a leaked subscription keeps a whole page graph alive.

## Names sort naturally

`NaturalOrder.Comparer` is the one comparer behind every sort of a name a person wrote —
mods, profiles, repos, savegames, members, instances — so "Mod 10" comes after
"Mod 9" rather than after "Mod 1". It wraps `StringComparer.CurrentCultureIgnoreCase` with
`NaturalSort.Extension`, which leaves the letters to the culture and reads the digit runs as
numbers.

It is deliberately **not** used for anything the machine reads. File names, content hashes,
volume roots and zip entries stay ordinal, because those orderings are compared against stored
values and must not move when the thread's culture does — `SavegamePacker` and the Farming
Simulator adapter's image ordering are both load-bearing in that way.

Nor is it used where an ordering already exists. Within one mod, the repo's registered
versions are laid out by `SequenceNumber` — the arbitrated answer every member shares — and the
version string decides nothing; it is the last resort for a version with no sequence number,
which is one that is not registered yet. Re-deriving an order from the strings would be a
second opinion, free to disagree with the repo about which version is newest.

The server still orders repos and savegames by name in SQL, where a natural sort is not
available. That is a stable base the client re-sorts on arrival, so nothing depends on the
server's order.

## Server communication

`RepoRepository`, `ProfileService` and `MembershipService` are the service-layer objects. They
hold an `ObservableCollection` of the current data, and after a mutation **apply the returned DTO
to the existing collection** rather than clearing and refetching the lot. The full-list refresh
they used to do discarded and rebuilt every view model, losing selection and scroll position and
costing a round trip for a one-field change — which is why a `*OfInterestChanged` selection dance
had to exist to undo the damage.

They translate `ApiException<CustomProblemDetails>` into `UserFriendlyException` for the cases
they know about, branching on `ex.Result.Type` rather than on the HTTP status code. A problem
type the generated client does not know about cannot be matched, so adding one on the server
means regenerating — see [03 — Server](03-server.md#regenerating-the-client).

`LastSelectionRepository` restores which repo and profile the user was last on, from
`LocalState`.

`ModsDudeClientBase` attaches the bearer token to every outgoing request by calling
`IAccessTokenAccessor.Get`, so token refresh is transparent to callers.

### Authentication

`AuthenticationService` is an MSAL public client against the Entra External ID CIAM
authority, with the `susi_1` (sign-up/sign-in) user flow and `http://localhost` as the
redirect. `Get()` tries silent acquisition first and falls back to interactive on
`MsalUiRequiredException`.

**There is no signing out.** Every surface in the client is a server call, so a signed-out
app has nothing to show and no page to show it on. The only account control is
`SwitchUser()`, surfaced as the sidebar's **Switch user** button: it prompts with
`Prompt.SelectAccount` and, *only once that sign-in has succeeded*, removes every other
account from the token cache. Cancelling the prompt is therefore free — the current user is
still signed in, because nothing was cleared on the way in.

`CurrentAccount` (MSAL's home account identifier plus the username to display) raises
`AccountChanged` when it becomes a *different* account. `Get()` runs on every outgoing
request and adopts the account it acquired for, so the common case raises nothing. The event
is always delivered on the UI thread; MSAL finishes wherever it likes and every listener
rebuilds bound state.

Two things listen. `AccountViewModel` — a singleton, because the shell it is drawn in is what
the switch replaces — shows the name and owns the command, and asks the same
discard-your-changes question a navigation would if `NavigationLockService` holds a lock.
The name it shows is the token's `name` claim — free, instant, and exactly what the server
stores, because the server keeps that claim and rewrites nothing. MSAL's `IAccount.Username` is
the account's *identifier at the provider*, which for this CIAM tenant is an email address and
not a name anybody chose, so it is never shown. What the round trip to `CurrentUserService` →
`GET users/me` is for is the **tag** and the avatar colour built from it, which are derived from
the subject id on the server and cannot be worked out here. The avatar is held back until that
answer arrives rather than drawn in a colour about to change, and the tag itself is only in the
tooltip: there is one user in this panel, so there is nobody to tell them apart from. A failed
fetch is swallowed — the name is already up and correct, what is missing is decoration.
`MainWindowViewModel` treats signing in and switching as one transition: it disposes the
current page, clears every `IUserScopedState`, and builds a fresh `MainPageViewModel`.

`IUserScopedState` is the line between what belongs to the account and what belongs to the
machine. `RepoRepository` and `ProfileService` implement it and are emptied on a switch; local
instances, content stores and the image cache describe the game installations on this PC and
deliberately do not.

## What a level closes, and how it says so

The server refuses what a membership level does not allow, but a 403 arriving after a form has
been filled in is a poor way to learn. Three shapes, chosen by how much of a page is gated:

**The whole page → the sidebar entry is disabled, not hidden.** `MenuItemViewModel.Restrict`
sets `IsAvailable` and the sentence to show instead. The shared `SidebarList` style binds
`ListViewItem.IsEnabled` to it, which refuses mouse and keyboard, and puts the explanation in
the container's tooltip.

Two details are load-bearing. WPF **suppresses tooltips on disabled elements**, so
`ToolTipService.ShowOnDisabled="True"` is required or the explanation is unreachable; and
Fluent's `ListViewItem` has **no disabled appearance of its own**, so a `DataTrigger` dims the
row to `Opacity 0.4` — without it a closed entry looks exactly like an open one. Hiding the
entry was the alternative and was rejected: a guest who cannot see **Members** has no way to
learn that a level exists to ask for.

It is an affordance, not a guard. A disabled container still accepts `SelectedItem` set from
code, so the pages keep their own checks and the server remains the only authority.

| Entry | Closed below |
| --- | --- |
| Main → **Create repo** | `User.IsTrusted` — not a level, and the reason `users/me` carries the flag |
| Repo → **Admin** | Admin |
| Repo → **Members** | Member |
| Repo → **Create profile** | Member |
| Profile → **Manage** | Member |

**Part of a page → the control is disabled, the page stays open.** Only `RepoModsPage`: every
read on it is Guest-level, so browsing, searching and filtering all work, and just Import,
Reorder versions, Delete version and Delete mod are refused. `CanModify` drives their
`CanExecute`, and `ModifyRestriction` is the tooltip — carried to the row menu through
`ModRowActions.Restriction`, since that template is shared.

The **Sources** panel is the exception that is hidden rather than disabled: sources exist to feed
an import and nothing else, so for a guest it would be a column of controls serving one refused
action. Its grid column is `Auto` with the width on the panel itself, so collapsing it gives the
space to the list instead of leaving a 280px hole.

The list itself is filtered to the repo's own mods for a guest — the floor is in `Passes`, not in
a chip, so "All" means all of the repo's — and the **On disk only** chip is hidden, since it
would select a list that is now always empty. An unregistered file on a guest's disk is only ever
interesting as something to import, which they cannot do.

### Saving is one request, and one revision

The editor is a draft until Save, and Save is now a single
`PUT repos/{repoId}/profiles/{profileId}/revisions` carrying the whole list plus the revision the
page was read at. `_basedOn` comes out of the same response the list came from rather than from
the profile DTO, because that is the only form of it that cannot already be stale by the time it
is used.

A refused save — somebody else saved while this page was open — is a question rather than an
error: **load theirs**, which reloads and discards this draft, or **save mine anyway**, which
re-reads the head and retries on top of it. Both are safe, and that is a property of history
rather than of the dialog: what is on the server is a revision either way, so saving over it does
not destroy it.

The footer carries an optional **version description** while there is something to save — Fusion
360's wording for the same field on the same gesture, borrowed because somebody who has used a CAD
package will recognise it. It maps to `ProfileRevision.Label`, which stays neutrally named because
the domain already spends the word "version" on a mod's. It is never required: a field the save
button refused to work without would be answered with "asdf" by the third save. It is cleared as
the save that carried it completes, so it cannot be dragged onto an unrelated edit ten minutes
later.

**A different page for the same entry.** Profile → **Mods** resolves to
`ProfileModsEditorPageViewModel` for a member and `ProfileModsPageViewModel` for a guest. The
menu item holds a `Func<PageViewModel>`, so this is a branch in `ProfilePageViewModel` and
nothing else changes. Closing the entry would have been wrong: a guest can sync the profile, so
what it contains is precisely their question, and the read-only page answers it without the
editor's drag surface having to be switched off control by control.

Different page, same rows. Both render `ModListItemViewModel` through the implicit template, so a
mod looks the same to a guest as it does to a member, its icon loads the same way, and its name is
still the link into the details dialog. What the editor's row carries as controls — the version
selector, the lock toggle, the remove button — the reader's row carries as one report at the end of
it: a lock icon when *this profile* holds the pin. The adapter's own lock is a fact about the
version and is already inside the shared row, so it is not repeated.

That is what `PinnedMod` carries a whole `CatalogModVersion` for. It comes from
`CatalogModVersion.FromRegistered` — the registered half alone, no scan — which is exactly right
here: a reader who cannot import has no use for the local half, and a registered version answers
the row's name, description and imagery on its own.

**There is no "pinned at a version the repo lost".** `ModDependency`'s foreign key onto
`ModVersions` is required and `Restrict`
([02 — Domain model](02-domain-model.md#locking-in-two-places) covers the entity;
`ProfileEntityTypeConfiguration` has the mapping), so the version cannot be deleted while a profile
names it — the delete endpoints refuse with `ModInUse`, and the database refuses underneath them.
`GetPinnedMods` still joins two reads, so a pin can fail to resolve, but only one way round: the
dependency itself was deleted between the dependency read and the later mod-list read. The mod is
no longer in the profile, so the row is dropped rather than captioned.

The editor is the case that looks the same and is not. Its catalog caches the registered half until
something invalidates it, while dependencies are read fresh on every load, so a teammate registering
a version and pinning it leaves this client with a pin it cannot resolve. `Placeholder` covers
exactly that, and keeps the row removable. It is marked `IsOnServer: true` on purpose: the repo does
hold the version, and a row claiming otherwise would read as pending and be handed to the importer
at save with no file to import.

## A refused delete is a list, not a wall

"A profile depends on it" is true and unactionable. `ModDependentsModalViewModel` replaces it: after
the server refuses, the page reads `.../dependents` and shows the profiles, the exact revisions
pinning the mod, and a link into each one — `ShellNavigationService.GoToProfileHistoryAsync` now
takes a revision to open at, so the link lands on the row rather than on the head.

The old flat refusal is still there as the fallback, for a follow-up read that fails or comes back
empty because somebody else edited in between. Being told less beats being told nothing after a
delete that visibly did not happen.

`ProfileHistoryPage` is where the list leads. Ticking revisions and pruning them is **Admin only**,
below Restore and Save-as because it is the one action on that page that destroys rather than adds.
A blocked prune opens `BlockedRevisionsModalViewModel`, which distinguishes the two reasons: the
head is an explanation and nothing more, while a revision a savegame was played on carries links to
that savegame — and the saves page grew an Admin-only *Delete this version* for exactly that,
absent rather than present-and-doomed on the head version.

Both dialogs close themselves before navigating, and regardless of whether navigation was refused: a
page holding unsaved changes is entitled to say no, and reopening a dialog over the page the user is
still standing on would be the app arguing with itself.

## Telling two users with one name apart

Display names are not unique — see
[02 — Domain model](02-domain-model.md#names-are-not-unique-and-nothing-tries-to-make-them).
Two members of a repo can both be called Anton, and both keep the name they chose. The client is
what makes that legible, in `Core/Users/UserDisplay.cs`:

| | |
| --- | --- |
| `FindAmbiguous(users)` | The ids of the users in *this set* who share a name with somebody else in it |
| `ColorFor(tag)` | The avatar colour, hue walked by the golden angle so consecutive tags land far apart |
| `InitialFor(displayName)` | The character on the avatar — the first rune worth drawing, or none |

The important part is that ambiguity is a property of **the list, not the person**, so it is
decided at the moment of rendering. `RepoMembersPageViewModel.Publish` computes it over the rows
it is about to build, exactly as it computes who the only Admin is. A repo where no two members
share a name shows no tags at all — even if the server knows of other people by those names. A
repo where two Antons meet shows the tag on *both* of them, because neither is the Anton and the
other one the duplicate.

The avatar is drawn either way. It is an identity, not a warning, and it is the same colour for
the same person in the member list and in their own account panel.

## Mod imagery

Farming Simulator mod images are almost all DDS, and a mod pack can ship dozens. Three pieces
handle this.

**`ILazyLoadable` + the `LazyLoad` attached behavior.** A view model implementing
`ILazyLoadable` gets `LoadAsync()` called when a virtualizing panel realizes its row —
attached with `b:LazyLoad.Source="{Binding}"` on the item template root. Because WPF recycles
containers, the same visual is handed a new item as it scrolls, which surfaces here as an
ordinary property change; so the behavior covers both first realization and every reuse.
`LoadAsync` must therefore be safe to call repeatedly, and implementations guard with a
`_requested` flag. Failures are absorbed rather than raised: there is no user action to suggest,
and an error modal per row would be unusable. They are logged and counted into the shell notice
all the same — see [Absorbed is not hidden](#absorbed-is-not-hidden).

**`ModImageProvider`.** A singleton with three layers:

- A `ConcurrentDictionary<string, Lazy<Task<ImageSource?>>>` memory cache, keyed
  `{image.CacheKey}|{maxWidth}`. Only images at or below 128px wide are cached — a thousand
  of those is a few megabytes; larger ones are loaded on demand and dropped.
- The disk cache, `ModImageCache` in `Client.Core`, configured in `LocalState.Settings` with its
  own path and size cap, evicting least-recently-used. Windows does not maintain last-access time
  by default and this cache is hot, so last-write stands in for last-used and a hit only
  re-stamps an entry once the timestamp has gone stale.
- A `SemaphoreSlim` throttle at `ProcessorCount / 2`. Scrolling a long list fast queues far
  more decode work than it consumes; bounding it keeps the machine responsive.

The cache deliberately does **not** pass the caller's cancellation token into the shared
task — one row scrolling out of view must not cancel the decode for every other row waiting
on the same image.

A **downloaded derivative skips the derive-and-cache path entirely.** It arrives already at the
size it will be drawn at and already sits in the cache under its own hash, so re-deriving it
would store the same picture twice under a key that can invalidate. That is the difference the
`IsPreSized` flag marks: a local image's key is `{modPath}|{entryName}|{length}|{crc32}|{width}`
and falls out of use when the file changes; a server image's key is the hash and can never
invalidate, so each one crosses the wire once per machine, ever.

**`ModImagerySource`** decides which of those a version gets, and the rule is keyed on
`IsOnServer` rather than on whether the file happens to be on this machine. A registered version
renders from the repo's derivatives even when the mod file is right there; only an unregistered
import candidate is read out of its archive. A registered version with no derivatives whose file
*is* here gets them generated and uploaded — once per version per session — rather than falling
back locally. Full reasoning in
[09 — Mod catalog](09-mod-catalog.md#registration-decides-where-imagery-comes-from).

**`ModImageDecoder`.** Tries the Windows WIC codecs first, which handle the legacy FourCC DDS
formats (every mod icon observed so far is DXT1). WIC refuses BC7, which most *store* images
use, so those fall through to a managed block decoder — measured at 60% of a real 2,656-image
set. Server derivatives arrive as WebP, which WIC only reads where an optional Windows extension
happens to be installed, so they go through the codec the app ships with. Anything that fails
entirely yields the placeholder initials instead.

**`ModImageDerivativeGenerator`** produces the two renditions at import: a 128 px thumbnail and a
full capped at 1024 px, encoded as WebP, resized from the full-resolution pixels rather than from
something already reduced.

## Page inventory

Status: **Working** · **Partial** — usable but incomplete · **Stub** — placeholder content. Read
off the view models rather than off a running app: "Working" here means the page is wired to a
real service and has no placeholder left in it, not that anyone has clicked every button.

| Page | Status | What it does |
| --- | --- | --- |
| `LoginPage` | Working | Shown until the first sign-in completes, and never returned to — there is no signing out |
| `MainPage` | Working | Shell: Home, Create repo, Join repo, Settings, the repo list, and the account panel with **Switch user** |
| `SettingsPage` | Working | Machine-wide settings — per-volume content stores and their assignments, the image cache |
| `CreateRepoPage` | Working | Name + adapter picker + base settings dynamic form |
| `JoinRepoPage` | Working | Paste an invite code. The only way into somebody else's repo |
| `RepoPage` | Working | Repo shell. Auto-selects "Connect game" when the repo has no instances |
| `RepoOverviewPage` | Working | Instance status and profiles at a glance |
| `RepoAdminPage` | Working | Rename repo, edit base settings, delete repo |
| `RepoMembersPage` | Working | Member list with avatars, level changes behind a Save button, Leave on your own row, and the repo's invites - create, copy, revoke, and their join counts |
| `RepoModsPage` | Working | The catalog, as two lists: local candidates and the source list on the left, the repo's mods and whatever is queued to join them on the right. Import, an "unused only" filter, per-row reorder and delete. Browsing is open to a guest, who gets the right-hand list alone; the writing actions are refused with a reason |
| `CreateLocalInstancePage` | Working | Name + instance settings form. Defaults the name to "Game" for the first instance, blocks duplicate names, and refuses a folder another instance owns |
| `InstancePage` | Working | Instance shell over Sync and Manage. Opens on Sync |
| `SyncPage` | Working | Plan preview, the unrecognised-files confirmation, per-mod progress, drift status, cancellation |
| `EditLocalInstancePage` | Working | Name, instance settings, active profile, delete. Phase 4 grows this into the instance's full page — see [PLAN.md](PLAN.md#phase-4--make-drift-unmissable) |
| `CreateProfilePage` | Working | |
| `ProfilePage` | Working | Profile shell over Overview, Mods, History, Manage |
| `ProfileOverviewPage` | Working | Mod count and current revision, plus the instances set to this profile |
| `ProfileModsEditorPage` | Working | The two-list mod editor: available on the left, pinned on the right, updates and locks on the right-hand rows, import on save. Members and admins only |
| `ProfileModsPage` | Working | The same **Mods** entry as a guest sees it: the pinned list, read-only, in the shared list row — name opens the details dialog, and the end of the row says whether the pin is locked and whether the repo still has the version |
| `ProfileHistoryPage` | Working | The profile's revisions on the left; on the right, either what the selected one pinned or what changed between it and another. Restore and Save as… for a member; readable by a guest |
| `EditProfilePage` | Working | Rename or delete a profile |
| `ExamplePage` | — | The placeholder. Still the Home page's content |

Dialogs and shared views:

| Component | Notes |
| --- | --- |
| `Dialog` / `ModalViewModel` | Modals are awaited: `IModalService.Show` returns a `Task` completed by the modal's `Done` flag |
| `ConfirmationDialogViewModel` | Factory methods for `ConfirmDelete`, `ValidationErrors` (truncated past five), and `Error` |
| `ModDetailsDialog` | Full mod description plus the lazily-decoded store-image strip |
| `ModVersionArbitrationDialog` | One dialog per import, covering every mod whose ordering the comparer could not settle. Dismissing it skips only those mods |
| `ModVersionReorderDialog` | Reordering one mod's registered versions by hand, through the placement endpoint |
| `ProfileLockedUpdatesDialog` | The locked mods a batch update skipped, one unchecked checkbox each, reached deliberately rather than fired at every save |
| `ProfileSaveAsDialog` | Names the profile a revision is being branched off into. The name is the only thing there is to ask — which revision was decided by the row it was opened from |
| `DynamicFormEditor` | Renders any `DynamicForm` from its attributes; raises `Modified`, which pages use to take the navigation lock |
| `ModListItem.xaml` | The standard mod row, applied as an implicit template — any items control fed `ModListItemViewModel` renders identically |
| `SidebarHeader` | |

`ModListItemViewModel` is worth a look as the model for list rows: it precomputes a short
description (first line of the mod's own description that is not just the mod's name again)
and initials for the icon placeholder, exposes a `Matches(searchTerm)` used for filtering, and
carries a `ModDisplayStatus`. That last is deliberately *not* the old `ModStatus`, which mixed
the fact "already in the repo" with the context-dependent judgments "new" and "update
available" — the facts are now `IsLocal` and `IsOnServer` on `CatalogModVersion`, and the
display status is computed per context from them. See
[09 — Mod catalog](09-mod-catalog.md#one-identity-two-facts).

An import that did not finish shows as the row's **chip and nothing else**. The warning triangle
that used to sit beside it was the same fact twice, in a row with four other things competing for
the same twelve pixels; the reason moved onto the chip as a tooltip, and is null where there is no
problem, so a row that is merely new does not sprout a tooltip saying nothing went wrong.
