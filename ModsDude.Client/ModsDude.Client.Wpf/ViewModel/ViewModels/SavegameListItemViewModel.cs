using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Users;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One savegame on the repo's Saves list: what it is called, which profile it follows, and one chip
/// saying whose it is right now.
/// </summary>
/// <remarks>
/// <para>
/// <b>The status is the server's, not a recomputation.</b> <see cref="SavegameCheckoutDto.Status"/>
/// already folds "open row" and "past its expiry" into Held, Stale and Ended, reporting Ended ahead of
/// expiry - what actually happened outranks what would have happened. Working that out again here
/// would be a second copy of a rule that has to agree with the server's or be worse than useless.
/// </para>
/// <para>
/// <b>Two of the chips arrive late</b>, because they are not facts about the savegame: whether the
/// slot on <em>this</em> machine has moved needs the disk, and how far behind the save's revision is
/// needs the profile's history. Both are appended when they arrive rather than held up in front of a
/// list that is otherwise ready.
/// </para>
/// </remarks>
public partial class SavegameListItemViewModel : ObservableObject
{
    private readonly string? _currentUserId;

    private bool _hasUnpublishedPlay;
    private int _revisionsBehind;
    private bool _lockedPinMoved;

    /// <summary>
    /// What this machine can do with this savegame, or null where there is no installation of the
    /// game to do it on.
    /// </summary>
    /// <remarks>
    /// <b>Null rather than a value on the rule's enum.</b> Whether a game is connected here is not a
    /// fact about this savegame - it is the absence of the thing the rule is about - so
    /// <see cref="SavegameRowRules"/> is only ever asked where there is one, and the sentence for the
    /// other case is <see cref="_notConnected"/> below.
    /// </remarks>
    private SavegameRowOffer? _offer;
    private string? _blockingSavegameName;

    /// <summary>What both buttons say when this repo's game is not connected on this machine.</summary>
    private const string _notConnected = "This game is not connected here";


    /// <param name="profileName">
    /// The profile this save follows. An attribute of the savegame rather than its parent, which is
    /// exactly why this list is one repo-level list with a profile column rather than a list per
    /// profile.
    /// </param>
    /// <param name="isAmbiguous">
    /// Whether somebody else holding a save in this same list is called the same thing. It is the
    /// list that decides that, not the person, so it arrives from outside.
    /// </param>
    public SavegameListItemViewModel(
        SavegameDto savegame,
        string profileName,
        string? currentUserId,
        bool isMember,
        bool isAmbiguous)
    {
        Savegame = savegame;
        ProfileName = profileName;
        _currentUserId = currentUserId;
        IsMember = isMember;
        ShowHolderTag = isAmbiguous;

        // Recorded here because the row's two actions need it and because it is what the binding will
        // carry a moment later - two answers to "which list does this savegame run on" is how a row comes
        // to describe a different apply from the one that runs.
        PinnedRevision = SavegameService.TargetRevisionOf(savegame);

        Chips = [];

        RefreshChips();
    }


    /// <summary>Raised when the row's own action is clicked. The page owns all of the flows.</summary>
    public event EventHandler? CheckOutRequested;
    public event EventHandler? CheckInRequested;
    public event EventHandler? TakeCopyRequested;
    public event EventHandler? ApplyProfileRequested;
    public event EventHandler? MakeCurrentRequested;


    public SavegameDto Savegame { get; }

    public Guid Id => Savegame.Id;
    public string Name => Savegame.Name;
    public string ProfileName { get; }

    /// <summary>Refused for a Guest, and therefore never offered - a picker leading to a refusal is worse than one never offered.</summary>
    public bool IsMember { get; }

    /// <summary>
    /// The revision this savegame runs on where it pins one, from
    /// <see cref="SavegameService.TargetRevisionOf"/>. Null for a current savegame, which follows its
    /// profile, and for one that follows no mod list.
    /// </summary>
    public int? PinnedRevision { get; }

    /// <summary>Whether this is a <em>past</em> savegame - one its profile has moved on from.</summary>
    /// <remarks>
    /// A fact about which savegame a profile is following, not a problem with either, which is why the
    /// chip saying it is <see cref="SavegameChipTone.Neutral"/> and why the list hides these rows by
    /// default rather than colouring them.
    /// </remarks>
    public bool IsPast => Savegame.SupersededAt is not null;

    /// <summary>Whether this savegame follows a mod list at all.</summary>
    public bool HasProfile => Savegame.ProfileId is not null;

    /// <summary>
    /// Whether taking the claim is on offer here and now: Member, and nothing about this machine in
    /// the way. <see cref="CheckOutBlockedReason"/> is the half that says why not.
    /// </summary>
    public bool CanCheckOut => IsMember && _offer?.CanCheckOut is true;

    /// <summary>Whether putting the mod folder on this savegame's list is on offer. Member, like check-out.</summary>
    public bool CanApplyProfile => IsMember && _offer?.CanApply is true;

    /// <summary>
    /// Whether this savegame can be put back in its profile's current slot.
    /// </summary>
    /// <remarks>
    /// Only a past one has anywhere to go: a current savegame is already there, and one following no mod
    /// list is in no succession. Nothing about this machine gates it - which savegame a profile follows is
    /// a decision about the repo, like publishing, and is gated the same way.
    /// </remarks>
    public bool CanMakeCurrent => IsMember && IsPast;

    /// <summary>
    /// The installation on this machine whose slot actually holds this savegame, or null where none
    /// does - including where the claim is yours but you took it somewhere else.
    /// </summary>
    /// <remarks>
    /// <b>Still a question worth asking now that a repo offers one game.</b> Whether that game would
    /// accept a check-out and whether it is already holding this save are different facts: a claim
    /// taken on the desktop is still yours on the laptop, and there is nothing there to check in.
    /// That row falls back to checking out, which is the honest offer - it fetches the save onto this
    /// machine and renews the claim it already has.
    /// </remarks>
    public Game? HeldHere { get; private set; }

    public bool IsHeldHere => HeldHere is not null;

    /// <summary>
    /// Whether handing the save back is what this row offers - which needs the claim to be yours
    /// <em>and</em> the copy to be on this machine.
    /// </summary>
    /// <remarks>
    /// Both halves, because they come apart: a claim taken on the desktop is still yours on the
    /// laptop, and there is nothing there to check in. That row falls back to checking out, which is
    /// the honest offer - it fetches the save onto this machine and renews the claim it already has.
    /// </remarks>
    public bool CanCheckIn => IsMember && IsHeldByMe && IsHeldHere;

    /// <summary>
    /// Whether taking the save is the row's accent button. Exactly one of this and
    /// <see cref="CanCheckIn"/> is ever true, so a row always has one obvious thing to do rather than
    /// two competing for the eye.
    /// </summary>
    public bool ChecksOutAsPrimary => IsMember && CanCheckIn is false;

    /// <summary>
    /// Whether taking it <em>again</em> is offered quietly beside Check in. That is a real thing to
    /// want - it is how a save moves to a different slot - but it is not what somebody holding a save
    /// usually came to do, so it does not get the accent.
    /// </summary>
    public bool ChecksOutAsSecondary => CanCheckIn;

    /// <summary>
    /// The check-out button's own word. "Again" wherever the claim is already yours, because a button
    /// offering to check out a save the row has just said you have is one that reads as a bug.
    /// </summary>
    public string CheckOutLabel => IsHeldByMe ? "Check out again" : "Check out";

    public string? CheckOutBlockedReason => _offer is SavegameRowOffer offer
        ? SavegameRowRules.Explain(offer.CheckOut, ProfileName, offer.PinnedRevision, _blockingSavegameName)
        : _notConnected;

    public string? ApplyBlockedReason => _offer is SavegameRowOffer offer
        ? SavegameRowRules.Explain(offer.Apply, ProfileName, offer.PinnedRevision, _blockingSavegameName)
        : _notConnected;

    /// <summary>
    /// What the row says under its buttons: the check-out refusal, which is the one somebody is
    /// acting on. Applying is the way out of it, so its own refusal is only worth a line where it is
    /// the one that differs - which is a savegame following no mod list, where there is nothing to apply.
    /// </summary>
    public string? BlockedReason => CheckOutBlockedReason ?? ApplyBlockedReason;

    public bool IsBlocked => BlockedReason is not null;

    /// <summary>
    /// The tooltip on each button: the refusal where there is one, and what the button does where
    /// there is not. A disabled button whose only explanation is its greyness is what this replaces.
    /// </summary>
    public string CheckOutToolTip => CheckOutBlockedReason
        ?? (IsHeldByMe
            ? "Writes the newest version into a slot again and renews your claim - which is how this save moves to a different slot, or onto this machine."
            : "Takes the claim and writes it into a slot. Nobody else can take it until you check it in.");

    /// <summary>
    /// Never a refusal: the button is only there when the save is yours and on this machine, which is
    /// the whole of what checking in needs.
    /// </summary>
    public string CheckInToolTip =>
        "Uploads what is in the slot as a new version and hands the save back, so somebody else can take it. A save that changed nothing mints nothing.";

    public string ApplyToolTip => ApplyBlockedReason
        ?? (PinnedRevision is int revision
            ? $"Puts this game's mod folder on '{ProfileName}' revision {revision}, which is what this savegame runs on."
            : $"Puts this game's mod folder on '{ProfileName}', which is what this savegame runs on.");

    public ObservableCollection<SavegameChip> Chips { get; }

    /// <summary>The head version's number and size, for the row's second line. Empty where nothing has been checked in yet.</summary>
    public string Summary => Savegame.Head is SavegameVersionDto head
        ? $"Version {head.Number} · {SavegameWording.Size(head.SizeBytes)} · {SavegameWording.Ago(head.Created)}"
        : "No versions yet";

    /// <summary>
    /// What the game says about the head version - the map, the hours in it - recorded by whoever
    /// checked it in and read here by everybody else. It is the half of a savegame row that is about
    /// the save rather than about the sharing of it.
    /// </summary>
    public string? GameSummary => Savegame.Head is SavegameVersionDto head && head.Details.Count > 0
        ? string.Join(" · ", head.Details.Select(x => x.Value))
        : null;

    public bool HasGameSummary => GameSummary is not null;

    /// <summary>Whoever holds it, or null where nobody does.</summary>
    public SavegameCheckoutDto? Holder => Savegame.Checkout is SavegameCheckoutDto checkout
        && checkout.Status is not SavegameCheckoutStatus.Ended
        ? checkout
        : null;

    public bool IsHeldByMe => Holder is SavegameCheckoutDto held
        && _currentUserId is not null
        && held.User.Id == _currentUserId;

    public bool ShowHolderTag { get; }

    public string? HolderTag => Holder?.User.Tag;
    public string? HolderColor => Holder is SavegameCheckoutDto held ? UserDisplay.ColorFor(held.User.Tag) : null;
    public string? HolderInitial => Holder is SavegameCheckoutDto held ? UserDisplay.InitialFor(held.User.DisplayName) : null;
    public bool HasHolder => Holder is not null;


    [RelayCommand(CanExecute = nameof(CanCheckOut))]
    private void CheckOut() => CheckOutRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Hands the save back from the slot holding it, as a new version.
    /// </summary>
    /// <remarks>
    /// The same flow the game's own slot list runs, reached from here because this is the list
    /// somebody is looking at when they finish an evening - and a row saying "You have it" whose only
    /// button offered to take it again was the thing that sent them hunting for the other page.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanCheckIn))]
    private void CheckIn() => CheckInRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The other half of the pair. Two actions rather than one because checking a save out never
    /// syncs mods: a plan that would quarantine files the repo has never seen has to be shown before
    /// anything is written, and folding it into a claim is how that disclosure gets skipped. See
    /// docs/10-savegame-profile-binding.md#two-actions-not-one.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyProfile))]
    private void ApplyProfile() => ApplyProfileRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Puts this savegame back in its profile's current slot, displacing whichever one is there.
    /// </summary>
    /// <remarks>
    /// One of the two things that change which savegame a profile is following, and the other way
    /// round from publishing - same swap, seen from the other end. Both are stated before they run,
    /// which is why this opens a confirmation naming what it displaces rather than acting on the
    /// click. See docs/10-savegame-profile-binding.md#current-and-past-savegames.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanMakeCurrent))]
    private void MakeCurrent() => MakeCurrentRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Open to everybody, Guest included. It is what makes the list worth showing to somebody who
    /// cannot take the claim: they can still read the history and play a copy.
    /// </summary>
    [RelayCommand]
    private void TakeCopy() => TakeCopyRequested?.Invoke(this, EventArgs.Empty);


    /// <summary>
    /// Records that the slot this machine holds for this savegame has moved since it was written -
    /// which is play that exists nowhere else, and the one caution the row can raise about itself.
    /// </summary>
    public void SetUnpublishedPlay(bool hasUnpublishedPlay)
    {
        if (_hasUnpublishedPlay == hasUnpublishedPlay)
        {
            return;
        }

        _hasUnpublishedPlay = hasUnpublishedPlay;

        RefreshChips();
    }

    /// <summary>
    /// Records what this machine can do with this savegame right now, from
    /// <see cref="SavegameRowRules.Describe"/>.
    /// </summary>
    /// <remarks>
    /// Arrives from outside for the same reason the drift chips do: none of it is a fact about the
    /// savegame. It needs the games this repo offers, what each one is holding and what its mod
    /// folder was last synced to - none of which a row has or should have.
    /// </remarks>
    /// <param name="blockingSavegameName">
    /// What the savegame already claiming the mod folder is called, where the page could find it in
    /// its own list.
    /// </param>
    /// <summary>
    /// Records which installation on this machine is holding the local copy - the fact that decides
    /// whether this row's primary action is Check in or Check out.
    /// </summary>
    /// <remarks>
    /// Arrives from outside for the same reason the offer does: it is not a fact about the savegame.
    /// It needs every game this repo offers and what each one's binding store says it is holding,
    /// none of which a row has.
    /// </remarks>
    public void SetHeldHere(Game? game)
    {
        if (ReferenceEquals(HeldHere, game))
        {
            return;
        }

        HeldHere = game;

        OnPropertyChanged(nameof(HeldHere));
        OnPropertyChanged(nameof(IsHeldHere));
        OnPropertyChanged(nameof(CanCheckIn));
        OnPropertyChanged(nameof(ChecksOutAsPrimary));
        OnPropertyChanged(nameof(ChecksOutAsSecondary));

        CheckInCommand.NotifyCanExecuteChanged();
    }

    /// <param name="offer">
    /// What the game this repo offers can do with this savegame, or null where none is connected on
    /// this machine.
    /// </param>
    public void SetOffer(SavegameRowOffer? offer, string? blockingSavegameName)
    {
        _offer = offer;
        _blockingSavegameName = blockingSavegameName;

        OnPropertyChanged(nameof(CanCheckOut));
        OnPropertyChanged(nameof(CanApplyProfile));
        OnPropertyChanged(nameof(CheckOutBlockedReason));
        OnPropertyChanged(nameof(ApplyBlockedReason));
        OnPropertyChanged(nameof(BlockedReason));
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(CheckOutToolTip));
        OnPropertyChanged(nameof(ApplyToolTip));

        CheckOutCommand.NotifyCanExecuteChanged();
        ApplyProfileCommand.NotifyCanExecuteChanged();
    }

    /// <param name="lockedPinMoved">
    /// Whether a <em>locked</em> pin moved between the two revisions. Only that turns the chip
    /// caution-coloured: an unlocked mod at a different version is untidy, a locked map at a different
    /// version is a damaged save waiting to happen.
    /// </param>
    public void SetRevisionDrift(int revisionsBehind, bool lockedPinMoved)
    {
        if (_revisionsBehind == revisionsBehind && _lockedPinMoved == lockedPinMoved)
        {
            return;
        }

        _revisionsBehind = revisionsBehind;
        _lockedPinMoved = lockedPinMoved;

        RefreshChips();
    }


    private void RefreshChips()
    {
        Chips.Clear();
        Chips.Add(BuildStateChip());

        // Current is the unmarked default, so only the exception carries one of these. Both are
        // Neutral and neither is ever Caution: which savegame a profile is following, and whether a savegame
        // follows one at all, are facts rather than problems - and spending the loud tone on them is
        // what teaches people to ignore it where it does mean a damaged save.
        if (IsPast)
        {
            Chips.Add(new SavegameChip(
                PinnedRevision is int revision ? $"Past · {ProfileName} rev {revision}" : $"Past · {ProfileName}",
                SavegameChipTone.Neutral));
        }
        else if (HasProfile is false)
        {
            Chips.Add(new SavegameChip("No mod list", SavegameChipTone.Neutral));
        }

        if (_hasUnpublishedPlay)
        {
            Chips.Add(new SavegameChip("unpublished play", SavegameChipTone.Caution));
        }

        if (_revisionsBehind > 0)
        {
            Chips.Add(new SavegameChip(
                SavegameWording.RevisionsBehind(_revisionsBehind),
                _lockedPinMoved ? SavegameChipTone.Caution : SavegameChipTone.Neutral));
        }

        OnPropertyChanged(nameof(HasHolder));
        OnPropertyChanged(nameof(Holder));
        OnPropertyChanged(nameof(IsHeldByMe));
    }

    /// <summary>
    /// The vocabulary the member list already uses: a person, what they have, and since when. A stale
    /// claim is said in a different tense on purpose - "has had it since 3 March" is somebody who
    /// forgot, and it has to read differently from somebody who is playing.
    /// </summary>
    private SavegameChip BuildStateChip()
    {
        if (Holder is not SavegameCheckoutDto holder)
        {
            return new SavegameChip("Available", SavegameChipTone.Neutral);
        }

        if (IsHeldByMe)
        {
            return new SavegameChip("You have it", SavegameChipTone.Accent);
        }

        var name = holder.User.DisplayName;

        return holder.Status is SavegameCheckoutStatus.Stale
            ? new SavegameChip($"{name} has had it since {SavegameWording.OnDate(holder.TakenAt)}", SavegameChipTone.Neutral)
            : new SavegameChip($"{name} has it, since {SavegameWording.Ago(holder.TakenAt)}", SavegameChipTone.Neutral);
    }
}
