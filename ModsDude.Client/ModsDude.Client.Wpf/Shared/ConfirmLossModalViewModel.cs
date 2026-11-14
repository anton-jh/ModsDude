using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Wpf.Shell.Modals;

namespace ModsDude.Client.Wpf.Shared;

public enum ConfirmLossChoice
{
    Cancel,
    Confirm,
    Alternative
}


/// <summary>
/// Asks before something destructive, listing every item that will be lost, one per line.
/// </summary>
/// <param name="alternativeText">A third answer between going ahead and not, such as doing something safer first. Null for none.</param>
public partial class ConfirmLossModalViewModel(
    string title,
    string message,
    IReadOnlyList<string> items,
    string confirmText,
    string cancelText,
    string? alternativeText = null)
    : ModalViewModel
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public IReadOnlyList<string> Items { get; } = items;
    public string ConfirmText { get; } = confirmText;
    public string CancelText { get; } = cancelText;
    public string? AlternativeText { get; } = alternativeText;

    public bool HasAlternative => AlternativeText is not null;

    public ConfirmLossChoice Choice { get; private set; }


    [RelayCommand]
    private void Confirm() => Answer(ConfirmLossChoice.Confirm);

    [RelayCommand]
    private void Alternative() => Answer(ConfirmLossChoice.Alternative);

    [RelayCommand]
    private void Cancel() => Answer(ConfirmLossChoice.Cancel);

    /// <summary>Escape keeps everything. Enter does nothing: losing things takes a click.</summary>
    public override bool TryCancel() => Press(CancelCommand);


    private void Answer(ConfirmLossChoice choice)
    {
        Choice = choice;
        Done = true;
    }
}
