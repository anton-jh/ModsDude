using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
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
        bool canCheckOut,
        bool isAmbiguous)
    {
        Savegame = savegame;
        ProfileName = profileName;
        _currentUserId = currentUserId;
        CanCheckOut = canCheckOut;
        ShowHolderTag = isAmbiguous;

        Chips = [];

        RefreshChips();
    }


    /// <summary>Raised when the row's own action is clicked. The page owns both flows.</summary>
    public event EventHandler? CheckOutRequested;
    public event EventHandler? TakeCopyRequested;


    public SavegameDto Savegame { get; }

    public Guid Id => Savegame.Id;
    public string Name => Savegame.Name;
    public string ProfileName { get; }

    /// <summary>Refused for a Guest, and therefore never offered - a picker leading to a refusal is worse than one never offered.</summary>
    public bool CanCheckOut { get; }

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
