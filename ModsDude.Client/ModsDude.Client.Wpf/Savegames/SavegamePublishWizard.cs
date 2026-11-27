using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Mods;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Profiles.Editor;
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


/// <param name="Label">What the check-in step was answered with as the snapshot's description.</param>
public sealed record SavegamePublishCheckIn(Guid SavegameId, HeldSavegameName Savegame, string? Label);


/// <summary>How a publish wizard was answered.</summary>
public abstract record SavegamePublishOutcome
{
    private SavegamePublishOutcome()
    {
    }


    public sealed record Publish(SavegamePublishPlan Plan) : SavegamePublishOutcome;

    /// <summary>Publishes nothing: opens the profile's editor looking at the savegame.</summary>
    public sealed record ReviewInEditor(Guid ProfileId, SavegameSlotRef Slot) : SavegamePublishOutcome;

    /// <summary>Publishes nothing: makes a profile of the savegame's mods.</summary>
    public sealed record CreateProfile(SavegameSlotOptionViewModel Slot) : SavegamePublishOutcome;
}


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
    private readonly IGoneRepos _goneRepos;
    private readonly IProfileStore _profileStore;
    private readonly ISyncManifestStore _manifestStore;
    private readonly ISavegameCheckInFlow _checkInFlow;
    private readonly IHeldSavegameNames _heldSavegameNames;
    private readonly IProfileModPinsClient _pinsClient;
    private readonly IModStore _modStore;
    private readonly ILogger _logger;

    /// <summary>Whether this member may edit profiles, and so be sent to one or make one.</summary>
    private readonly bool _canEditProfiles;

    private readonly SavegameSlotPickerStepViewModel _slotStep;

    private (SavegameSlotRef Slot, SavegamePublishStepViewModel Step)? _publishStep;
    private (Guid ProfileId, ActiveProfile? Active, QuestionStepViewModel Step)? _activateStep;
    private (Guid SavegameId, bool Activates, SavegameCheckInStepViewModel Step)? _checkInStep;
    private (SavegameSlotRef Slot, SavegamePublishTarget Target, SavegameModCheckStepViewModel? Step)? _checkStep;
    private ReviewStepViewModel? _reviewStep;
    private string? _reviewKey;

    private IReadOnlyDictionary<Guid, HeldSavegameName>? _heldNames;

    /// <summary>The plan a path enumerated to its end leaves behind.</summary>
    private SavegamePublishOutcome? _reachedEnd;


    public SavegamePublishWizard(
        Repo repo,
        Game game,
        Guid? preselectProfileId,
        SavegameSlotRef? preselectSlot,
        IReadOnlyList<SavegameSlotOptionViewModel> slots,
        ISavegameSlots savegameSlots,
        IHeldSavegames heldSavegames,
        IGoneRepos goneRepos,
        IProfileStore profileStore,
        ISyncManifestStore manifestStore,
        ISavegameCheckInFlow checkInFlow,
        IHeldSavegameNames heldSavegameNames,
        IProfileModPinsClient pinsClient,
        IModStore modStore,
        ILogger logger)
    {
        _repo = repo;
        _game = game;
        _preselectProfileId = preselectProfileId;
        _savegameSlots = savegameSlots;
        _heldSavegames = heldSavegames;
        _goneRepos = goneRepos;
        _profileStore = profileStore;
        _manifestStore = manifestStore;
        _checkInFlow = checkInFlow;
        _heldSavegameNames = heldSavegameNames;
        _pinsClient = pinsClient;
        _modStore = modStore;
        _logger = logger;
        _canEditProfiles = repo.MembershipLevel >= RepoMembershipLevel.Member;

        _slotStep = new SavegameSlotPickerStepViewModel(repo.Name, slots, preselectSlot);
    }


    public WizardStepViewModel First => _slotStep;

    /// <summary>What the wizard was answered with, once its last step has been.</summary>
    public SavegamePublishOutcome? Outcome { get; private set; }


    public async Task<WizardStepViewModel?> NextAsync(WizardStepViewModel answered, CancellationToken cancellationToken)
    {
        _reachedEnd = null;

        var next = await WizardPath.StepAfterAsync(PathAsync(cancellationToken), answered);

        Outcome = next is null ? _reachedEnd : null;

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

        if (profile.CreatesProfile)
        {
            _reachedEnd = new SavegamePublishOutcome.CreateProfile(slot);

            yield break;
        }

        if (profile.ToTarget() is SavegamePublishTarget target
            && await CheckStepAsync(slot, profile.Name, target, cancellationToken) is SavegameModCheckStepViewModel check)
        {
            yield return check;

            if (check.ReviewChosen)
            {
                _reachedEnd = new SavegamePublishOutcome.ReviewInEditor(target.ProfileId, slot.Ref);

                yield break;
            }
        }

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
            var held = await HeldNameAsync(heldId, cancellationToken);
            var step = CheckInStep(heldId, held, keep.ActivatesFirst, profile.Name, publish.TrimmedName);

            yield return step;

            checkIn = new SavegamePublishCheckIn(heldId, held, step.TrimmedLabel);
            actions.Add(("check in", $"Check in {held.Quoted}"));
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

        _reachedEnd = new SavegamePublishOutcome.Publish(new SavegamePublishPlan(
            slot, publish.TrimmedName, publish.TrimmedLabel, profile, publish.KeepPlaying, checkIn, keep.ActivatesFirst));
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
            _heldSavegames.FindProfileHold(_game.Identity) is SavegameCheckoutBinding held && _goneRepos.IsGone(held.RepoId),
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
        await _profileStore.EnsureLoadedAsync(_repo.Id, cancellationToken);

        var options = new List<SavegamePublishOption>();

        foreach (var profile in _profileStore.Live(_repo.Id).OrderBy(x => x.Name, NaturalOrder.Comparer).ThenBy(x => x.Id))
        {
            options.Add(new SavegamePublishOption(
                profile.Id,
                profile.Name,
                SavegameRevisionRules.DeclaredRevisionFor(profile.Id, profile.HeadRevision, appliedProfileId, appliedRevision),
                profile.Id == appliedProfileId));
        }

        options.Add(SavegamePublishOption.NoModList);

        if (_canEditProfiles && _savegameSlots.ListsMods(_game))
        {
            options.Add(SavegamePublishOption.NewFromSavegame);
        }

        return options;
    }

    /// <summary>
    /// How the savegame's mods differ from the revision it would be published on, where the game's
    /// savegames list their mods and anything differs. Null otherwise.
    /// </summary>
    private async Task<SavegameModCheckStepViewModel?> CheckStepAsync(
        SavegameSlotOptionViewModel slot,
        string profileName,
        SavegamePublishTarget target,
        CancellationToken cancellationToken)
    {
        if (_savegameSlots.ListsMods(_game) is false)
        {
            return null;
        }

        if (_checkStep is { } made && made.Slot == slot.Ref && made.Target == target)
        {
            return made.Step;
        }

        var step = await BuildCheckStepAsync(slot, profileName, target, cancellationToken);

        _checkStep = (slot.Ref, target, step);

        return step;
    }

    private async Task<SavegameModCheckStepViewModel?> BuildCheckStepAsync(
        SavegameSlotOptionViewModel slot,
        string profileName,
        SavegamePublishTarget target,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SavegameModReference> listed;

        try
        {
            listed = await _savegameSlots.ReadModsAsync(_game, slot.Ref, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only information: a save whose list cannot be read is still published, as it was before
            // there was a list to compare.
            _logger.LogWarning(exception, "Could not read the mods of slot {Slot}; it is published without comparing them to the profile.", slot.Ref);

            return null;
        }

        var pins = OtherProfilesReader.ToPins(
            await _pinsClient.GetProfileModPinsV1Async(_repo.Id, target.ProfileId, target.Revision, cancellationToken));
        var registered = await _modStore.GetAsync(_repo.Id, cancellationToken);

        var comparison = SavegameModComparison.Compare(
            listed,
            pins,
            registered.Where(x => x.Locked).Select(x => ModKey.From(x.ModId)).ToHashSet());

        if (comparison.IsEmpty)
        {
            return null;
        }

        var names = registered
            .GroupBy(x => ModKey.From(x.ModId))
            .ToDictionary(x => x.Key, x => x.MaxBy(version => version.SequenceNumber)!.DisplayName);

        return new SavegameModCheckStepViewModel(
            profileName,
            target.Revision,
            comparison,
            x => names.GetValueOrDefault(x) ?? x.Value,
            _canEditProfiles);
    }

    private QuestionStepViewModel ActivateStep(Guid profileId, string profileName, string savegameName)
    {
        if (_activateStep is { } made && made.ProfileId == profileId && made.Active == _game.ActiveProfile)
        {
            return made.Step;
        }

        var current = _game.ActiveProfile is ActiveProfile active
            && _profileStore.Find(active.RepoId, active.ProfileId) is Profile activeProfile
                ? $"'{_game.Name}' is on '{activeProfile.Name}'."
                : $"'{_game.Name}' follows no profile.";

        var step = new QuestionStepViewModel(
            $"Activate '{profileName}'?",
            $"{current} Keeping '{savegameName}' checked out needs '{profileName}' active.",
            "Activate");

        _activateStep = (profileId, _game.ActiveProfile, step);

        return step;
    }

    private SavegameCheckInStepViewModel CheckInStep(Guid savegameId, HeldSavegameName savegame, bool activates, string profileName, string publishedName)
    {
        if (_checkInStep is { } made && made.SavegameId == savegameId && made.Activates == activates)
        {
            return made.Step;
        }

        var step = _checkInFlow.CreateStep(
            _game,
            savegameId,
            savegame,
            activates
                ? $"Checked in first to activate '{profileName}'"
                : $"Checked in first to keep '{publishedName}' checked out");

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

    /// <summary>What the savegame holding the mod folder is called, read once per wizard.</summary>
    private async Task<HeldSavegameName> HeldNameAsync(Guid savegameId, CancellationToken cancellationToken)
    {
        _heldNames ??= await _heldSavegameNames.ReadAsync(_game, _repo.Id, cancellationToken);

        return _heldNames.GetValueOrDefault(savegameId) ?? HeldSavegameName.Unknown;
    }
}
