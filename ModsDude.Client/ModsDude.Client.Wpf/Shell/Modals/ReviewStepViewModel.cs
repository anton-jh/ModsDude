namespace ModsDude.Client.Wpf.Shell.Modals;

/// <summary>
/// The last step of a wizard whose answers add up to more than one action: each of them, in the order
/// they run, and the button that starts them.
/// </summary>
public sealed class ReviewStepViewModel : WizardStepViewModel
{
    /// <param name="actions">One line per action, in the order they run.</param>
    /// <param name="commitLabel">The button that starts them. Never just "Continue".</param>
    public ReviewStepViewModel(string title, IReadOnlyList<string> actions, string commitLabel)
    {
        Title = title;
        Actions = [.. actions.Select((x, i) => new ReviewAction(i + 1, x))];
        Choices = [new WizardChoice(commitLabel) { IsDefault = true }];
    }


    public override string Title { get; }

    public IReadOnlyList<ReviewAction> Actions { get; }


    /// <summary>"Check in, activate and publish", from the verbs of each action in order.</summary>
    public static string Sentence(IReadOnlyList<string> verbs)
    {
        var joined = verbs.Count switch
        {
            0 => "",
            1 => verbs[0],
            _ => $"{string.Join(", ", verbs.Take(verbs.Count - 1))} and {verbs[^1]}"
        };

        return joined.Length == 0 ? joined : char.ToUpperInvariant(joined[0]) + joined[1..];
    }
}


public sealed record ReviewAction(int Number, string Text);
