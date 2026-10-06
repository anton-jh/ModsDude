namespace ModsDude.Client.Wpf.Shell.Modals;

/// <summary>
/// A question in a wizard that needs nothing but a sentence and its answers. Cancel, in the wizard's
/// footer, is always the other answer.
/// </summary>
public sealed class QuestionStepViewModel : WizardStepViewModel
{
    public QuestionStepViewModel(string title, string message, IReadOnlyList<WizardChoice> choices)
    {
        Title = title;
        Message = message;
        Choices = choices;
    }

    /// <summary>A question with one way forward, which Enter takes.</summary>
    public QuestionStepViewModel(string title, string message, string answer)
        : this(title, message, [new WizardChoice(answer) { IsDefault = true }])
    {
    }


    public override string Title { get; }

    public string Message { get; }
}
