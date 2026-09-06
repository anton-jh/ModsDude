using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using System.Windows;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One mod as the profile pins it: which version, whether the pin is held, and whether the file
/// still has to be imported before it can be saved.
/// </summary>
/// <remarks>
/// Keyed by <see cref="ModId"/> and not by version, which is what makes the version selector part of
/// the row rather than a property of whatever was moved in - a profile depends on a mod at exactly
/// one version. The mod itself is rendered by the shared list row, so it looks the same here as it
/// does everywhere else and its icon loads the same way.
/// </remarks>
public partial class ProfileModRowViewModel : ObservableObject
{
    private readonly Guid _repoId;
    private readonly ModListItemViewModel.Factory _itemFactory;
    private readonly Func<ProfileModRowViewModel, ProfileModVersionOption, Task<bool>> _confirmLockedChange;

    private ProfileModVersionOption _selectedVersion;


    public ProfileModRowViewModel(
        Guid repoId,
        IReadOnlyList<CatalogModVersion> versions,
        CatalogModVersion selected,
        bool lockedByProfile,
        ModListItemViewModel.Factory itemFactory,
        Func<ProfileModRowViewModel, ProfileModVersionOption, Task<bool>> confirmLockedChange)
    {
        _repoId = repoId;
        _itemFactory = itemFactory;
        _confirmLockedChange = confirmLockedChange;

        ModId = selected.ModId;
        Name = selected.Name;

        // Newest first: a version selector is opened to move forward far more often than back.
        Versions = [.. versions.Reverse().Select(x => new ProfileModVersionOption(x))];

        _selectedVersion = Versions.FirstOrDefault(x => x.Version.VersionId == selected.VersionId)
            ?? new ProfileModVersionOption(selected);
        _lockedByProfile = lockedByProfile;
        _item = CreateItem(_selectedVersion);
    }


    public ModKey ModId { get; }
    public string Name { get; }

    public IReadOnlyList<ProfileModVersionOption> Versions { get; }


    /// <summary>
    /// Whether the search box is showing this row. Delegated to the shared list row rather than
    /// reimplemented, so both sides of the editor answer the same question the same way - and so
    /// that the answer follows the version selector, since <see cref="Item"/> is replaced when the
    /// selection changes.
    /// </summary>
    public bool Matches(string? searchTerm) => Item.Matches(searchTerm);

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
    /// Where the repo's newest version of this mod would take the pin, or null when it is already
    /// there. Set by the page, which is the thing that knows the repo's ordering.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateTooltip))]
    private ModVersionKey? _updateTo;


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

    public string UpdateTooltip => UpdateTo is ModVersionKey version
        ? IsLocked
            ? $"Version {version} is available. This mod is locked, so batch updates leave it alone - moving it is a decision on this row."
            : $"Move this profile to version {version}."
        : string.Empty;

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

        Item = CreateItem(value);

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

    private ModListItemViewModel CreateItem(ProfileModVersionOption option)
    {
        var item = _itemFactory.Create(_repoId, option.Version);

        item.IsSelectable = false;

        return item;
    }
}

/// <summary>One entry in a row's version selector.</summary>
public sealed record ProfileModVersionOption(CatalogModVersion Version)
{
    public string Label => Version.IsOnServer
        ? Version.VersionId.Value
        : $"{Version.VersionId.Value} — imports on save";
}
