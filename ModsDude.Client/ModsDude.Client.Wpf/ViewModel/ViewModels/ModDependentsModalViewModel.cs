using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Why a mod could not be deleted, in terms of the profiles and revisions that are holding it - and
/// with a way to go and look at each one.
/// </summary>
/// <remarks>
/// <para>
/// <b>The refusal used to be the end of the road.</b> "A profile depends on it" is true and
/// unactionable: a repo has several profiles, each with hundreds of revisions, and a version pinned
/// once long ago is as undeletable as one pinned now. Naming them turns a wall into a list, and
/// linking them turns the list into a next step.
/// </para>
/// <para>
/// <b>The head revision is called out separately.</b> Every other revision can be pruned; the head
/// cannot, because it is what the profile currently pins. Somebody has to take the mod out of the
/// profile and save before pruning is even the right tool, and that is a different job from tidying
/// up history.
/// </para>
/// </remarks>
public partial class ModDependentsModalViewModel : ModalViewModel
{
    /// <param name="what">
    /// What was refused, named the way the user named it - "version 1.2.0 of Big Bud" rather than a
    /// pair of ids. The dialog is about their action, not about the row it came from.
    /// </param>
    /// <param name="goToRevision">
    /// Opens a profile's history at one revision. Returns false where the shell refused - a page
    /// holding unsaved changes is entitled to say no - which is why the dialog stays open until it
    /// knows the navigation happened.
    /// </param>
    public ModDependentsModalViewModel(
        string what,
        ModDependentsDto dependents,
        Func<Guid, int, Task<bool>> goToRevision)
    {
        What = what;
        Truncated = dependents.Truncated;

        Profiles = [.. dependents.Profiles.Select(x => new ProfileDependentViewModel(x, OnGoTo))];

        _goToRevision = goToRevision;
    }


    private readonly Func<Guid, int, Task<bool>> _goToRevision;


    public string What { get; }

    public ObservableCollection<ProfileDependentViewModel> Profiles { get; }

    public string Title => "Still in use";

    public string Message => Profiles.Count == 1
        ? $"{What} is pinned by one profile's history, so the repo cannot let go of it yet."
        : $"{What} is pinned by {Profiles.Count} profiles' histories, so the repo cannot let go of it yet.";

    /// <summary>
    /// What to actually do, said once rather than repeated per row. Two different jobs, and which
    /// one applies is decided by whether a head revision is in the list.
    /// </summary>
    public string Advice => Profiles.Any(x => x.IncludesHead)
        ? "A profile still pins it in its current revision: take the mod out there and save first. "
            + "After that, an admin can prune the older revisions that hold it."
        : "An admin can prune these revisions from each profile's history, and the mod becomes deletable.";

    public bool Truncated { get; }

    public string TruncationNote =>
        "There are more than this list shows. Pruning what is here will reveal the rest.";


    [RelayCommand]
    private void Close() => Done = true;


    private async void OnGoTo(Guid profileId, int revision)
    {
        // The dialog has to be out of the way before the page it navigates to is on screen, and it
        // is closed regardless: a refused navigation leaves the user where they were, which is this
        // page, and reopening the dialog over it would be the app arguing with itself.
        Done = true;

        await _goToRevision(profileId, revision);
    }
}


/// <summary>One profile's hold on the mod, and every revision of it that pins one.</summary>
public partial class ProfileDependentViewModel
{
    public ProfileDependentViewModel(ProfileDependentDto dto, Action<Guid, int> goTo)
    {
        ProfileId = dto.ProfileId;
        Name = dto.ProfileName;
        IncludesHead = dto.IncludesHead;

        Revisions = [.. dto.Revisions.Select(x => new RevisionLinkViewModel(dto.ProfileId, x, goTo))];
    }


    public Guid ProfileId { get; }
    public string Name { get; }

    /// <summary>
    /// Whether the profile's <em>current</em> revision is one of them. The one case pruning cannot
    /// solve, so the row says so rather than leaving somebody to compare numbers.
    /// </summary>
    public bool IncludesHead { get; }

    public ObservableCollection<RevisionLinkViewModel> Revisions { get; }

    public string Summary => Revisions.Count == 1
        ? "1 revision pins it"
        : $"{Revisions.Count} revisions pin it";
}


/// <summary>One revision, as a link into the history page with it already selected.</summary>
public partial class RevisionLinkViewModel
{
    private readonly Guid _profileId;
    private readonly Action<Guid, int> _goTo;


    public RevisionLinkViewModel(Guid profileId, int revision, Action<Guid, int> goTo)
    {
        _profileId = profileId;
        _goTo = goTo;

        Revision = revision;
        Label = $"#{revision}";
    }


    public int Revision { get; }
    public string Label { get; }


    [RelayCommand]
    private void Open() => _goTo(_profileId, Revision);
}
