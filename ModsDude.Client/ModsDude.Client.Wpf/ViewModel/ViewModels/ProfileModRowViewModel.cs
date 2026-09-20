using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Profiles;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One mod as the profile pins it: which version, whether the pin is held, and whether the file
/// still has to be imported before it can be saved.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by <see cref="ModId"/> and not by version, which is what makes the version selector part of
/// the row rather than a property of whatever was moved in - a profile depends on a mod at exactly
/// one version. The mod itself is rendered by the shared list row, so it looks the same here as it
/// does everywhere else and its icon loads the same way.
/// </para>
/// <para>
/// <b>Also the left list's row, unpinned.</b> The left side needs exactly the same shape - a
/// selector over one shared mod row that swaps itself when the selection moves - so it is built from
/// this type too, with <c>lockedByProfile</c> fixed at <see langword="false"/> and a confirm callback
/// that never asks: nothing is committed by choosing what the row's own + would add, only by
/// pressing it. The page tells the two apart by which list it reads a row out of, not by anything on
/// the row itself.
/// </para>
/// </remarks>
public partial class ProfileModRowViewModel : ObservableObject, ISelectableRow
{
    private readonly Guid _repoId;
    private readonly ModListItemViewModel.Factory _itemFactory;
    private readonly Func<ProfileModRowViewModel, ProfileModVersionOption, Task<bool>> _confirmLockedChange;

    /// <summary>
    /// Whether this is a row of the profile's own list. Only what its shared row draws depends on it: the
    /// pinned list has a lock toggle that says what holds the pin, so it leaves the adapter's padlock to
    /// the available list.
    /// </summary>
    private readonly bool _isPinned;

    private ProfileModVersionOption _selectedVersion;
    private IReadOnlyList<ProfileModVersionOption> _versions;

    /// <summary>
    /// The mod's ordering, kept only to answer "order not settled" for whichever versions the
    /// selector is currently offering. Null is silently "nothing is unsettled" rather than an error -
    /// a row built before the set is known yet (the very first frame) should not throw over a label.
    /// </summary>
    private ModVersionSet? _set;


    public ProfileModRowViewModel(
        Guid repoId,
        IReadOnlyList<CatalogModVersion> versions,
        CatalogModVersion selected,
        bool lockedByProfile,
        ModListItemViewModel.Factory itemFactory,
        Func<ProfileModRowViewModel, ProfileModVersionOption, Task<bool>> confirmLockedChange,
        ModVersionSet? set = null,
        bool isPinned = false)
    {
        _repoId = repoId;
        _itemFactory = itemFactory;
        _confirmLockedChange = confirmLockedChange;
        _set = set;
        _isPinned = isPinned;

        ModId = selected.ModId;
        Name = selected.Name;

        _versions = BuildOptions(versions);

        _selectedVersion = _versions.FirstOrDefault(x => x.Version.VersionId == selected.VersionId)
            ?? new ProfileModVersionOption(selected);
        _lockedByProfile = lockedByProfile;
        _item = CreateItem(_selectedVersion, selected: false);
    }


    public ModKey ModId { get; }
    public string Name { get; }

    /// <summary>
    /// What the selector offers, newest first. Replaced rather than fixed at construction, because a
    /// source chip changes what is known about this mod and the draft it belongs to outlives that.
    /// </summary>
    public IReadOnlyList<ProfileModVersionOption> Versions
    {
        get => _versions;
        private set
        {
            _versions = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSeveralVersions));
        }
    }


    /// <summary>
    /// Whether the search box is showing this row. Delegated to the shared list row rather than
    /// reimplemented, so both sides of the editor answer the same question the same way - and so
    /// that the answer follows the version selector, since <see cref="Item"/> is replaced when the
    /// selection changes.
    /// </summary>
    public bool Matches(string? searchTerm) => Item.Matches(searchTerm);

    /// <summary>
    /// Whether the editor has this row picked. Kept on <see cref="Item"/> rather than here, because
    /// the checkbox the user clicks belongs to the shared list row - and because that is what makes
    /// a mod picked on one side still picked when it is moved to the other. Changing the version
    /// replaces the item, so <see cref="CreateItem"/> carries the flag over.
    /// </summary>
    public bool IsSelected
    {
        get => Item.IsSelected;
        set => Item.IsSelected = value;
    }

    /// <summary>
    /// The shared list row for whatever version is selected. Replaced rather than mutated when the
    /// version changes, because a row wraps exactly one version - and a new one is what makes the
    /// icon and the details dialog follow the selection.
    /// </summary>
    [ObservableProperty]
    private ModListItemViewModel _item;

    /// <summary>
    /// The user's decision about this profile. Never the adapter's - that one is a property of the
    /// mod version and is not editable anywhere.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lock))]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    [NotifyPropertyChangedFor(nameof(LockTooltip))]
    [NotifyPropertyChangedFor(nameof(UpdateTooltip))]
    private bool _lockedByProfile;

    /// <summary>
    /// Where the newest version of this mod would take the pin, or null when it is already there.
    /// Set by the page, which is the thing that holds the ordering.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateTooltip))]
    private ModVersionKey? _updateTo;

    /// <summary>
    /// Whether taking that update also means uploading a file. Set alongside
    /// <see cref="UpdateTo"/>, because the tooltip is the only place a row says what the move costs.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateTooltip))]
    private bool _updateImportsOnSave;

    /// <summary>
    /// Why the left list would leave this row out by default, if it would. Set by the page beside the
    /// status chip, because it is the draft that decides it - a pin, or a lock on one, is what turns a
    /// row from ordinary into noise.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIgnored))]
    [NotifyPropertyChangedFor(nameof(CanToggleIgnore))]
    [NotifyPropertyChangedFor(nameof(IgnoreTooltip))]
    private IgnoreState _ignoreState;

    /// <summary>Set apart from the rest, by somebody's decision or by a lock.</summary>
    public bool IsIgnored => IgnoreState is IgnoreState.Ignored or IgnoreState.OtherVersionOfLocked;

    /// <summary>
    /// Whether the row's eye does anything. Only somebody's own decision can be reversed from here: a
    /// pin cannot be ignored, and a version of a locked one is ignored by the lock rather than by
    /// anybody.
    /// </summary>
    public bool CanToggleIgnore => IgnoreState is IgnoreState.Ignored or IgnoreState.None;

    public string IgnoreTooltip => IgnoreState switch
    {
        IgnoreState.Ignored => "Ignored. Click to stop ignoring this mod in this profile.",
        IgnoreState.OtherVersionOfLocked =>
            "Ignored because this profile holds its pin of this mod in place at another version. "
            + "Unlock the pin and this is an update again.",
        IgnoreState.Pinned => "Pinned in this profile, so it cannot be ignored.",
        _ => "Ignore this mod in this profile. It is hidden from this list until you show ignored mods."
    };


    /// <remarks>
    /// Written by hand rather than generated, because a locked pin has to be asked about
    /// <em>before</em> it moves - and a generated setter has already moved it by the time it can say
    /// anything.
    /// </remarks>
    public ProfileModVersionOption SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedVersion))
            {
                return;
            }

            if (Lock.IsLocked)
            {
                // The selector has already moved by the time this runs, so put it back and let the
                // answer do the moving. A confirmation that arrived after the change would be a
                // notification rather than a decision.
                RevertSelector();

                _ = ConfirmThenApplyAsync(value);

                return;
            }

            Apply(value);
        }
    }

    /// <summary>Version-sensitive as the adapter derived it, which is a fact about this version.</summary>
    public bool LockedByAdapter => SelectedVersion.Version.Locked;

    public ProfileModLock Lock => new(LockedByAdapter, LockedByProfile);

    public bool IsLocked => Lock.IsLocked;

    /// <summary>Whether the selector has anything to offer beyond what is already pinned.</summary>
    public bool HasSeveralVersions => Versions.Count > 1;

    /// <summary>
    /// Pinned at a version the repo does not hold. Nothing is uploaded until Save, so this row is a
    /// draft that discarding simply throws away.
    /// </summary>
    public bool IsPending => SelectedVersion.Version.IsOnServer is false;

    public bool HasUpdate => UpdateTo is not null;

    /// <summary>
    /// What the ⬆ on this row would do, in the mod's terms.
    /// </summary>
    /// <remarks>
    /// <b>A profile is not the thing that moves; a mod's version is.</b> This used to say "move this
    /// profile to version X", which names the wrong subject and reads as a change to the whole list.
    /// The second sentence is the cost, and it is only there when there is one: an update to a
    /// version only on disk is an upload as well as a pin.
    /// </remarks>
    public string UpdateTooltip
    {
        get
        {
            if (UpdateTo is not ModVersionKey version)
            {
                return string.Empty;
            }

            var move = $"Update to {version}.";

            if (UpdateImportsOnSave)
            {
                move += " Saving imports it.";
            }

            return IsLocked
                ? $"{move} This mod is locked, so batch updates leave it alone - moving it is a decision on this row."
                : move;
        }
    }

    /// <summary>
    /// Says which level the lock came from, because the two have different fixes - and never implies
    /// a scope this toggle does not have. It is the user's own, about this profile: there is no
    /// repo-wide user override, so someone who disagrees with the adapter unlocks here.
    /// </summary>
    public string LockTooltip
    {
        get
        {
            if (Lock.IsLocked is false)
            {
                return "Hold this mod at this version in this profile. Batch updates will skip it.";
            }

            var source = Lock.Source is ProfileModLockSource.Profile
                ? "Locked in this profile, by you. Other profiles are unaffected."
                : "Version-sensitive, as the game adapter read it from the mod file.";

            return Lock.CanBeUnlockedByProfile
                ? $"{source} Batch updates leave it alone; unticking this releases it."
                : $"{source} Batch updates leave it alone, and unticking this does not release it - the "
                    + "adapter re-derives its answer from every version, and there is no repo-wide override.";
        }
    }

    public ProfileModPin Pin => new(ModId, SelectedVersion.Version.VersionId, Lock);


    /// <summary>
    /// Moves the pin without asking, for callers that have already asked - the per-row update, and
    /// the modal that lists the locked mods with a checkbox each.
    /// </summary>
    public void SetVersion(ModVersionKey version)
    {
        if (Versions.FirstOrDefault(x => x.Version.VersionId == version) is ProfileModVersionOption option)
        {
            Apply(option);
        }
    }

    /// <summary>
    /// Re-offers this row's versions from a freshly composed catalog, keeping the pin where it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What a source toggle is allowed to do to a draft.</b> A chip changes what is <em>known</em>
    /// about a mod, never what this profile has decided about it - so the selector is rebuilt and the
    /// pin, the lock and the selection are not. <paramref name="versions"/> comes from the session's
    /// ever-growing accumulation rather than the current snapshot alone, so it is guaranteed to
    /// already contain this row's own pin - there is nothing left here to prepend.
    /// </para>
    /// <para>
    /// The inner list row is replaced only where the version record actually says something
    /// different, because replacing it drops a loaded thumbnail - and a chip being ticked is not a
    /// reason for two hundred icons to blink.
    /// </para>
    /// </remarks>
    public void Rebase(IReadOnlyList<CatalogModVersion> versions, ModVersionSet? set)
    {
        _set = set;
        Versions = BuildOptions(versions);

        var option = Versions.First(x => x.Version.VersionId == SelectedVersion.Version.VersionId);

        if (ModListItemViewModel.RendersTheSame(option.Version, SelectedVersion.Version))
        {
            // The selector is a new list of new options, so the one it is showing has to be the one
            // this row holds or the combo box reads as unset.
            _selectedVersion = option;

            OnPropertyChanged(nameof(SelectedVersion));

            return;
        }

        Apply(option);
    }

    /// <summary>
    /// The left list's counterpart of <see cref="Rebase"/>. Unlike a pin, nothing here is sacred -
    /// there is no draft decision to protect - so the page is free to hand back whichever version
    /// <paramref name="chosen"/> should be: the one this row already had selected where that survives
    /// the recompose, or its own default where the page has decided this row must snap to a
    /// particular version regardless, which is what a pending removal does.
    /// </summary>
    public void SetAvailableOptions(IReadOnlyList<CatalogModVersion> offered, CatalogModVersion chosen, ModVersionSet? set)
    {
        _set = set;
        Versions = BuildOptions(offered);

        var option = Versions.First(x => x.Version.VersionId == chosen.VersionId);

        if (ModListItemViewModel.RendersTheSame(option.Version, SelectedVersion.Version))
        {
            _selectedVersion = option;

            OnPropertyChanged(nameof(SelectedVersion));

            return;
        }

        Apply(option);
    }

    /// <summary>Newest first, since a version selector is opened to move forward far more often than back.</summary>
    private IReadOnlyList<ProfileModVersionOption> BuildOptions(IReadOnlyList<CatalogModVersion> versions)
        => [.. versions.Reverse().Select(x => new ProfileModVersionOption(x, _set?.CouldNotCompareToNewest(x.VersionId) ?? false))];


    private async Task ConfirmThenApplyAsync(ProfileModVersionOption value)
    {
        if (await _confirmLockedChange(this, value))
        {
            Apply(value);
        }
    }

    private void Apply(ProfileModVersionOption value)
    {
        _selectedVersion = value;

        var previous = Item;

        previous.PropertyChanged -= OnItemChanged;

        Item = CreateItem(value, previous.IsSelected);

        OnPropertyChanged(nameof(SelectedVersion));
        OnPropertyChanged(nameof(LockedByAdapter));
        OnPropertyChanged(nameof(Lock));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(LockTooltip));
    }

    private void RevertSelector()
    {
        // Raised after the binding has finished writing, or the selector keeps the value it is about
        // to be told it does not have.
        Application.Current?.Dispatcher.BeginInvoke(
            () => OnPropertyChanged(nameof(SelectedVersion)),
            DispatcherPriority.DataBind);
    }

    /// <param name="selected">
    /// Carried over from the item being replaced. A version change must not put a picked row down:
    /// the user picked the <em>mod</em>, and which version it sits at is a different decision.
    /// </param>
    private ModListItemViewModel CreateItem(ProfileModVersionOption option, bool selected)
    {
        var item = _itemFactory.Create(_repoId, option.Version);

        // The mod list editor is the only page that builds these rows, and it picks rows on both
        // sides.
        item.IsSelectable = true;
        item.IsSelected = selected;
        item.ShowAdapterLock = _isPinned is false;

        // The row's own selector already says which version this is, on both sides.
        item.ShowVersion = false;

        item.PropertyChanged += OnItemChanged;

        return item;
    }

    /// <summary>
    /// Re-raises the one thing about the item that is also a fact about this row. The page listens
    /// to rows, not to the items inside them, so a click on the checkbox has to arrive here.
    /// </summary>
    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModListItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsSelected));
        }
    }
}

/// <summary>One entry in a row's version selector.</summary>
/// <param name="CouldNotCompare">
/// Whether the ordering left this version genuinely uncompared against what the repo holds - see
/// <c>ModVersionSet.CouldNotCompareToNewest</c>. Only ever true for an unregistered version: two
/// registered versions are always settled by <c>SequenceNumber</c>, which is taken as fact and never
/// handed to the comparer.
/// </param>
public sealed record ProfileModVersionOption(CatalogModVersion Version, bool CouldNotCompare = false)
{
    /// <summary>
    /// The version, starred where saving has to import it first. The words live in <see cref="Note"/>
    /// rather than on the label: the selector is 150px wide, and a sentence in it was clipped.
    /// </summary>
    public string Label => Version.IsOnServer
        ? Version.VersionId.Value
        : $"{Version.VersionId.Value}*";

    /// <summary>What the star means, or null for a version that has nothing to say.</summary>
    public string? Note
    {
        get
        {
            if (Version.IsOnServer)
            {
                return null;
            }

            return CouldNotCompare
                ? "Imports on save. The order between this and what the repo holds is not settled, so importing will ask which comes first."
                : "Imports on save.";
        }
    }
}
