using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Users;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Where on this machine the local copy of a savegame is sitting.
/// </summary>
/// <remarks>
/// <b>The slot list folded into the savegame list, and this is what carries it.</b> There used to be
/// a page per game listing every place it can hold a save; it was a second list of the same holds
/// keyed the other way round, reachable only through a game page nobody had reason to open. A
/// savegame that is checked out is one row here, and where it is sitting is a fact about that row.
/// </remarks>
/// <param name="FolderName">
/// Which of the game's savegame folders, or null where it reaches one - the same rule every folder
/// name in the app follows. A single-folder game does not have its save in a folder <em>called</em>
/// something, and a slot id is a name the player has never thought in.
/// </param>
/// <param name="IsUnreachable">
/// Whether the folder holding it is one the game's settings no longer name. The save is still on this
/// disk and still claimed; nothing can read, pack or recycle it until the settings point back at that
/// folder. See <see cref="ModsDude.Client.Core.Savegames.ISavegameService.GetUnreachableHolds"/>.
/// </param>
public sealed record SavegameHoldHere(Game Game, SavegameSlotRef Slot, string? FolderName, bool IsUnreachable);


/// <summary>
/// One savegame on the repo's Saves list: what it is called, which profile it follows, and one chip
/// saying whose it is right now.
/// </summary>
/// <remarks>
/// <para>
/// <b>The status is the server's, not a recomputation.</b> <see cref="SavegameCheckoutDto.Status"/>
/// already folds "open row" into Held and Ended. Working that out again here would be a second copy of
/// a rule that has to agree with the server's or be worse than useless.
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
    public event EventHandler? DiscardRequested;
    public event EventHandler? DisconnectRequested;
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
    public Game? HeldHere => Hold?.Game;

    public bool IsHeldHere => Hold is not null;

    /// <summary>
    /// Where the local copy is sitting, or null where it is not on this machine. See
    /// <see cref="SavegameHoldHere"/>.
    /// </summary>
    public SavegameHoldHere? Hold { get; private set; }

    /// <summary>
    /// Whether the folder holding the local copy is one the game's settings no longer name. Nothing
    /// that touches the bytes works in that state - there is no folder to pack or recycle - so the
    /// row's only offer is to stop tracking it.
    /// </summary>
    public bool IsHoldUnreachable => Hold?.IsUnreachable is true;

    /// <summary>
    /// Whether handing the save back is what this row offers - which needs the claim to be yours
    /// <em>and</em> the copy to be on this machine, in a folder that can still be reached.
    /// </summary>
    /// <remarks>
    /// The first two halves come apart: a claim taken on the desktop is still yours on the laptop,
    /// and there is nothing there to check in. That row falls back to checking out, which is the
    /// honest offer - it fetches the save onto this machine and renews the claim it already has.
    /// </remarks>
    public bool CanCheckIn => IsMember && IsHeldByMe && IsHeldHere && IsHoldUnreachable is false;

    /// <summary>
    /// Whether giving the save back without minting a snapshot is on offer - the verb for a save
    /// taken by mistake and never played.
    /// </summary>
    /// <remarks>
    /// <b>Exactly where Check in is, and beside it.</b> They are the two ways out of holding a save
    /// and the choice between them is about what happened while you had it, not about where you are
    /// standing - so putting one on the savegame list and the other on a slot list somewhere else was
    /// how somebody came to check a save in because Discard was not in front of them.
    /// </remarks>
    public bool CanDiscard => CanCheckIn;

    /// <summary>
    /// Whether ModsDude can be told to forget the local copy. Offered only where the folder holding
    /// it has gone out of the settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one state nothing else gets out of.</b> Check in and Discard both need the bytes, and
    /// there are none to be had: the game's settings no longer name the folder they are in. This
    /// writes local state alone, which is why it still works when everything else is refused.
    /// </para>
    /// <para>
    /// <b>Not gated on membership</b>, because it writes nothing anybody else can see. A guest
    /// holding a save is as entitled to stop holding it as anybody.
    /// </para>
    /// <para>
    /// <b>And not offered on an ordinary held row.</b> "Keep this as my own save while the claim stays
    /// taken" is a real thing to want and a rare one, and a third button on every held row is what it
    /// would cost. A hold whose savegame the repo has deleted is not here either - there is no reason
    /// to leave one standing, so it is dropped on sight rather than turned into a question.
    /// </para>
    /// </remarks>
    public bool CanDisconnect => IsHeldHere && IsHoldUnreachable;

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
            ? "Writes the newest snapshot into a slot again and renews your claim - which is how this save moves to a different slot, or onto this machine."
            : "Takes the claim and writes it into a slot. Nobody else can take it until you check it in.");

    /// <summary>
    /// Never a refusal: the button is only there when the save is yours and on this machine, which is
    /// the whole of what checking in needs.
    /// </summary>
    public string CheckInToolTip =>
        "Uploads what is in the slot as a new snapshot and hands the save back, so somebody else can take it. A save that changed nothing mints nothing.";

    public string DiscardToolTip =>
        "Hands the claim back without minting a snapshot. The local copy goes to the Recycle Bin, so this is for a save taken by mistake rather than one that has been played.";

    public string DisconnectToolTip =>
        "ModsDude forgets this copy. Nothing on disk changes and the server is not told, so the claim stays yours.";

    /// <summary>
    /// Which of the game's savegame folders the local copy is in, where that is worth saying.
    /// </summary>
    /// <remarks>
    /// <b>Null in the ordinary case, deliberately.</b> The chip already says the save is yours, and a
    /// game that reaches one savegame folder does not have the copy in a folder <em>called</em>
    /// anything - so there is nothing here that a slot id would not make worse. It speaks up for a
    /// game with several folders, which is the case the game's own slot list used to exist for.
    /// </remarks>
    public string? HoldNote => Hold is { IsUnreachable: false, FolderName: string named }
        ? $"Your copy is in the '{named}' folder."
        : null;

    public bool HasHoldNote => HoldNote is not null;

    /// <summary>
    /// Why the local copy cannot be reached, where it cannot. Null - nearly always - for a hold in a
    /// folder the settings still name.
    /// </summary>
    /// <remarks>
    /// Its own property rather than a tone on <see cref="HoldNote"/>, because the two are not the
    /// same kind of statement: which of three folders a save is in is a fact, and a folder that has
    /// left the settings is a save nothing on this machine can open. Only the second is coloured.
    /// </remarks>
    public string? UnreachableHoldNote
    {
        get
        {
            if (Hold is not { IsUnreachable: true } hold)
            {
                return null;
            }

            var where = hold.FolderName is string folder
                ? $"in '{folder}', a folder this game's settings no longer name"
                : "in a folder this game's settings no longer name";

            return $"Your copy is {where}. It is still on this disk and still claimed - point the settings " +
                   "back at that folder to check it in, or stop tracking it here.";
        }
    }

    public bool HasUnreachableHoldNote => UnreachableHoldNote is not null;

    public string ApplyToolTip => ApplyBlockedReason
        ?? (PinnedRevision is int revision
            ? $"Puts this game's mod folder on '{ProfileName}' revision {revision}, which is what this savegame runs on."
            : $"Puts this game's mod folder on '{ProfileName}', which is what this savegame runs on.");

    public ObservableCollection<SavegameChip> Chips { get; }

    /// <summary>The head snapshot's number and size, for the row's second line. Empty where nothing has been checked in yet.</summary>
    public string Summary => Savegame.Head is SavegameSnapshotDto head
        ? $"Snapshot {head.Number} · {SavegameWording.Size(head.SizeBytes)} · {SavegameWording.Ago(head.Created)}"
        : "No snapshots yet";

    /// <summary>
    /// What the game says about the head snapshot - the map, the hours in it - recorded by whoever
    /// checked it in and read here by everybody else. It is the half of a savegame row that is about
    /// the save rather than about the sharing of it.
    /// </summary>
    public string? GameSummary => Savegame.Head is SavegameSnapshotDto head && head.Details.Count > 0
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
    /// Hands the save back from the slot holding it, as a new snapshot.
    /// </summary>
    /// <remarks>
    /// Here rather than on a slot list of its own, because this is the list somebody is looking at when
    /// they finish an evening - and a row saying "You have it" whose only button offered to take it
    /// again was the thing that sent them hunting for the other page.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanCheckIn))]
    private void CheckIn() => CheckInRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Gives the save back without minting anything - taken by mistake, never played.
    /// </summary>
    /// <remarks>
    /// Beside Check in, because it is the other half of the same decision. It used to be a row action
    /// on the game's own slot list, which is a page away from the list somebody is looking at when
    /// they realise they took the wrong save.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void Discard() => DiscardRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Stops tracking the local copy, for a hold in a folder the settings no longer name.
    /// </summary>
    /// <remarks>
    /// The way out of the binding rather than out of the checkout: nothing on disk changes and the
    /// server is not told, which is what makes it the only thing that can work when there is no
    /// folder left to read.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect() => DisconnectRequested?.Invoke(this, EventArgs.Empty);

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
    /// Records where on this machine the local copy is sitting - the fact that decides whether this
    /// row's primary action is Check in or Check out, and which of the three ways out of a hold it
    /// offers.
    /// </summary>
    /// <remarks>
    /// Arrives from outside for the same reason the offer does: it is not a fact about the savegame.
    /// It needs the game this repo offers, what its binding store says it is holding and which of its
    /// folders the settings still name - none of which a row has.
    /// </remarks>
    public void SetHeldHere(SavegameHoldHere? hold)
    {
        if (Hold == hold)
        {
            return;
        }

        Hold = hold;

        OnPropertyChanged(nameof(Hold));
        OnPropertyChanged(nameof(HeldHere));
        OnPropertyChanged(nameof(IsHeldHere));
        OnPropertyChanged(nameof(IsHoldUnreachable));
        OnPropertyChanged(nameof(CanCheckIn));
        OnPropertyChanged(nameof(CanDiscard));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(HoldNote));
        OnPropertyChanged(nameof(HasHoldNote));
        OnPropertyChanged(nameof(UnreachableHoldNote));
        OnPropertyChanged(nameof(HasUnreachableHoldNote));
        OnPropertyChanged(nameof(ChecksOutAsPrimary));
        OnPropertyChanged(nameof(ChecksOutAsSecondary));

        CheckInCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
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
    /// snapshot is a damaged save waiting to happen.
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
    /// The vocabulary the member list already uses: a person, what they have, and since when. A claim
    /// does not expire - it is held until it ends - so how long ago it was taken is the whole of what
    /// there is to say, and the reader is left to judge whether that is long enough to take it over.
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

        return new SavegameChip($"{name} has it, since {SavegameWording.Ago(holder.TakenAt)}", SavegameChipTone.Neutral);
    }
}
