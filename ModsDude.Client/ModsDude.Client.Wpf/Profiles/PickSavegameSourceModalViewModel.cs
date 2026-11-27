using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Wpf.Savegames;
using ModsDude.Client.Wpf.Shell.Modals;
using System.Globalization;

namespace ModsDude.Client.Wpf.Profiles;

/// <summary>Picks a savegame on this machine to read as a source: the mods it lists go on the left.</summary>
public partial class PickSavegameSourceModalViewModel : ModalViewModel
{
    public PickSavegameSourceModalViewModel(IReadOnlyList<SavegameSourceOption> savegames)
    {
        Savegames = savegames;
        _selected = savegames.FirstOrDefault();
    }


    public IReadOnlyList<SavegameSourceOption> Savegames { get; }

    public string Title => "Read a savegame";

    public string Message => "Lists the mods it was played with on the left as a source.";

    public bool HasSavegames => Savegames.Count > 0;

    public string EmptyText => "No savegames on this machine";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private SavegameSourceOption? _selected;

    /// <summary>Null until something is confirmed, so a dismissed modal adds no source.</summary>
    public SavegameSourceOption? Result { get; private set; }


    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = Selected;
        Done = true;
    }

    private bool CanConfirm() => Selected is not null;

    [RelayCommand]
    private void Cancel()
    {
        Done = true;
    }


    public override bool IsDismissable => true;

    public override bool TryCancel() => Press(CancelCommand);

    public override bool TryAccept() => Press(ConfirmCommand);
}


/// <summary>An occupied slot of one of this repo's games, as a row of the savegame source picker.</summary>
/// <param name="nameGame">Whether the row names its game, where the repo has more than one on this machine.</param>
public sealed class SavegameSourceOption(Game game, GameSavegameSlot slot, bool nameGame)
{
    private readonly IReadOnlyList<SavegameDetailLine> _details
        = SavegameDetailText.Lines(slot.Details, CultureInfo.CurrentCulture, TimeZoneInfo.Local);


    public Game Game => game;
    public GameSavegameSlot Slot => slot;

    public string Label => slot.DisplayName is { Length: > 0 } name ? name : "Unnamed";

    public int? Number => slot.Number;

    public string Detail => string.Join(" · ", (nameGame ? [game.Name] : Array.Empty<string>())
        .Concat(_details.Take(SavegameSlotWording.DetailsOnTheRow).Select(x => x.Text)));

    public string ToolTip => SavegameSlotWording.DescribeFully(Label, slot.Ref, slot.TargetName, _details, slot.Number);
}
