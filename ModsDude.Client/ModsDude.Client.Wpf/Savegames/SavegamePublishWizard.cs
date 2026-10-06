using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Shell.Modals;
using System.Runtime.CompilerServices;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>What a publish was answered with: everything the work needs, nothing it has done.</summary>
/// <param name="CheckInFirst">The savegame holding the mod folder, checked in so the new one can be kept. Null where none is.</param>
/// <param name="ActivatesFirst">Whether the profile the new savegame follows is activated before it is published.</param>
public sealed record SavegamePublishPlan(
    SavegameSlotOptionViewModel Slot,
    string Name,
    string? Label,
    SavegamePublishOption Profile,
    bool KeepPlaying,
    SavegamePublishCheckIn? CheckInFirst,
    bool ActivatesFirst);


/// <param name="Name">What the step called it, which may be a placeholder.</param>
/// <param name="RenameTo">Its real name, to write into the slot, or null where it could not be read.</param>
/// <param name="Label">What the check-in step was answered with as the snapshot's description.</param>
public sealed record SavegamePublishCheckIn(Guid SavegameId, string Name, string? RenameTo, string? Label);


/// <summary>
/// The questions a publish asks, one at a time: which slot, the publish itself, and whatever keeping
/// the save needs first - activating its profile, and checking in the savegame holding the mod folder.
/// </summary>
/// <remarks>
/// <para>
/// <b>The steps are worked out from the answers each time, never stored as a list.</b> The path is
/// walked again from the current answers and the state of the game - see <see cref="WizardPath"/> - so
/// going back and unticking "keep playing" drops the two steps that only followed from it.
/// </para>
/// <para>
/// <b>Each step is made once and reused</b> while what it is about stays the same, so going back
/// shows it as it was answered and the wizard can find it on the path walked again.
/// </para>
/// </remarks>
public sealed class SavegamePublishWizard
{
    private readonly Repo _repo;
    private readonly Game _game;
    private readonly Guid? _preselectProfileId;
    private readonly ISavegameSlots _savegameSlots;
    private readonly IHeldSavegames _heldSavegames;
    private readonly IProfileService _profileService;
    private readonly ISyncManifestStore _manifestStore;
    private readonly ISavegameCheckInFlow _checkInFlow;
    private readonly ISavegamesClient _savegamesClient;

    private readonly SavegameSlotPickerStepViewModel _slotStep;

    private (SavegameSlotRef Slot, SavegamePublishStepViewModel Step)? _publishStep;
    private (Guid ProfileId, ActiveProfile? Active, QuestionStepViewModel Step)? _activateStep;
    private (Guid SavegameId, bool Activates, SavegameCheckInStepViewModel Step)? _checkInStep;
    private ReviewStepViewModel? _reviewStep;
    private string? _reviewKey;

    private readonly Dictionary<Guid, string?> _heldNames = [];

    /// <summary>The plan a path enumerated to its end leaves behind.</summary>
    private SavegamePublishPlan? _reachedEnd;


    public SavegamePublishWizard(
        Repo repo,
        Game game,
        Guid? preselectProfileId,
        IReadOnlyList<SavegameSlotOptionViewModel> slots,
        ISavegameSlots savegameSlots,
        IHeldSavegames heldSavegames,
        IProfileService profileService,
        ISyncManifestStore manifestStore,
        ISavegameCheckInFlow checkInFlow,
        ISavegamesClient savegamesClient)
    {
        _repo = repo;
        _game = game;
        _preselectProfileId = preselectProfileId;
        _savegameSlots = savegameSlots;
        _heldSavegames = heldSavegames;
        _profileService = profileService;
        _manifestStore = manifestStore;
        _checkInFlow = checkInFlow;
        _savegamesClient = savegamesClient;

        _slotStep = new SavegameSlotPickerStepViewModel(repo.Name, slots);
    }


    public WizardStepViewModel First => _slotStep;

    /// <summary>What the wizard was answered with, once its last step has been.</summary>
    public SavegamePublishPlan? Plan { get; private set; }


    public async Task<WizardStepViewModel?> NextAsync(WizardStepViewModel answered, CancellationToken cancellationToken)
    {
        _reachedEnd = null;

        var next = await WizardPath.StepAfterAsync(PathAsync(cancellationToken), answered);

        Plan = next is null ? _reachedEnd : null;

        return next;
    }


    /// <summary>
    /// Every step the answers so far lead to, in order: slot, publish, then - only where the save is
    /// kept - activating its profile and checking in the savegame holding the mod folder, and a review
    /// where that adds up to more than one action.
    /// </summary>
    private async IAsyncEnumerable<WizardStepViewModel> PathAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return _slotStep;

        var slot = _slotStep.SelectedSlot
            ?? throw new InvalidOperationException("The slot step was answered without a slot.");

        var publish = await PublishStepAsync(slot, cancellationToken);

        yield return publish;

        var profile = publish.SelectedProfile
            ?? throw new InvalidOperationException("The publish step was answered without a profile.");

        var keep = publish.KeepPlaying
            ? _heldSavegames.DecideKeepPublished(_game, _repo.Id, profile.ProfileId)
            : SavegameKeepPlan.Ready;

        var actions = new List<(string Verb, string Line)>();
        SavegamePublishCheckIn? checkIn = null;

        if (keep.ActivatesFirst && profile.ProfileId is Guid profileId)
        {
            yield return ActivateStep(profileId, profile.Name, publish.TrimmedName);
        }

        if (keep.ChecksInFirst is Guid heldId)
        {
            var heldName = await HeldNameAsync(heldId, cancellationToken);
            var held = heldName ?? "the savegame checked out here";
            var step = CheckInStep(heldId, held, keep.ActivatesFirst, profile.Name, publish.TrimmedName);

            yield return step;

            checkIn = new SavegamePublishCheckIn(heldId, held, heldName, step.TrimmedLabel);
            actions.Add(("check in", $"Check in '{held}'"));
        }

        if (keep.ActivatesFirst)
        {
            actions.Add(("activate", $"Activate '{profile.Name}' in '{_game.Name}'"));
        }

        actions.Add(("publish", publish.KeepPlaying
            ? $"Publish '{publish.TrimmedName}' and keep it checked out"
            : $"Publish '{publish.TrimmedName}'"));

        if (actions.Count > 1)
        {
            yield return ReviewStep(publish.TrimmedName, actions);
        }

        _reachedEnd = new SavegamePublishPlan(
            slot, publish.TrimmedName, publish.TrimmedLabel, profile, publish.KeepPlaying, checkIn, keep.ActivatesFirst);
    }

    private async Task<SavegamePublishStepViewModel> PublishStepAsync(SavegameSlotOptionViewModel slot, CancellationToken cancellationToken)
    {
        if (_publishStep is { } made && made.Slot == slot.Ref)
        {
            return made.Step;
        }

        // This slot's own folder: the first snapshot's revision is a declaration about the mods that
        // were beside these bytes.
        var manifest = _manifestStore.TryRead(new ModTargetRef(_game.Identity, slot.Ref.Target));
        var options = await BuildOptionsAsync(manifest?.ProfileId, manifest?.ProfileRevision, cancellationToken);

        var preselected = _preselectProfileId
            ?? (_game.ActiveProfile is ActiveProfile active && active.RepoId == _repo.Id ? active.ProfileId : null);

        var step = new SavegamePublishStepViewModel(
            slot.Label,
            _repo.Name,
            slot.Label,
            options,
            options.FirstOrDefault(x => x.ProfileId == preselected && x.ProfileId is not null),
            options.FirstOrDefault(x => x.ProfileId is not null && x.ProfileId == manifest?.ProfileId)?.Name,
            _savegameSlots.DescribeSlotNumber(_game, slot.Ref));

        _publishStep = (slot.Ref, step);

        return step;
    }

    /// <summary>
    /// Every profile in the repo as something the step can offer, plus the no-mod-list answer last.
    /// </summary>
    private async Task<IReadOnlyList<SavegamePublishOption>> BuildOptionsAsync(
        Guid? appliedProfileId,
        int? appliedRevision,
        CancellationToken cancellationToken)
    {
        // The repo's Saves page is reachable without ever having opened a profile.
        if (_profileService.Profiles.Any(x => x.RepoId == _repo.Id) is false)
        {
            await _profileService.RefreshProfiles(_repo.Id, cancellationToken);
        }

        var options = new List<SavegamePublishOption>();

        foreach (var profile in _profileService.Profiles.Where(x => x.RepoId == _repo.Id).OrderBy(x => x.Name, NaturalOrder.Comparer))
        {
            options.Add(new SavegamePublishOption(
                profile.Id,
                profile.Name,
                SavegameRevisionRules.DeclaredRevisionFor(profile.Id, profile.HeadRevision, appliedProfileId, appliedRevision),
                profile.Id == appliedProfileId));
        }

        options.Add(SavegamePublishOption.NoModList);

        return options;
    }

    private QuestionStepViewModel ActivateStep(Guid profileId, string profileName, string savegameName)
    {
        if (_activateStep is { } made && made.ProfileId == profileId && made.Active == _game.ActiveProfile)
        {
            return made.Step;
        }

        var current = _game.ActiveProfile is ActiveProfile active
            && _profileService.Profiles.FirstOrDefault(x => x.Id == active.ProfileId) is ProfileDto activeProfile
                ? $"'{_game.Name}' is on '{activeProfile.Name}'."
                : $"'{_game.Name}' follows no profile.";

        var step = new QuestionStepViewModel(
            $"Activate '{profileName}'?",
            $"{current} Keeping '{savegameName}' checked out needs '{profileName}' active, so it is activated before the save is published.",
            $"Activate '{profileName}'");

        _activateStep = (profileId, _game.ActiveProfile, step);

        return step;
    }

    private SavegameCheckInStepViewModel CheckInStep(Guid savegameId, string savegameName, bool activates, string profileName, string publishedName)
    {
        if (_checkInStep is { } made && made.SavegameId == savegameId && made.Activates == activates)
        {
            return made.Step;
        }

        var step = _checkInFlow.CreateStep(
            _game,
            savegameId,
            savegameName,
            savegameName,
            activates
                ? $"Checked in so '{profileName}' can be activated."
                : $"Checked in so '{publishedName}' can stay checked out. '{_game.Name}' holds one savegame that follows a mod list.");

        _checkInStep = (savegameId, activates, step);

        return step;
    }

    private ReviewStepViewModel ReviewStep(string savegameName, IReadOnlyList<(string Verb, string Line)> actions)
    {
        var key = string.Join('\n', actions.Select(x => x.Line));

        if (_reviewStep is not null && _reviewKey == key)
        {
            return _reviewStep;
        }

        _reviewKey = key;
        _reviewStep = new ReviewStepViewModel(
            $"Publish '{savegameName}'",
            [.. actions.Select(x => x.Line)],
            ReviewStepViewModel.Sentence([.. actions.Select(x => x.Verb)]));

        return _reviewStep;
    }

    /// <summary>
    /// What the savegame holding the mod folder is called. It may be in another repo, so it is read
    /// from the repo its binding names. Null where it could not be found there.
    /// </summary>
    private async Task<string?> HeldNameAsync(Guid savegameId, CancellationToken cancellationToken)
    {
        if (_heldNames.TryGetValue(savegameId, out var known) is false)
        {
            var binding = _heldSavegames.FindProfileHold(_game.Identity);

            known = binding is SavegameCheckoutBinding held && held.SavegameId == savegameId
                ? (await _savegamesClient.GetSavegamesV1Async(held.RepoId, cancellationToken)).FirstOrDefault(x => x.Id == savegameId)?.Name
                : null;

            _heldNames[savegameId] = known;
        }

        return known;
    }
}
