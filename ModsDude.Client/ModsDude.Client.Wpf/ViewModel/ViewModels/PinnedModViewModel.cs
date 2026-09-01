using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One row of a profile's mod list as a reader sees it: the name, the pinned version, and whether
/// that pin is held in place.
/// </summary>
/// <remarks>
/// The editor's row carries a version picker, an update affordance and a lock toggle, all of which
/// are decisions. This one carries none - the lock is reported because it explains why the version
/// is what it is, not because anybody here can change it.
/// </remarks>
public class PinnedModViewModel
{
    public PinnedModViewModel(PinnedMod mod)
    {
        Name = mod.DisplayName;
        Version = mod.VersionId.Value;
        IsMissing = mod.IsRegistered is false;

        LockNote = mod.Lock.Source switch
        {
            ProfileModLockSource.Both => "Locked - this mod is version-sensitive, and the profile pins it too",
            ProfileModLockSource.Adapter => "Locked - this mod is version-sensitive",
            ProfileModLockSource.Profile => "Locked by this profile",
            _ => null
        };

        // A pin whose version the repo no longer registers cannot be synced, and saying so here is
        // the only chance a guest gets - they cannot open the editor that would show it in red.
        Note = IsMissing
            ? "This version is no longer in the repo. Ask a member to repin it."
            : null;
    }


    public string Name { get; }
    public string Version { get; }
    public bool IsMissing { get; }

    public string? LockNote { get; }
    public bool IsLocked => LockNote is not null;

    public string? Note { get; }
    public bool HasNote => Note is not null;
}
