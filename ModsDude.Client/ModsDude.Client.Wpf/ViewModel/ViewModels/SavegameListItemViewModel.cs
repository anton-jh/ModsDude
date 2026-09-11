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

    private SavegameRowOffer _offer = new(SavegameRowBlock.NoInstance, SavegameRowBlock.NoInstance, Guid.Empty, null);
    private string? _blockingSavegameName;


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
        // carry a moment later - two answers to "which list does this farm run on" is how a row comes
        // to describe a different apply from the one that runs.
        PinnedRevision = SavegameService.TargetRevisionOf(savegame);

        Chips = [];

        RefreshChips();
    }


    /// <summary>Raised when the row's own action is clicked. The page owns all three flows.</summary>
    public event EventHandler? CheckOutRequested;
    public event EventHandler? TakeCopyRequested;
    public event EventHandler? ApplyProfileRequested;


    public SavegameDto Savegame { get; }

    public Guid Id => Savegame.Id;
    public string Name => Savegame.Name;
    public string ProfileName { get; }

    /// <summary>Refused for a Guest, and therefore never offered - a picker leading to a refusal is worse than one never offered.</summary>
    public bool IsMember { get; }

    /// <summary>
    /// The revision this farm runs on where it pins one, from
    /// <see cref="SavegameService.TargetRevisionOf"/>. Null for a current savegame, which follows its
    /// profile, and for one that follows no mod list.
    /// </summary>
    public int? PinnedRevision { get; }

    /// <summary>Whether this is a <em>past</em> savegame - one its profile has moved on from.</summary>
    /// <remarks>
    /// A fact about which farm a profile is following, not a problem with either, which is why the
    /// chip saying it is <see cref="SavegameChipTone.Neutral"/> and why the list hides these rows by
    /// default rather than colouring them.
    /// </remarks>
    public bool IsPast => Savegame.SupersededAt is not null;

    /// <summary>Whether this farm follows a mod list at all.</summary>
    public bool HasProfile => Savegame.ProfileId is not null;

    /// <summary>
    /// Whether taking the claim is on offer here and now: Member, and nothing about this machine in
    /// the way. <see cref="CheckOutBlockedReason"/> is the half that says why not.
    /// </summary>
    public bool CanCheckOut => IsMember && _offer.CanCheckOut;

    /// <summary>Whether putting the mod folder on this farm's list is on offer. Member, like check-out.</summary>
    public bool CanApplyProfile => IsMember && _offer.CanApply;

    /// <summary>
    /// The installation both buttons act on, decided by the page.
    /// </summary>
    /// <remarks>
    /// Held here rather than worked out at the click, so the instance the refusal is about is the
    /// instance the action runs against. Two answers to "where would this go" is how a row comes to
    /// explain one folder and act on another.
    /// </remarks>
    public LocalInstance? Host { get; set; }

    public string? CheckOutBlockedReason
        => SavegameRowRules.Explain(_offer.CheckOut, ProfileName, _offer.PinnedRevision, _blockingSavegameName);

    public string? ApplyBlockedReason
        => SavegameRowRules.Explain(_offer.Apply, ProfileName, _offer.PinnedRevision, _blockingSavegameName);

    /// <summary>
    /// What the row says under its buttons: the check-out refusal, which is the one somebody is
    /// acting on. Applying is the way out of it, so its own refusal is only worth a line where it is
    /// the one that differs - which is a farm following no mod list, where there is nothing to apply.
    /// </summary>
    public string? BlockedReason => CheckOutBlockedReason ?? ApplyBlockedReason;

    public bool IsBlocked => BlockedReason is not null;

    /// <summary>
    /// The tooltip on each button: the refusal where there is one, and what the button does where
    /// there is not. A disabled button whose only explanation is its greyness is what this replaces.
    /// </summary>
    public string CheckOutToolTip => CheckOutBlockedReason
        ?? "Takes the claim and writes it into a slot. Nobody else can take it until you check it in.";

    public string ApplyToolTip => ApplyBlockedReason
        ?? (PinnedRevision is int revision
            ? $"Puts this game's mod folder on '{ProfileName}' revision {revision}, which is what this farm runs on."
            : $"Puts this game's mod folder on '{ProfileName}', which is what this farm runs on.");

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
    /// The other half of the pair. Two actions rather than one because checking a save out never
    /// syncs mods: a plan that would quarantine files the repo has never seen has to be shown before
    /// anything is written, and folding it into a claim is how that disclosure gets skipped. See
    /// docs/10-savegame-profile-binding.md#two-actions-not-one.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyProfile))]
    private void ApplyProfile() => ApplyProfileRequested?.Invoke(this, EventArgs.Empty);

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
    /// savegame. It needs the instances this repo offers, what each one is holding and what its mod
    /// folder was last synced to - none of which a row has or should have.
    /// </remarks>
    /// <param name="blockingSavegameName">
    /// What the savegame already claiming the mod folder is called, where the page could find it in
    /// its own list.
    /// </param>
    public void SetOffer(SavegameRowOffer offer, string? blockingSavegameName)
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
        // Neutral and neither is ever Caution: which farm a profile is following, and whether a farm
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
