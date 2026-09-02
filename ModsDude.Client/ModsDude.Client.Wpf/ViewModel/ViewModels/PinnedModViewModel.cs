using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One row of a profile's mod list as a reader sees it: the shared list row, plus the two things
/// only a profile can say about the mod on it.
/// </summary>
/// <remarks>
/// The mod itself is rendered by <see cref="ModListItemViewModel"/>, exactly as in the editor and on
/// the repo's mod list - same icon, same description, same name that opens the details dialog. What
/// hangs off the end of the row is what the editor puts there as a control and this page can only
/// report: whether the profile holds the pin where it is.
/// </remarks>
public class PinnedModViewModel
{
    public PinnedModViewModel(PinnedMod mod, Guid repoId, ModListItemViewModel.Factory itemFactory)
    {
        Item = itemFactory.Create(repoId, mod.Version);

        // Nothing on this page picks mods, and no action on the repo is offered to a guest.
        Item.IsSelectable = false;

        // Only the profile's own lock: the adapter's is a fact about the version, which the shared
        // row already carries its own icon for. Saying it twice on one row would read as two locks.
        IsLockedByProfile = mod.Lock.ByProfile;

        LockNote = mod.Lock.Source switch
        {
            ProfileModLockSource.Both => "Locked by this profile - and version-sensitive besides. "
                + "Only a member can release it.",
            ProfileModLockSource.Profile => "Locked by this profile. Only a member can release it.",
            _ => null
        };
    }


    /// <summary>The shared list row, which is the whole of the mod as it is rendered anywhere else.</summary>
    public ModListItemViewModel Item { get; }

    public bool IsLockedByProfile { get; }
    public string? LockNote { get; }
}
