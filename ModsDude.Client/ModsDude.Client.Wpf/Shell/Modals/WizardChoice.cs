using CommunityToolkit.Mvvm.ComponentModel;

namespace ModsDude.Client.Wpf.Shell.Modals;

/// <summary>
/// One forward answer a wizard step offers, drawn as a button in the wizard's footer.
/// </summary>
/// <remarks>
/// The label, whether it can be chosen and whether it is shown are read off the step each time it
/// changes - see <see cref="WizardStepViewModel"/> - so a step states them once as expressions
/// rather than keeping them in sync.
/// </remarks>
/// <param name="choose">Records the answer on the step. Nothing about the work happens here.</param>
public sealed class WizardChoice(Func<string> label, Action? choose = null) : ObservableObject
{
    public WizardChoice(string label, Action? choose = null)
        : this(() => label, choose)
    {
    }


    public string Label => label();

    public bool IsAccent { get; init; } = true;

    /// <summary>What Enter presses. At most one per step, and none where Enter should do nothing.</summary>
    public bool IsDefault { get; init; }

    public Func<bool> EnabledWhen { get; init; } = () => true;

    public Func<bool> VisibleWhen { get; init; } = () => true;

    public bool IsVisible => VisibleWhen();

    public bool CanChoose => IsVisible && EnabledWhen();


    internal void Choose() => choose?.Invoke();

    internal void Refresh()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(CanChoose));
    }
}
