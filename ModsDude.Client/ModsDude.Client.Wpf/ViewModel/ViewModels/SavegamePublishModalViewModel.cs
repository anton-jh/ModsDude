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
/// The number this first snapshot records, from <see cref="SavegameService.DeclaredRevisionFor"/>.
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
/// follows, and optionally what this first snapshot was.
/// </summary>
/// <remarks>
/// <para>
/// <b>Publish is not check-in.</b> "Upload this new thing" and "upload a new snapshot of that thing"
/// have opposite failure modes, and one button doing both is how somebody's savegame ends up as a snapshot
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
/// <para>
/// <b>Whether you keep playing is the fourth, and it is not always a question.</b> Publishing to the
/// mod list this game is already on leaves an ordinary held save, so the choice is offered and ticked.
/// Publishing to any <em>other</em> profile cannot: the save would follow one mod list while sitting in
/// a folder on another, which is the state that damages saves, and no apply clears it because the apply
/// table refuses every profile the folder could move to. So that answer is taken away rather than
/// warned about, and the notice says where the copy goes.
/// </para>
/// </remarks>
public partial class SavegamePublishModalViewModel : ModalViewModel
{
    /// <param name="preselected">
    /// The profile to arrive on, or null to arrive on nothing. Null where the game follows no
    /// profile in this repo: the two defaults available there - the first profile in the list, and no
    /// mod list - are both permanent decisions made on the user's behalf, so the dialog asks instead.
    /// </param>
    /// <param name="activeProfileId">
    /// Which profile this game follows, or null where it follows none in this repo. Read only to decide
    /// whether keeping the save is on offer - <b>separately from <paramref name="preselected"/></b>,
    /// which is the same profile today and is a statement about where the dialog opens rather than
    /// about what the folder is on.
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
        Guid? activeProfileId,
        string? folderProfileName,
        int? slotNumber = null)
    {
        SlotLabel = slotLabel;
        SlotNumber = slotNumber;
        RepoName = repoName;
        ActiveProfileId = activeProfileId;
        FolderProfileName = folderProfileName;

        _name = suggestedName;
        _selectedProfile = preselected;

        Profiles = [.. profiles];
    }


    public string SlotLabel { get; }
    public int? SlotNumber { get; }
    public string RepoName { get; }

    /// <inheritdoc cref="SavegamePublishModalViewModel(string, string, string, IReadOnlyList{SavegamePublishOption}, SavegamePublishOption?, Guid?, string?, int?)"/>
    public Guid? ActiveProfileId { get; }

    /// <inheritdoc cref="SavegamePublishModalViewModel(string, string, string, IReadOnlyList{SavegamePublishOption}, SavegamePublishOption?, Guid?, string?, int?)"/>
    public string? FolderProfileName { get; }

    /// <summary>Every profile in the repo, plus <see cref="SavegamePublishOption.NoModList"/> last.</summary>
    public ObservableCollection<SavegamePublishOption> Profiles { get; }

    public string Title => "Publish this save";

    private static string Capitalised(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    public string Message =>
        $"{Capitalised(SavegameSlotWording.Named(SlotNumber, SlotLabel))} is uploaded to {RepoName} as a savegame of its own.";


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
    [NotifyPropertyChangedFor(nameof(CanKeepPlaying))]
    [NotifyPropertyChangedFor(nameof(KeepPlaying))]
    [NotifyPropertyChangedFor(nameof(HandOverNotice))]
    [NotifyPropertyChangedFor(nameof(HasHandOverNotice))]
    [NotifyPropertyChangedFor(nameof(Consequence))]
    [NotifyPropertyChangedFor(nameof(ConfirmLabel))]
    private SavegamePublishOption? _selectedProfile;

    /// <summary>
    /// What the user asked for, which is not always what happens - see <see cref="KeepPlaying"/>.
    /// </summary>
    /// <remarks>
    /// Kept separately from the answer so that picking another profile and picking this one back does
    /// not silently drop a tick the user had put there. Ticked by default: publishing a save you are
    /// in the middle of and being handed it back is the ordinary case, and it is what this dialog
    /// always did.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeepPlaying))]
    [NotifyPropertyChangedFor(nameof(Consequence))]
    [NotifyPropertyChangedFor(nameof(ConfirmLabel))]
    private bool _wantsToKeepPlaying = true;

    /// <summary>The name to publish under, or null where the dialog was dismissed.</summary>
    public string? Result { get; private set; }

    /// <summary>Blank means no description of the first snapshot, which is the ordinary answer.</summary>
    public string? TrimmedLabel => string.IsNullOrWhiteSpace(Label) ? null : Label.Trim();

    /// <summary>
    /// The revision this records, as a number.
    /// </summary>
    /// <remarks>
    /// <b>A declaration, which is why it is shown rather than implied.</b> The bytes predate ModsDude,
    /// so nothing knows which mods were in the folder while this savegame was played, and no arrangement of
    /// this dialog recovers it. Every snapshot after this one is observed.
    /// </remarks>
    public string? RevisionText => SelectedProfile switch
    {
        null => null,
        { DeclaredRevision: int revision } profile =>
            $"This first snapshot is recorded as played on {profile.Name} rev {revision}. Nothing checks that this savegame can actually run on it - nothing can.",
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
    /// Whether staying checked out is an answer this profile allows.
    /// </summary>
    /// <remarks>
    /// <b>The mod folder, not the savegame.</b> A save following no mod list constrains no folder and
    /// may always be kept; a save following the profile this game is already on sits in a folder that
    /// is on its list, which is the ordinary held state. Every other profile is the refused one -
    /// see the remarks on this class.
    /// </remarks>
    public bool CanKeepPlaying => SelectedProfile is not { ProfileId: Guid chosen } || chosen == ActiveProfileId;

    /// <summary>
    /// What actually happens to the local copy: what was asked for, where the profile allows it.
    /// </summary>
    /// <remarks>
    /// <b>The clamp is in the getter, so the box unticks itself when the answer stops being available
    /// and remembers the tick when it comes back.</b> The alternative - writing false into
    /// <see cref="WantsToKeepPlaying"/> on every profile change - would make picking the wrong profile
    /// and picking back silently lose a decision the user made.
    /// </remarks>
    public bool KeepPlaying
    {
        get => CanKeepPlaying && WantsToKeepPlaying;
        set => WantsToKeepPlaying = value;
    }

    /// <summary>
    /// That this publish hands the save straight back, and why there is no choice about it.
    /// </summary>
    /// <remarks>
    /// Named for the consequence rather than for the rule: "the apply table refuses every profile this
    /// folder could move to" is true and is not what somebody about to lose a folder needs to read.
    /// The Recycle Bin is said out loud because it is the whole of what makes this recoverable.
    /// </remarks>
    public string? HandOverNotice => CanKeepPlaying
        ? null
        : $"This game is not on {SelectedProfile?.Name}, so you cannot keep this save checked out: it would be " +
          "sitting in a mod folder running a different list, which is what damages a save. Once the upload is " +
          "verified the local copy goes to the Recycle Bin and the savegame is anybody's to take - check it out " +
          $"again after applying {SelectedProfile?.Name} to carry on playing it.";

    public bool HasHandOverNotice => HandOverNotice is not null;

    /// <summary>The verb carries what happens to the copy on this machine, the same way check-in's does.</summary>
    public string ConfirmLabel => KeepPlaying
        ? "Publish and keep playing"
        : "Publish - the local copy goes to the Recycle Bin";

    public string Consequence => KeepPlaying
        ? "The save stays exactly where it is and stays yours. Nobody else can take it until you check it in."
        : "The slot is freed once the upload is verified, and the save is anybody's to take.";

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


    public override bool TryCancel() => Press(CancelCommand);

    /// <summary>
    /// Enter publishes only where the button would - a blank name and an unanswered profile both
    /// refuse it - and it publishes whatever the tick currently says, which is what the button does
    /// too.
    /// </summary>
    public override bool TryAccept() => Press(ConfirmCommand);
}
