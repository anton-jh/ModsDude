using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>Which of the two download modes this dialog is confirming.</summary>
public enum SavegameCheckOutMode
{
    /// <summary>Takes the claim, writes the slot, binds it, and puts the mods right.</summary>
    CheckOut,

    /// <summary>
    /// Writes the slot and nothing else - no claim, no binding, no mods. What a Guest is offered, and
    /// what a Member uses to look at an old version without holding the save hostage.
    /// </summary>
    TakeCopy
}


/// <summary>
/// What the mod folder would have to do, or that it would have to do nothing.
/// </summary>
/// <param name="LockedNames">
/// The locked mods whose version moves. Named rather than counted, because this is the moment a map
/// at the wrong version stops being untidy and starts being a damaged save.
/// </param>
public sealed record SavegameModsSummary(
    bool AlreadyCorrect,
    string Text,
    IReadOnlyList<string> LockedNames,
    string? Consequence)
{
    public bool HasLocked => LockedNames.Count > 0;

    public string LockedText => $"Locked, and moving: {string.Join(", ", LockedNames)}.";

    public bool HasConsequence => Consequence is { Length: > 0 };
}


/// <summary>
/// How far the save's own revision is from the profile's current one, in the one sentence that says
/// it. Caution only where a <em>locked</em> pin moved between them - the case that damages saves.
/// </summary>
public sealed record SavegameRevisionNote(string Text, bool IsCaution);


/// <summary>
/// Everything the dialog needs about one instance. Recomputed when the instance selection changes,
/// because the slots, the mod plan and the revision note are all facts about a particular folder.
/// </summary>
public sealed record SavegameCheckOutContext(
    LocalInstance Instance,
    IReadOnlyList<SavegameSlotOptionViewModel> Slots,
    SavegameSlotId? Suggested,
    string? SlotNote,
    SavegameModsSummary? Mods,
    SavegameRevisionNote? Revision);


/// <summary>
/// The one confirmation a check-out gets, carrying four sections - mods, instance, slot, revision -
/// each of which disappears when it has nothing to say.
/// </summary>
/// <remarks>
/// <para>
/// <b>The common case is a name, a slot and one button.</b> That is the whole design: an ordinary
/// evening, where the mods are already right and there is one instance and a free slot, must not read
/// like a form. Everything here is either an answer the user has to give or a consequence they have
/// to see, and anything that is neither is absent rather than greyed out.
/// </para>
/// <para>
/// <b>The picker is shown on every check-out.</b> The remembered slot pre-selects it; it never decides
/// it. What varies is only whether the full list is open on arrival - it is when the suggestion is
/// missing, so that "no free slot" is a state somebody can see and act on rather than a dialog that
/// looks broken.
/// </para>
/// <para>
/// <b>A slot holding unchecked-in play is refused, not warned about.</b> Confirming a write there
/// would be a button whose consequence is somebody's evening, and no wording makes that safe - so the
/// dialog offers checking that savegame in instead, as a single action.
/// </para>
/// </remarks>
public partial class SavegameCheckOutModalViewModel : ModalViewModel
{
    private readonly Func<LocalInstance, CancellationToken, Task<SavegameCheckOutContext>> _load;
    private readonly int _headVersion;

    private bool _reloading;


    /// <param name="versionNumber">
    /// The version being taken. Where it is not the head, this dialog is also confirming the restore
    /// that copies it forward - said out loud rather than hidden, because it mints a version.
    /// </param>
    public SavegameCheckOutModalViewModel(
        SavegameCheckOutMode mode,
        string savegameName,
        string profileName,
        int versionNumber,
        int headVersion,
        IReadOnlyList<LocalInstance> instances,
        SavegameCheckOutContext context,
        Func<LocalInstance, CancellationToken, Task<SavegameCheckOutContext>> load)
    {
        Mode = mode;
        SavegameName = savegameName;
        ProfileName = profileName;
        VersionNumber = versionNumber;
        _headVersion = headVersion;
        _load = load;

        Instances = [.. instances];
        Slots = [];

        _selectedInstance = context.Instance;

        Apply(context);
    }


    public SavegameCheckOutMode Mode { get; }

    public string SavegameName { get; }
    public string ProfileName { get; }
    public int VersionNumber { get; }

    public string Title => Mode is SavegameCheckOutMode.TakeCopy
        ? $"Take a copy of '{SavegameName}'"
        : $"Check out '{SavegameName}'";

    /// <summary>
    /// The one line under the title. An older version says that taking it copies it forward, because
    /// that is a version somebody else will see appear.
    /// </summary>
    public string Intro
    {
        get
        {
            if (Mode is SavegameCheckOutMode.TakeCopy)
            {
                return VersionNumber == _headVersion
                    ? "A copy, and nothing more: nobody is stopped from playing it, and this machine records no claim on it. The slot is an ordinary save of your own from then on."
                    : $"A copy of version {VersionNumber}, and nothing more: nobody is stopped from playing it, and this machine records no claim on it. The slot is an ordinary save of your own from then on.";
            }

            return VersionNumber == _headVersion
                ? "Nobody else can check this out until you check it back in."
                : $"Version {VersionNumber} is copied forward as the newest version first - nothing in between is deleted - and that is what lands in the slot. Nobody else can check it out until you check it back in.";
        }
    }

    /// <summary>Every instance this repo offers. The section is absent where there is one.</summary>
    public IReadOnlyList<LocalInstance> Instances { get; }

    public ObservableCollection<SavegameSlotOptionViewModel> Slots { get; }

    /// <summary>The chosen instance and slot, or null where the dialog was dismissed.</summary>
    public SavegameCheckOutResult? Result { get; private set; }

    /// <summary>
    /// Set instead of <see cref="Result"/> when the user took the way out of a refused slot. The page
    /// checks that savegame in and offers this dialog again.
    /// </summary>
    public Guid? CheckInFirstSavegameId { get; private set; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstanceSection))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private LocalInstance? _selectedInstance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSlotRefused))]
    [NotifyPropertyChangedFor(nameof(SlotWarning))]
    [NotifyPropertyChangedFor(nameof(HasSlotWarning))]
    [NotifyPropertyChangedFor(nameof(BlockedActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckInBlockingSavegameCommand))]
    private SavegameSlotOptionViewModel? _selectedSlot;

    /// <summary>
    /// Whether the whole list is open. False shows the pre-selected row and a <b>Change</b> next to
    /// it; there is no state in which the slot is decided without being shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPreselection))]
    private bool _showAllSlots;

    /// <summary>Why the pre-selection is what it is, when it is not simply the remembered slot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSlotNote))]
    private string? _slotNote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowModsSection))]
    private SavegameModsSummary? _mods;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRevisionSection))]
    private SavegameRevisionNote? _revision;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isBusy;


    /// <summary>Absent where the repo offers one instance - there is nothing to ask.</summary>
    public bool ShowInstanceSection => Instances.Count > 1;

    public bool ShowModsSection => Mods is not null;
    public bool ShowRevisionSection => Revision is not null;

    public bool ShowPreselection => ShowAllSlots is false;
    public bool HasSlotNote => SlotNote is { Length: > 0 };

    public bool IsSlotRefused => SelectedSlot?.IsRefused is true;

    public string BlockedActionLabel => SelectedSlot?.BlockedAction ?? "Check that savegame in first";

    /// <summary>
    /// The consequence of writing into the chosen slot, where there is one. Shown on the dialog rather
    /// than saved for a second confirmation, so that the button underneath can carry it too.
    /// </summary>
    public string? SlotWarning => SelectedSlot switch
    {
        null => null,
        { IsRefused: true } slot => slot.SaveName is { Length: > 0 } name
            ? $"'{name}' has been played here and never checked in. It exists nowhere else, so nothing may be written over it."
            : "This slot holds play that has never been checked in. It exists nowhere else, so nothing may be written over it.",
        { Availability: Core.Savegames.SavegameSlotAvailability.Unrecognised } slot => slot.SaveName is { Length: > 0 } name
            ? $"'{name}' is in this slot and is not from this repo. It goes to the Recycle Bin, where you can put it back."
            : "The save in this slot is not from this repo. It goes to the Recycle Bin, where you can put it back.",
        { Availability: Core.Savegames.SavegameSlotAvailability.HeldClean } slot => slot.OccupyingSavegameName is { Length: > 0 } name
            ? $"'{name}' is checked out here and has not been played. It is on the server already, so nothing is lost."
            : "A checked-out savegame is here and has not been played. It is on the server already, so nothing is lost.",
        _ => null
    };

    public bool HasSlotWarning => SlotWarning is not null;

    /// <summary>The verb, carrying what it costs. Never just "Continue".</summary>
    public string ConfirmLabel
    {
        get
        {
            var write = Mode is SavegameCheckOutMode.TakeCopy ? "Write the copy" : "Check it out";

            return SelectedSlot?.Availability switch
            {
                Core.Savegames.SavegameSlotAvailability.Unrecognised => SelectedSlot.SaveName is { Length: > 0 } name
                    ? $"{write} - '{name}' goes to the Recycle Bin"
                    : $"{write} - the save here goes to the Recycle Bin",
                Core.Savegames.SavegameSlotAvailability.HeldClean => $"{write} over the save here",
                _ => write
            };
        }
    }


    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (SelectedInstance is not LocalInstance instance || SelectedSlot is not SavegameSlotOptionViewModel slot)
        {
            return;
        }

        Result = new SavegameCheckOutResult(instance, slot);
        Done = true;
    }

    private bool CanConfirm()
        => IsBusy is false && SelectedInstance is not null && SelectedSlot is { IsRefused: false };

    /// <summary>
    /// The one action a refused slot offers. It closes this dialog rather than checking in behind it:
    /// the page owns that flow, and the answer changes what every section here says.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckInBlockingSavegame))]
    private void CheckInBlockingSavegame()
    {
        CheckInFirstSavegameId = SelectedSlot?.OccupyingSavegameId;
        Done = true;
    }

    private bool CanCheckInBlockingSavegame() => SelectedSlot is { IsRefused: true, OccupyingSavegameId: not null };

    /// <summary>Opens the full list. There is no way back to the collapsed form, and no need for one.</summary>
    [RelayCommand]
    private void ChangeSlot() => ShowAllSlots = true;

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        Done = true;
    }


    /// <summary>
    /// A different instance is a different folder, so every section is re-read rather than patched.
    /// The guard is what stops the reload's own write to <see cref="SelectedInstance"/> - when the
    /// load fails and the selection is put back - from starting another one.
    /// </summary>
    partial void OnSelectedInstanceChanged(LocalInstance? value)
    {
        if (_reloading || value is null)
        {
            return;
        }

        _ = ReloadAsync(value);
    }

    private async Task ReloadAsync(LocalInstance instance)
    {
        IsBusy = true;

        try
        {
            Apply(await _load(instance, CancellationToken.None));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nothing awaits this, so an escaping exception would go unobserved rather than reaching
            // the shell's handler. The dialog says so and offers no slots, which refuses Confirm.
            _reloading = true;

            try
            {
                Slots.Clear();
                SelectedSlot = null;
                SlotNote = $"'{instance.Name}' could not be read: {exception.Message}";
                ShowAllSlots = true;
            }
            finally
            {
                _reloading = false;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(SavegameCheckOutContext context)
    {
        _reloading = true;

        try
        {
            SelectedInstance = context.Instance;

            Slots.Clear();

            foreach (var slot in context.Slots)
            {
                Slots.Add(slot);
            }

            SlotNote = context.SlotNote;
            Mods = context.Mods;
            Revision = context.Revision;

            SelectedSlot = context.Suggested is SavegameSlotId suggested
                ? Slots.FirstOrDefault(x => string.Equals(x.Id.Value, suggested.Value, StringComparison.OrdinalIgnoreCase))
                : null;

            // Nothing pre-selected means the remembered slot is gone and none is free, which is a
            // state the user has to see rather than a dialog that looks empty.
            ShowAllSlots = SelectedSlot is null;
        }
        finally
        {
            _reloading = false;
        }

        OnPropertyChanged(nameof(ConfirmLabel));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSlotChanged(SavegameSlotOptionViewModel? value)
    {
        OnPropertyChanged(nameof(ConfirmLabel));
    }
}


/// <summary>What the dialog settled: which instance, and which slot in it.</summary>
public sealed record SavegameCheckOutResult(LocalInstance Instance, SavegameSlotOptionViewModel Slot);
