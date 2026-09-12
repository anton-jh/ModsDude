using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Savegames;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One answer to "which mod list does this savegame follow": a profile in the repo, or none of them.
/// </summary>
/// <remarks>
/// <b>No mod list is a choice rather than a fallback.</b> Adapters with savegame support and no mod
/// support have no profile to offer, and a save in a mod-capable repo may equally be published without
/// one - it records no revision, takes no part in the apply table, and nothing ever reports it as
/// drifted. It is also permanent: nothing moves a savegame between profiles, or into one.
/// </remarks>
/// <param name="DeclaredRevision">
/// The number this first version records, from <see cref="SavegameService.DeclaredRevisionFor"/>.
/// Null only for <see cref="NoModList"/>, whose pair is null on both halves.
/// </param>
/// <param name="CurrentSavegameName">
/// The savegame this profile is following right now, which publishing displaces. Null where the profile
/// has none - the ordinary starting state, where there is nothing to supersede and nothing to say.
/// </param>
/// <param name="CurrentSavegameRevision">The revision that savegame stays on once it is past.</param>
/// <param name="FolderIsOnIt">
/// Whether the mod folder is actually on this profile. Where it is not, the revision below is a
/// declaration about a list this folder has never run - which the dialog says out loud.
/// </param>
public sealed record SavegamePublishOption(
    Guid? ProfileId,
    string Name,
    int? DeclaredRevision,
    string? CurrentSavegameName,
    int? CurrentSavegameRevision,
    bool FolderIsOnIt)
{
    public static SavegamePublishOption NoModList { get; } = new(null, "No mod list", null, null, null, false);


    /// <summary>What the publish sends, with the pair kept together or absent together.</summary>
    public SavegamePublishTarget? ToTarget()
        => ProfileId is Guid profileId && DeclaredRevision is int revision
            ? new SavegamePublishTarget(profileId, revision)
            : null;
}


/// <summary>
/// Publishing a save that is already on this machine: what the repo should call it, which mod list it
/// follows, and optionally what this first version was.
/// </summary>
/// <remarks>
/// <para>
/// <b>Publish is not check-in.</b> "Upload this new thing" and "upload a new version of that thing"
/// have opposite failure modes, and one button doing both is how somebody's savegame ends up as a version
/// of somebody else's. This one is only ever reached from a slot, and it names the thing being made.
/// </para>
/// <para>
/// <b>It asks about the profile, and every answer is legitimate.</b> The game's active one is only
/// the likeliest: a savegame can be published to a list it is not currently on, and to no list at all. The
/// answer cannot be revised afterwards - nothing moves a savegame between profiles - which is why it
/// is asked here rather than derived from whatever the folder happens to be on.
/// </para>
/// <para>
/// <b>Three consequences, stated inline rather than as a second dialog.</b> The revision this declares,
/// the savegame it supersedes, and the folder being on a different list are all things somebody would want
/// to have seen before pressing the button, and a confirmation that appears afterwards is one that
/// gets clicked through.
/// </para>
/// </remarks>
public partial class SavegamePublishModalViewModel : ModalViewModel
{
    /// <param name="preselected">
    /// The profile to arrive on, or null to arrive on nothing. Null where the game follows no
    /// profile in this repo: the two defaults available there - the first profile in the list, and no
    /// mod list - are both permanent decisions made on the user's behalf, so the dialog asks instead.
    /// </param>
    /// <param name="folderProfileName">
    /// Which mod list the folder is actually on, for the sentence that says so where the chosen
    /// profile is a different one. Null where it is on none.
    /// </param>
    public SavegamePublishModalViewModel(
        string slotLabel,
        string repoName,
        string suggestedName,
        IReadOnlyList<SavegamePublishOption> profiles,
        SavegamePublishOption? preselected,
        string? folderProfileName)
    {
        SlotLabel = slotLabel;
        RepoName = repoName;
        FolderProfileName = folderProfileName;

        _name = suggestedName;
        _selectedProfile = preselected;

        Profiles = [.. profiles];
    }


    public string SlotLabel { get; }
    public string RepoName { get; }

    /// <inheritdoc cref="SavegamePublishModalViewModel(string, string, string, IReadOnlyList{SavegamePublishOption}, SavegamePublishOption?, string?)"/>
    public string? FolderProfileName { get; }

    /// <summary>Every profile in the repo, plus <see cref="SavegamePublishOption.NoModList"/> last.</summary>
    public ObservableCollection<SavegamePublishOption> Profiles { get; }

    public string Title => "Publish this save";

    public string Message =>
        $"'{SlotLabel}' is uploaded to {RepoName} as a savegame of its own. The save stays exactly where " +
        "it is - it is yours, checked out, until you check it in.";


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name;

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyPropertyChangedFor(nameof(RevisionText))]
    [NotifyPropertyChangedFor(nameof(HasRevisionText))]
    [NotifyPropertyChangedFor(nameof(SupersedeNotice))]
    [NotifyPropertyChangedFor(nameof(HasSupersedeNotice))]
    [NotifyPropertyChangedFor(nameof(MismatchNotice))]
    [NotifyPropertyChangedFor(nameof(HasMismatchNotice))]
    private SavegamePublishOption? _selectedProfile;

    /// <summary>The name to publish under, or null where the dialog was dismissed.</summary>
    public string? Result { get; private set; }

    /// <summary>Blank means no description of the first version, which is the ordinary answer.</summary>
    public string? TrimmedLabel => string.IsNullOrWhiteSpace(Label) ? null : Label.Trim();

    /// <summary>
    /// The revision this records, as a number.
    /// </summary>
    /// <remarks>
    /// <b>A declaration, which is why it is shown rather than implied.</b> The bytes predate ModsDude,
    /// so nothing knows which mods were in the folder while this savegame was played, and no arrangement of
    /// this dialog recovers it. Every version after this one is observed.
    /// </remarks>
    public string? RevisionText => SelectedProfile switch
    {
        null => null,
        { DeclaredRevision: int revision } profile =>
            $"This first version is recorded as played on {profile.Name} rev {revision}. Nothing checks that this savegame can actually run on it - nothing can.",
        _ => "This save follows no mod list. It records no revision, no profile is ever applied on its behalf, and nothing reports it as drifted. It cannot be given one later."
    };

    public bool HasRevisionText => RevisionText is not null;

    /// <summary>
    /// That this publish displaces the savegame the profile is following now.
    /// </summary>
    /// <remarks>
    /// Inline rather than a second dialog, and worded for what actually happens to the other savegame:
    /// past is not archived and not read-only. It stays playable, it stays checkable-out, and the one
    /// thing that changes is that its revision stops moving.
    /// </remarks>
    public string? SupersedeNotice => SelectedProfile is { CurrentSavegameName: { Length: > 0 } current } profile
        ? profile.CurrentSavegameRevision is int revision
            ? $"'{current}' is {profile.Name}'s current savegame. Publishing this makes it past - it stays playable and stays on rev {revision}."
            : $"'{current}' is {profile.Name}'s current savegame. Publishing this makes it past - it stays playable, and its mod list stops moving."
        : null;

    public bool HasSupersedeNotice => SupersedeNotice is not null;

    /// <summary>The folder is on one list and this is being recorded against another.</summary>
    public string? MismatchNotice => SelectedProfile is { ProfileId: not null, FolderIsOnIt: false } profile
        ? FolderProfileName is { Length: > 0 } folder
            ? $"This folder is on '{folder}'. Nothing checks that this savegame can run on {profile.Name}."
            : $"This folder has never been synced to a mod list, so {profile.Name}'s latest revision is what gets recorded."
        : null;

    public bool HasMismatchNotice => MismatchNotice is not null;

    /// <summary>
    /// The two things that have to be answered: the name everybody else will see, and which mod list
    /// this savegame follows for the rest of its life.
    /// </summary>
    public bool IsValid => string.IsNullOrWhiteSpace(Name) is false && SelectedProfile is not null;


    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Confirm()
    {
        Result = Name.Trim();
        Done = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        Done = true;
    }
}
