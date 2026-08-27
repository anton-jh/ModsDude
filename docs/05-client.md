# Client

Two projects:

- **`ModsDude.Client.Core`** — game adapters, the generated server client, local state,
  domain models. No UI framework reference. This is where a future CLI or a different
  shell would plug in.
- **`ModsDude.Client.Wpf`** — the desktop app. Views, view models, navigation, imaging.

`ModsDude.Client.Cli` exists as an empty leftover directory with no project file.

## MVVM conventions

CommunityToolkit.Mvvm throughout: `[ObservableProperty]` on private fields,
`[RelayCommand]` on methods, `[NotifyPropertyChangedFor]` / `[NotifyCanExecuteChangedFor]`
for derived state. View models are `partial` so the generator can extend them.

The folder split inside the WPF project is by role, not by feature:

```
View/          XAML, behaviors, imaging, value converters, WPF-only services
ViewModel/     Pages, ViewModels, Windows, and the interfaces the View layer implements
Services/      AuthenticationService (MSAL)
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
| `RepoRepository`, `ProfileService`, `LocalInstanceRepository`, `StateStore` | Singleton | They hold the app's live collections and the persisted state |
| `IModImageProvider` | Singleton | So decoded thumbnails survive navigating away and back |
| `NavigationLockService` | Singleton | One global "there are unsaved changes" flag |
| `NavigationManager` | **Transient** | Each nesting level owns its own |
| `AuthenticationService` | Singleton, and registered as `IAccessTokenAccessor` | |
| `IModalService` | Resolves to `MainWindowViewModel` | The shell owns the modal slot |

`OnStartup` shows the window first and only then awaits the first token acquisition, so the
UI is up while MSAL possibly opens a browser.

`Application_DispatcherUnhandledException` is the global net: it marks the exception handled,
wraps anything that is not already a `UserFriendlyException`, and shows an error modal. This
is why view models can let exceptions propagate rather than try/catching everywhere.

## Navigation

The app is a sidebar app, nested up to three levels deep:

```
MainWindow
└─ MainPage                    Home │ Create repo │ ...repos
   └─ RepoPage                 Overview │ Admin │ Members │ Mods │ Create profile │ Connect game │ ...profiles │ ...instances
      ├─ RepoModsPage          Import │ Manage
      └─ ProfilePage           Overview │ Mods │ Manage
```

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

**Disposing the outgoing page matters.** Dragging the mouse down the sidebar constructs and
discards one page per item it passes over; without disposal each of those keeps its
initialization running. `RepoModsPageViewModel.Dispose` exists solely to propagate disposal
to the sub-page its own `NavigationManager` owns.

### Drag-selection is a `ListBox` default, not a feature

That page-per-item behaviour is not deliberate. A WPF `ListBox` extends selection to whatever
the pointer passes over while the button is held, and since selection drives navigation here,
dragging through the sidebar navigates to every item on the way. The 150 ms scan delay in the
import page and the dispose-on-navigate discipline both exist to make that survivable.

Suppressing it is worth doing on its own — nothing wants "navigate to eight pages in
half a second" — and it is also the prerequisite for any drag gesture in the sidebar. Once a
drag passes the threshold and `DragDrop.DoDragDrop` captures the mouse, the `ListBox` stops
receiving the move events and selection stops following. Distinguishing a click from a drag
means the usual `PreviewMouseMove` plus `SystemParameters.MinimumHorizontalDragDistance` /
`MinimumVerticalDragDistance` check.

### The navigation lock

`NavigationLockService` is a single global slot holding whichever page has unsaved changes.
Pages acquire it from their `OnXChanged` partial methods and release it on save. It throws if
a second page tries to acquire while another holds it — an assertion that only one editor can
be dirty at a time, which the nesting model guarantees.

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
thread, but only after the collection it wraps is complete. `RepoModsImportPageViewModel`
scans in `InitAsync` and builds the view in `OnInitCompleted`.

Overriding `OnInitFailed` without calling `base` suppresses the modal — the import page does
exactly this for `OperationCanceledException`, because a cancelled scan is the *expected*
result of navigating away, not an error.

## Local state

`Store<T>` (`Client.Core/Persistence/`) is a lazily-loaded, lock-guarded JSON file in the
app data directory. `StateStore` is `Store<LocalState>` over `state.json`.

```
LocalState
├─ Version                 schema version, currently 1
├─ LastSelectedRepos       declared, not yet used
├─ LastSelectedProfiles    declared, not yet used
└─ Repos: { repoId → LocalRepoState { LocalInstances } }
```

### Where this is going

Two changes are planned, both described in [PLAN.md](PLAN.md#settled-architecture-decisions):

```
LocalState
├─ Version
├─ Settings                              NEW — machine-wide, not per repo or adapter
│   ├─ Stores: { volumeRoot → { Path, MaxSizeBytes } }
│   ├─ ImageCache: { Path, MaxSizeBytes } one per machine, not per volume
│   └─ DisabledSources: { sourceId }     mod sources the user switched off
├─ Instances: { instanceId → { GameAdapterId, Name, AdapterInstanceSettings,
│                              ActiveProfile: (RepoId, ProfileId)? } }
└─ Repos: { repoId → LocalRepoState }
```

**Instances move out from under repos** and key on `GameAdapterId` instead, so one game
installation is configured once and listed under every repo using that adapter. A repo
offers an instance whose adapter `Id` matches; if the compatibility versions differ, the
repo's adapter has to be able to read settings authored by the older one, which is what
compatibility versions are for.

**Content store settings are machine-wide.** The store is content-addressed, so it does not
care which game or which repo a file belongs to — a Farming Simulator archive and a BeamNG
archive are both just bytes at an address. Keeping the configuration out of instance and
repo settings is what stops the "same thing configured in several places, then drifting"
problem from reappearing. `LocalState.Settings` is a new concept: the first genuinely global
client setting.

There is no migration. The system has no users yet, so `Version` gets bumped and old state
is discarded.

A corrupt file is not fatal: a `JsonException` on load moves the file aside as
`state_corrupted_{unixMillis}.json` and starts fresh, so a bad write costs the user their
instance list rather than the ability to launch.

`LocalInstanceRepository` hands out the `ObservableCollection<PersistedLocalInstance>` for a
repo and subscribes `store.Save()` to its `CollectionChanged` — **adding or removing an
instance persists automatically**, with no explicit save call anywhere in the UI. Note that
this fires on add/remove only; editing an existing instance's fields does not trigger a save
by itself.

Alongside `state.json` in the same directory sits `msal_cache.dat`, MSAL's encrypted token
cache, configured in `AuthenticationService.ConfigureTokenCacheAsync`.

## Keeping collections in sync

`ObservableCollectionSynchronizer<TSource, TTarget, TKey>` projects one observable collection
into another — models into view models — and keeps the target **sorted by a key selector**
given as an expression:

```csharp
_reposSynchronizer = new(_repoService.Repos, Repos, MapRepoToVm, x => x.Title);
```

The expression is both compiled into a key selector and inspected for its property name, so
the synchronizer can subscribe to `PropertyChanged` on each target and re-sort when that one
property changes. Renaming a repo moves it to its new alphabetical position with no
intervention.

`Repo` uses it in the other direction too, with `targetAlreadyInitialized: true`: the source
is the live `LocalInstance` list and the target is the persisted models, so mutating the UI
collection writes through to `state.json`.

Every holder of a synchronizer must dispose it — it subscribes to `CollectionChanged` on a
long-lived source, and a leaked subscription keeps a whole page graph alive.

## Server communication

`RepoRepository` and `ProfileService` are the two service-layer objects. Both follow the same
shape: hold an `ObservableCollection` of the current data, and after any mutation call
`Refresh*` and then raise a `*OfInterestChanged` event carrying the id that just changed.
Pages listen for that event and select the matching sidebar item — which is why creating a
repo lands you on the new repo's page.

They translate `ApiException` into `UserFriendlyException` for the cases they know about.
(These checks currently test for HTTP 409, which the server does not return — see
[08 — Known issues](08-known-issues.md).)

`ModsDudeClientBase` attaches the bearer token to every outgoing request by calling
`IAccessTokenAccessor.Get`, so token refresh is transparent to callers.

### Authentication

`AuthenticationService` is an MSAL public client against the Entra External ID CIAM
authority, with the `susi_1` (sign-up/sign-in) user flow and `http://localhost` as the
redirect. `Get()` tries silent acquisition first and falls back to interactive on
`MsalUiRequiredException`. `ForceRelogin()` clears every cached account and prompts for
account selection — this is what the app's "logout" does.

`IsLoggedIn` raises `LoggedInChanged`, which `MainWindowViewModel` uses to swap between the
login page and the main page.

## Mod imagery

Farming Simulator mod images are almost all DDS, and a mod pack can ship dozens. Three pieces
handle this.

**`ILazyLoadable` + the `LazyLoad` attached behavior.** A view model implementing
`ILazyLoadable` gets `LoadAsync()` called when a virtualizing panel realizes its row —
attached with `b:LazyLoad.Source="{Binding}"` on the item template root. Because WPF recycles
containers, the same visual is handed a new item as it scrolls, which surfaces here as an
ordinary property change; so the behavior covers both first realization and every reuse.
`LoadAsync` must therefore be safe to call repeatedly, and implementations guard with a
`_requested` flag. Failures are swallowed: there is no user action to suggest, and an error
modal per row would be unusable.

**`ModImageProvider`.** A singleton with three layers:

- A `ConcurrentDictionary<string, Lazy<Task<ImageSource?>>>` memory cache, keyed
  `{image.CacheKey}|{maxWidth}`. Only images at or below 128px wide are cached — a thousand
  of those is a few megabytes; larger ones are loaded on demand and dropped.
- A PNG disk cache under `LocalAppData/ModsDude/image-cache`, named by a truncated SHA-256 of
  the key, written via a temp file and atomic move.
- A `SemaphoreSlim` throttle at `ProcessorCount / 2`. Scrolling a long list fast queues far
  more decode work than it consumes; bounding it keeps the machine responsive.

The cache deliberately does **not** pass the caller's cancellation token into the shared
task — one row scrolling out of view must not cancel the decode for every other row waiting
on the same image.

**`ModImageDecoder`.** Tries the Windows WIC codecs first, which handle the legacy FourCC DDS
formats (every mod icon observed so far is DXT1). WIC refuses BC7, which most *store* images
use, so those fall through to a managed block decoder. Anything that fails entirely yields
the placeholder initials instead.

## Page inventory

Status: **Working** · **Partial** — usable but incomplete · **Stub** — placeholder content.

| Page | Status | What it does |
| --- | --- | --- |
| `LoginPage` | Working | Shown while not authenticated |
| `MainPage` | Working | Shell: Home, Create repo, and the repo list |
| `CreateRepoPage` | Working | Name + adapter picker + base settings dynamic form |
| `RepoPage` | Working | Repo shell. Auto-selects "Connect game" when the repo has no instances |
| `RepoAdminPage` | Working | Rename repo, edit base settings, delete repo |
| `CreateLocalInstancePage` | Working | Name + instance settings form. Defaults the name to "Game" for the first instance and blocks duplicates |
| `EditLocalInstancePage` | Working | Edit or delete an instance. Planned to grow into the instance's real page — active profile, drift status, Re-apply — see [07](07-mod-sync-design.md#which-instances-does-apply-target) |
| `CreateProfilePage` | Working | |
| `EditProfilePage` | Working | Rename or delete a profile |
| `RepoModsPage` | Partial | Shell over Import and Manage — the two are planned to merge into one list, see [09](09-mod-catalog.md#manage) |
| `RepoModsImportPage` | **Partial** | Scans, dedupes, searches, selects — but `ImportAsync` is an empty `TODO`. Nothing uploads |
| `ProfilePage` | Partial | Shell over Overview, Mods, Manage |
| `ProfileModsEditorPage` | **Stub** | Hardcoded `["test 1", ...]`, empty `SaveChanges` |
| Repo → Members | **Stub** | `ExamplePageViewModel` |
| All Overview pages | **Stub** | `ExamplePageViewModel` |
| Mods → Manage | **Stub** | `ExamplePageViewModel` |
| `ExamplePage` | — | The placeholder itself |

Dialogs and shared views:

| Component | Notes |
| --- | --- |
| `Dialog` / `ModalViewModel` | Modals are awaited: `IModalService.Show` returns a `Task` completed by the modal's `Done` flag |
| `ConfirmationDialogViewModel` | Factory methods for `ConfirmDelete`, `ValidationErrors` (truncated past five), and `Error` |
| `ModDetailsDialog` | Full mod description plus the lazily-decoded store-image strip |
| `DynamicFormEditor` | Renders any `DynamicForm` from its attributes; raises `Modified`, which pages use to take the navigation lock |
| `ModListItem.xaml` | The standard mod row, applied as an implicit template — any items control fed `ModListItemViewModel` renders identically |
| `SidebarHeader` | |

`ModListItemViewModel` is worth a look as the model for list rows: it precomputes a short
description (first line of the mod's own description that is not just the mod's name again)
and initials for the icon placeholder, exposes a `Matches(searchTerm)` used for filtering, and
carries a `ModStatus` (`New` / `UpdateAvailable` / `AlreadyInRepo`) that nothing sets yet — it
is there for the import flow to fill in.
