using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Wpf.Mods;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Profiles.Editor;

/// <summary>
/// One row of the profile mod editor. It shows computed state and reports what the user does to it;
/// it decides nothing.
/// </summary>
/// <remarks>
/// The shared list row wraps exactly one version, so it is replaced when the version is, carrying the
/// selection with it.
/// </remarks>
public abstract partial class EditorModRowViewModel : ObservableObject, ISelectableRow
{
    private readonly Guid _repoId;
    private readonly ModListItemViewModel.Factory _itemFactory;
    private readonly Action<EditorModRowViewModel, ProfileModVersionOption> _versionPicked;

    private IReadOnlyList<ProfileModVersionOption> _versions = [];
    private CatalogModVersion _version;


    protected EditorModRowViewModel(
        Guid repoId,
        ModKey modId,
        CatalogModVersion version,
        ModListItemViewModel.Factory itemFactory,
        Action<EditorModRowViewModel, ProfileModVersionOption> versionPicked)
    {
        _repoId = repoId;
        _itemFactory = itemFactory;
        _versionPicked = versionPicked;
        _version = version;
        _item = CreateItem(version, selected: false);

        ModId = modId;
    }


    public ModKey ModId { get; }

    public string Name => _version.Name;

    public CatalogModVersion Version => _version;

    [ObservableProperty]
    private ModListItemViewModel _item;

    public bool IsSelected
    {
        get => Item.IsSelected;
        set => Item.IsSelected = value;
    }

    public IReadOnlyList<ProfileModVersionOption> Versions => _versions;

    public bool HasSeveralVersions => _versions.Count > 1;

    /// <summary>
    /// What the version picker's box says. Read one way only: the picker reports a choice as a command
    /// and never holds a selection of its own, so what it shows cannot drift from what the row holds.
    /// </summary>
    public ProfileModVersionOption CurrentVersion
        => _versions.FirstOrDefault(x => x.Version.VersionId == _version.VersionId) ?? new ProfileModVersionOption(_version);

    [ObservableProperty]
    private bool _isVersionMenuOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSortCaption))]
    private string? _sortCaption;

    [ObservableProperty]
    private string? _sortTooltip;

    public bool HasSortCaption => SortCaption is not null;


    [RelayCommand]
    private void PickVersion(ProfileModVersionOption? option)
    {
        IsVersionMenuOpen = false;

        if (option is not null && option.Version.VersionId != _version.VersionId)
        {
            _versionPicked(this, option);
        }
    }


    protected void Show(
        CatalogModVersion version,
        IReadOnlyList<ProfileModVersionOption> versions,
        RemoteUpdateViewModel? remoteUpdate,
        string? sortCaption,
        string? sortTooltip)
    {
        if (ModListItemViewModel.RendersTheSame(version, _version) is false)
        {
            var previous = Item;

            previous.PropertyChanged -= OnItemPropertyChanged;

            Item = CreateItem(version, previous.IsSelected);
        }

        _version = version;

        if (_versions.SequenceEqual(versions) is false)
        {
            _versions = versions;

            OnPropertyChanged(nameof(Versions));
            OnPropertyChanged(nameof(HasSeveralVersions));
        }

        OnPropertyChanged(nameof(CurrentVersion));

        if (Item.RemoteUpdate?.Version != remoteUpdate?.Version || Item.RemoteUpdate?.Text != remoteUpdate?.Text)
        {
            Item.RemoteUpdate = remoteUpdate;
        }

        SortCaption = sortCaption;
        SortTooltip = sortTooltip;
    }

    protected abstract void Configure(ModListItemViewModel item);


    private ModListItemViewModel CreateItem(CatalogModVersion version, bool selected)
    {
        var item = _itemFactory.Create(_repoId, version);

        item.IsSelectable = true;
        item.IsSelected = selected;
        item.ShowVersion = false;
        item.OutlineStatus = true;

        item.PropertyChanged += OnItemPropertyChanged;

        return item;
    }

    partial void OnItemChanged(ModListItemViewModel value) => Configure(value);

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModListItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsSelected));
        }
    }
}


/// <summary>A mod the profile does not hold: what the + would add, at the version its selector shows.</summary>
public sealed partial class AvailableModRowViewModel : EditorModRowViewModel
{
    public AvailableModRowViewModel(
        Guid repoId,
        AvailableModRow state,
        ModListItemViewModel.Factory itemFactory,
        Action<EditorModRowViewModel, ProfileModVersionOption> versionPicked)
        : base(repoId, state.ModId, state.Version, itemFactory, versionPicked)
    {
        State = state;
    }


    public AvailableModRow State { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IgnoreTooltip))]
    private bool _isIgnored;

    [ObservableProperty]
    private bool _isPickable = true;

    public string IgnoreTooltip => IsIgnored
        ? "Ignored. Click to stop ignoring this mod in this profile."
        : "Ignore this mod in this profile. It is hidden from this list until you show ignored mods.";


    public void Update(AvailableModRow state, RemoteUpdateViewModel? remoteUpdate, bool pickable)
    {
        State = state;
        IsPickable = pickable;
        IsIgnored = state.IsIgnored;

        Show(state.Version, state.Options, remoteUpdate, state.SortCaption, state.SortTooltip);
        Configure(Item);
    }

    protected override void Configure(ModListItemViewModel item)
    {
        if (State is null)
        {
            return;
        }

        item.IsPickable = IsPickable;
        item.Status = State.Status switch
        {
            AvailableModStatus.New => ModDisplayStatus.New,
            AvailableModStatus.NewVersion => ModDisplayStatus.NewVersion,
            _ => ModDisplayStatus.AlreadyInRepo
        };
        item.OrderNotSettled = State.OrderNotSettled;
        item.Sources = State.Sources;
    }
}


/// <summary>A mod in the draft, or taken out of it.</summary>
public sealed partial class PinnedModRowViewModel : EditorModRowViewModel
{
    private readonly Action<PinnedModRowViewModel, bool> _lockToggled;


    public PinnedModRowViewModel(
        Guid repoId,
        PinnedModRow state,
        ModListItemViewModel.Factory itemFactory,
        Action<EditorModRowViewModel, ProfileModVersionOption> versionPicked,
        Action<PinnedModRowViewModel, bool> lockToggled)
        : base(repoId, state.ModId, state.Version, itemFactory, versionPicked)
    {
        _lockToggled = lockToggled;
        State = state;
    }


    public PinnedModRow State { get; private set; }

    [ObservableProperty]
    private bool _isPickable = true;

    public bool IsTakenOut => State.IsTakenOut;
    public bool IsChanged => State.Touch is not ProfileModTouch.None;
    public bool HasUpdate => State.Update is not null;
    public bool IsLocked => State.Lock.IsLocked;
    public bool LockedByAdapter => State.Lock.ByAdapter;

    /// <summary>The profile's own lock. The toggle can never release the adapter's.</summary>
    public bool LockedByProfile
    {
        get => State.Lock.ByProfile;
        set
        {
            if (value != State.Lock.ByProfile)
            {
                _lockToggled(this, value);
            }
        }
    }

    public string UpdateTooltip
    {
        get
        {
            if (State.Update is not ProfileModUpdate update)
            {
                return string.Empty;
            }

            var move = update.ImportsOnSave ? $"Update to {update.To}. Saving imports it." : $"Update to {update.To}.";

            return IsLocked ? $"{move} Locked, so batch updates leave it alone." : move;
        }
    }

    public string LockTooltip
    {
        get
        {
            var lockState = State.Lock;

            if (lockState.IsLocked is false)
            {
                return "Hold this mod at this version in this profile. Batch updates will skip it.";
            }

            var source = lockState.Source is ProfileModLockSource.Profile
                ? "Locked in this profile, by you. Other profiles are unaffected."
                : "Version-sensitive, as the game adapter read it from the mod file.";

            return lockState.CanBeUnlockedByProfile
                ? $"{source} Batch updates leave it alone; unticking this releases it."
                : $"{source} Batch updates leave it alone, and unticking this does not release it.";
        }
    }


    public void Update(PinnedModRow state, RemoteUpdateViewModel? remoteUpdate, bool pickable)
    {
        State = state;
        IsPickable = pickable;

        Show(state.Version, state.Options, remoteUpdate, state.SortCaption, state.SortTooltip);
        Configure(Item);

        OnPropertyChanged(nameof(IsTakenOut));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(LockedByAdapter));
        OnPropertyChanged(nameof(LockedByProfile));
        OnPropertyChanged(nameof(UpdateTooltip));
        OnPropertyChanged(nameof(LockTooltip));
    }

    /// <summary>Puts the lock toggle back on what the draft holds, after a toggle that was turned down.</summary>
    public void RestoreLock()
    {
        Application.Current?.Dispatcher.BeginInvoke(
            () => OnPropertyChanged(nameof(LockedByProfile)),
            DispatcherPriority.DataBind);
    }

    protected override void Configure(ModListItemViewModel item)
    {
        if (State is null)
        {
            return;
        }

        item.IsPickable = IsPickable;
        item.ShowAdapterLock = false;
        item.Status = State.IsPending ? ModDisplayStatus.ImportsOnSave : ModDisplayStatus.None;
        item.Touch = State.Touch;
        item.TouchTooltip = State.TouchTooltip;
        item.IsStruckOut = State.IsTakenOut;
    }
}
