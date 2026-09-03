using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One mod as a comparison of two revisions shows it: the shared list row, and what happened to it.
/// </summary>
/// <remarks>
/// The same row as the profile's mod list and the repo's - so the icon loads the same way and the
/// name still opens the details dialog - with the transition on the end where the read-only list
/// puts its lock icon. A comparison is a reading surface, so nothing here is selectable.
/// </remarks>
public class ProfileModChangeViewModel
{
    public ProfileModChangeViewModel(ProfileModChange change, Guid repoId, ModListItemViewModel.Factory itemFactory)
    {
        Item = itemFactory.Create(repoId, change.Version);
        Item.IsSelectable = false;

        Kind = change.Kind;

        KindText = change.Kind switch
        {
            ProfileModChangeKind.Added => "Added",
            ProfileModChangeKind.Removed => "Removed",
            _ => "Changed"
        };

        Transition = Describe(change);
    }


    public ModListItemViewModel Item { get; }

    public ProfileModChangeKind Kind { get; }

    public string KindText { get; }

    /// <summary>What moved, in the form somebody reads a diff in. Empty where only the lock did.</summary>
    public string Transition { get; }

    public bool HasTransition => Transition.Length > 0;

    public bool IsAdded => Kind is ProfileModChangeKind.Added;
    public bool IsRemoved => Kind is ProfileModChangeKind.Removed;


    private static string Describe(ProfileModChange change)
    {
        var version = change.Kind switch
        {
            ProfileModChangeKind.Added => change.ToVersionId?.Value ?? "",
            ProfileModChangeKind.Removed => change.FromVersionId?.Value ?? "",
            _ when change.VersionMoved => $"{change.FromVersionId?.Value} → {change.ToVersionId?.Value}",
            _ => ""
        };

        // A save whose whole point was holding a mod where it is has nothing else to show, so the
        // lock is said in words rather than left as a row that appears to report nothing.
        var locks = change.LockChanged
            ? change.ToLocked ? "locked" : "unlocked"
            : "";

        return string.Join(" · ", new[] { version, locks }.Where(x => x.Length > 0));
    }
}
