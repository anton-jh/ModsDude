using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shell.Modals;
using System.Runtime.CompilerServices;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>What a check-out was answered with: everything the work needs, nothing it has done.</summary>
/// <param name="CheckIns">The savegames checked in first, in the order they are.</param>
/// <param name="Activates">The profile activated before the save is written, or null where none is.</param>
public sealed record SavegameCheckOutPlan(
    SavegameRevisionMode RevisionMode,
    int? PinnedRevision,
    IReadOnlyList<SavegameCheckOutCheckIn> CheckIns,
    ProfileDto? Activates,
    SavegameSlotOptionViewModel Slot);


/// <param name="Label">What the check-in step was answered with as the snapshot's description.</param>
public sealed record SavegameCheckOutCheckIn(Guid SavegameId, HeldSavegameName Savegame, string? Label);


/// <summary>
/// The questions a check-out or a copy asks, one at a time: taking it from somebody, which mod list,
/// activating it, checking in what holds the mod folder, the slot, and checking in what holds the slot.
/// </summary>
/// <remarks>
/// <para>
/// <b>The steps are worked out from the answers each time.</b> The path is walked again from the
/// current answers and the state of the game - see <see cref="WizardPath"/> - so a step that no
/// longer follows is not asked, and the slot step, which reads and hashes the mod folder, is only
/// built once it is the next question.
/// </para>
/// <para>
/// <b>Each step is made once and reused</b> while what it is about stays the same, so going back
/// shows it as it was answered.
/// </para>
/// <para>
/// <b>A copy claims nothing</b>, so it checks nothing in, and activating for it is a choice rather
/// than a step: writing the copy next to whatever mods the folder has now is the user's call.
/// </para>
/// </remarks>
public sealed class SavegameCheckOutWizard
{
    private readonly Repo _repo;
    private readonly Game _game;
    private readonly SavegameDto _savegame;
    private readonly int _snapshotNumber;
    private readonly int? _playedRevision;
    private readonly SavegameCheckOutMode _mode;
    private readonly SavegameRevisionMode? _fixedRevisionMode;
    private readonly SavegameCheckoutDto? _takingFrom;
    private readonly SavegameCompatibilityVerdict? _verdict;
    private readonly IReadOnlyDictionary<Guid, HeldSavegameName> _heldNames;
    private readonly ISavegameOffers _offers;
    private readonly IProfileService _profileService;
    private readonly ISavegameCheckOutContextBuilder _contextBuilder;
    private readonly ISavegameCheckInFlow _checkInFlow;

    private QuestionStepViewModel? _takeOverStep;
    private SavegameCompatibilityStepViewModel? _compatibilityStep;
    private (int? Pinned, QuestionStepViewModel Step)? _activateStep;
    private bool _copyActivates = true;
    private readonly Dictionary<(Guid SavegameId, string Reason), SavegameCheckInStepViewModel> _checkInSteps = [];
    private (int? Pinned, string CheckedInFirst, SavegameCheckOutStepViewModel Step)? _slotStep;
    private (string Key, ReviewStepViewModel Step)? _reviewStep;

    /// <summary>The plan a path enumerated to its end leaves behind.</summary>
    private SavegameCheckOutPlan? _reachedEnd;


    /// <param name="fixedRevisionMode">
    /// Which mod list the save goes onto where that is already decided - by the row's own button, or
    /// by the save being held here in a mode. Null asks, where the latest has moved far enough.
    /// </param>
    /// <param name="takingFrom">Whoever else holds the claim this check-out takes, or null.</param>
    public SavegameCheckOutWizard(
        Repo repo,
        Game game,
        SavegameDto savegame,
        int snapshotNumber,
        int? playedRevision,
        SavegameCheckOutMode mode,
        SavegameRevisionMode? fixedRevisionMode,
        SavegameCheckoutDto? takingFrom,
        SavegameCompatibilityVerdict? verdict,
        IReadOnlyDictionary<Guid, HeldSavegameName> heldNames,
        ISavegameOffers offers,
        IProfileService profileService,
        ISavegameCheckOutContextBuilder contextBuilder,
        ISavegameCheckInFlow checkInFlow)
    {
        _repo = repo;
        _game = game;
        _savegame = savegame;
        _snapshotNumber = snapshotNumber;
        _playedRevision = playedRevision;
        _mode = mode;
        _fixedRevisionMode = fixedRevisionMode;
        _takingFrom = takingFrom;
        _verdict = verdict;
        _heldNames = heldNames;
        _offers = offers;
        _profileService = profileService;
        _contextBuilder = contextBuilder;
        _checkInFlow = checkInFlow;
    }


    /// <summary>The first step, which is worked out like every other.</summary>
    public async Task<WizardStepViewModel> FirstAsync(CancellationToken cancellationToken)
    {
        await foreach (var step in PathAsync(cancellationToken))
        {
            return step;
        }

        throw new InvalidOperationException("A check-out always reaches its slot step.");
    }

    /// <summary>What the wizard was answered with, once its last step has been.</summary>
    public SavegameCheckOutPlan? Plan { get; private set; }


    public async Task<WizardStepViewModel?> NextAsync(WizardStepViewModel answered, CancellationToken cancellationToken)
    {
        _reachedEnd = null;

        var next = await WizardPath.StepAfterAsync(PathAsync(cancellationToken), answered);

        Plan = next is null ? _reachedEnd : null;

        return next;
    }


    private async IAsyncEnumerable<WizardStepViewModel> PathAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_takingFrom is not null)
        {
            yield return TakeOverStep(_takingFrom);
        }

        var revisionMode = _fixedRevisionMode;

        if (revisionMode is null && CompatibilityStep() is SavegameCompatibilityStepViewModel compatibility)
        {
            yield return compatibility;

            revisionMode = compatibility.Result;
        }

        var mode = revisionMode ?? SavegameRevisionMode.Latest;
        var pinned = SavegameRevisionRules.PinnedRevision(mode, _playedRevision);

        var profile = _repo.Adapter.CanSupportMods
            ? _profileService.FindLive(_repo.Id, _savegame.ProfileId)
            : null;

        // The same rule the row drew its button from, read again: the folder may have moved since.
        var offer = _repo.Adapter.CanSupportMods && _offers.ReadHost(_repo) is SavegameHost host
            ? SavegameRowRules.Describe(
                _savegame.Id, _savegame.ProfileId, profile?.HeadRevision, pinned, host.Held, host.AppliedProfileId, host.AppliedRevision)
            : null;

        var list = SavegameRowRules.DescribeActivation(profile?.Name ?? SavegameWording.ProfileOf(_savegame), pinned);

        var checkIns = new List<SavegameCheckOutCheckIn>();
        var actions = new List<(string Verb, string Line)>();
        ProfileDto? activates = null;

        // A profile that cannot be seen activates nothing, and the rule says so: an activation needs
        // the profile's head.
        if (_mode is SavegameCheckOutMode.CheckOut)
        {
            if (offer is { ActivatesFirst: true } && profile is not null)
            {
                yield return ActivateStep(list, pinned);

                activates = profile;
            }

            if (offer?.ChecksInFirst is Guid holding)
            {
                var checkIn = Name(holding);
                var step = CheckInStep(checkIn, activates is not null
                    ? $"Checked in so {list} can be activated."
                    : $"Checked in so '{_savegame.Name}' can be checked out. '{_game.Name}' holds one savegame that follows a mod list.");

                yield return step;

                checkIns.Add(checkIn with { Label = step.TrimmedLabel });
            }
        }
        else if (offer is { ActivatesFirst: true, ChecksInFirst: null } && profile is not null)
        {
            yield return ActivateStep(list, pinned);

            activates = _copyActivates ? profile : null;
        }

        if (activates is not null)
        {
            actions.Add(("activate", $"Activate {list} in '{_game.Name}'"));
        }

        var slotStep = await SlotStepAsync(pinned, checkIns.Select(x => x.SavegameId).ToHashSet(), cancellationToken);

        yield return slotStep;

        var slot = slotStep.SelectedSlot
            ?? throw new InvalidOperationException("The slot step was answered without a slot.");

        if (slotStep.ChecksInOccupantFirst
            && slot is { IsRefused: true, OccupyingSavegameId: Guid occupying }
            && checkIns.All(x => x.SavegameId != occupying))
        {
            var checkIn = Name(occupying);
            var step = CheckInStep(checkIn, "Checked in so its slot is free.");

            yield return step;

            checkIns.Add(checkIn with { Label = step.TrimmedLabel });
        }

        actions.InsertRange(0, checkIns.Select(x => ("check in", $"Check in {x.Savegame.Quoted}")));
        actions.Add(DescribeWrite(slot));

        if (actions.Count > 1)
        {
            yield return ReviewStep(actions);
        }

        _reachedEnd = new SavegameCheckOutPlan(mode, pinned, checkIns, activates, slot);
    }

    private QuestionStepViewModel TakeOverStep(SavegameCheckoutDto holder)
    {
        var name = holder.User.DisplayName;

        return _takeOverStep ??= new QuestionStepViewModel(
            $"{name} has '{_savegame.Name}' checked out",
            $"They have had it since {SavegameWording.Exactly(holder.TakenAt)}. Checking it out takes it from them, "
                + "and their ModsDude will tell them.\n\n"
                + "If they are playing it, you will each have a copy of the same save: whoever checks in second has "
                + "to force it, and that overwrites the other's play.",
            $"Take it from {name}");
    }

    /// <summary>
    /// Asked only where the latest has moved far enough from the revision the snapshot was played on.
    /// </summary>
    private SavegameCompatibilityStepViewModel? CompatibilityStep()
    {
        if (_verdict is not { ShouldPrompt: true } verdict || _playedRevision is not int played)
        {
            return null;
        }

        return _compatibilityStep ??= new SavegameCompatibilityStepViewModel(
            _savegame.Name,
            SavegameWording.ProfileOf(_savegame),
            played,
            verdict.Comparison.To,
            verdict);
    }

    /// <summary>
    /// A check-out holds the save against this folder, so the folder has to be right. A copy claims
    /// nothing, so writing it next to whatever the folder has now is the user's choice.
    /// </summary>
    private QuestionStepViewModel ActivateStep(string list, int? pinned)
    {
        if (_activateStep is { } made && made.Pinned == pinned)
        {
            return made.Step;
        }

        var runsOn = $"'{_savegame.Name}' runs on {list}, and the mod folder in '{_game.Name}' is not on it.";

        var step = _mode is SavegameCheckOutMode.TakeCopy
            ? new QuestionStepViewModel(
                $"Activate {list} first?",
                $"{runsOn} Activating it puts the mods the copy was saved with in place. Leaving it writes the copy "
                    + "next to whatever mods the folder has now, which the game may not load it with.",
                [
                    new WizardChoice("Activate it, then take the copy", () => _copyActivates = true) { IsDefault = true },
                    new WizardChoice("Leave the mods as they are", () => _copyActivates = false) { IsAccent = false }
                ])
            : new QuestionStepViewModel(
                $"Activate {list}?",
                $"{runsOn} It is activated before the save is checked out.",
                $"Activate {list}");

        _activateStep = (pinned, step);

        return step;
    }

    /// <summary>A savegame to check in first, as the plan records it before its step is answered.</summary>
    private SavegameCheckOutCheckIn Name(Guid savegameId)
        => new(savegameId, _heldNames.GetValueOrDefault(savegameId) ?? HeldSavegameName.Unknown, null);

    private SavegameCheckInStepViewModel CheckInStep(SavegameCheckOutCheckIn checkIn, string reason)
    {
        if (_checkInSteps.TryGetValue((checkIn.SavegameId, reason), out var step) is false)
        {
            step = _checkInFlow.CreateStep(_game, checkIn.SavegameId, checkIn.Savegame, reason);
            _checkInSteps[(checkIn.SavegameId, reason)] = step;
        }

        return step;
    }

    private async Task<SavegameCheckOutStepViewModel> SlotStepAsync(
        int? pinned,
        IReadOnlySet<Guid> checkedInFirst,
        CancellationToken cancellationToken)
    {
        var key = string.Join(',', checkedInFirst.Order());

        if (_slotStep is { } made && made.Pinned == pinned && made.CheckedInFirst == key)
        {
            return made.Step;
        }

        var context = await _contextBuilder.BuildAsync(
            _repo, _savegame, _game, _mode, pinned, _verdict, _heldNames, checkedInFirst, cancellationToken);

        var step = new SavegameCheckOutStepViewModel(
            _mode,
            _savegame.Name,
            SavegameWording.ProfileOf(_savegame),
            _snapshotNumber,
            _savegame.Head?.Number ?? _snapshotNumber,
            context);

        _slotStep = (pinned, key, step);

        return step;
    }

    private (string Verb, string Line) DescribeWrite(SavegameSlotOptionViewModel slot)
    {
        var into = SavegameSlotWording.Named(slot.Number, slot.Label);

        if (_mode is SavegameCheckOutMode.TakeCopy)
        {
            return ("take the copy", $"Write a copy of '{_savegame.Name}' into {into}");
        }

        return ("check out", _takingFrom is SavegameCheckoutDto holder
            ? $"Check out '{_savegame.Name}' into {into}, taking it from {holder.User.DisplayName}"
            : $"Check out '{_savegame.Name}' into {into}");
    }

    private ReviewStepViewModel ReviewStep(IReadOnlyList<(string Verb, string Line)> actions)
    {
        var key = string.Join('\n', actions.Select(x => x.Line));

        if (_reviewStep is { } made && made.Key == key)
        {
            return made.Step;
        }

        var step = new ReviewStepViewModel(
            _mode is SavegameCheckOutMode.TakeCopy ? $"Take a copy of '{_savegame.Name}'" : $"Check out '{_savegame.Name}'",
            [.. actions.Select(x => x.Line)],
            ReviewStepViewModel.Sentence([.. actions.Select(x => x.Verb)]));

        _reviewStep = (key, step);

        return step;
    }
}
