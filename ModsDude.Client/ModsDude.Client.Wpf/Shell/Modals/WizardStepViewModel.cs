using CommunityToolkit.Mvvm.ComponentModel;

namespace ModsDude.Client.Wpf.Shell.Modals;

/// <summary>
/// One question in a <see cref="WizardModalViewModel"/>: its content and the answers it offers. The
/// frame, the title and the buttons belong to the wizard.
/// </summary>
/// <remarks>
/// The answer stays on the step, so going back to it shows what was chosen, and the flow reads it
/// once the wizard is done.
/// </remarks>
public abstract class WizardStepViewModel : ObservableObject
{
    private readonly IReadOnlyList<WizardChoice> _choices = [];


    protected WizardStepViewModel()
    {
        // Any change to the step can change what a choice says or whether it can be chosen.
        PropertyChanged += (_, _) =>
        {
            foreach (var choice in _choices)
            {
                choice.Refresh();
            }
        };
    }


    public abstract string Title { get; }

    public IReadOnlyList<WizardChoice> Choices
    {
        get => _choices;
        protected init => _choices = value;
    }
}
