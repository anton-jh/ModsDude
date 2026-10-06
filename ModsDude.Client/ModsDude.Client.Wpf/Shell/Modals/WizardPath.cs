using ModsDude.Client.Core.Exceptions;

namespace ModsDude.Client.Wpf.Shell.Modals;

/// <summary>
/// Finds the next step on a wizard's path, where the path is the steps its answers lead to, worked
/// out one at a time.
/// </summary>
/// <remarks>
/// <b>Lazily, so a step is only worked out once it is the next one.</b> A path is enumerated up to
/// the step after the one just answered and no further, which keeps a costly step - one that reads the
/// disk, say - from being built while the questions before it are still open. Every step before
/// that one has been answered, so the path may read their answers as it goes.
/// </remarks>
public static class WizardPath
{
    /// <returns>
    /// The step after <paramref name="answered"/>, or null where it was the last - in which case the
    /// path has been enumerated to its end.
    /// </returns>
    /// <exception cref="UserFriendlyException">
    /// The answered step is no longer on the path, because the state it was worked out from moved
    /// while the wizard was open.
    /// </exception>
    public static async Task<WizardStepViewModel?> StepAfterAsync(
        IAsyncEnumerable<WizardStepViewModel> path,
        WizardStepViewModel answered)
    {
        var found = false;

        await foreach (var step in path)
        {
            if (found)
            {
                return step;
            }

            found = ReferenceEquals(step, answered);
        }

        return found
            ? null
            : throw new UserFriendlyException(
                "Something changed while you were answering",
                $"The answered step '{answered.Title}' is no longer on the wizard's path, so the state it was worked out from moved while it was open.");
    }
}
