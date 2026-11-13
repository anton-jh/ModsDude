namespace ModsDude.Client.Core.Exceptions;

/// <summary>
/// A failure said in words for the user. <see cref="UserMessage"/> and <see cref="Hint"/> are all
/// that is ever shown; <see cref="DeveloperMessage"/> goes to the log only.
/// </summary>
/// <param name="developerMessage">What a developer needs to diagnose it: ids, paths, the server's detail. Logged, never shown.</param>
public class UserFriendlyException(
    string userMessage,
    string? developerMessage = null,
    Exception? inner = null)
    : Exception(userMessage, inner)
{
    /// <summary>What happened, in plain, short words.</summary>
    public string UserMessage { get; } = userMessage;

    public string DeveloperMessage { get; } = developerMessage ?? userMessage;

    /// <summary>What the user can do about it, in plain words, where there is something. Shown under the message.</summary>
    public string? Hint { get; init; }

    /// <summary>The message and the hint as sentences, for a place that shows them in running text.</summary>
    public string Describe() => Hint is null ? AsSentence(UserMessage) : $"{AsSentence(UserMessage)} {Hint}";


    public static UserFriendlyException WrapUnknown(Exception exception)
    {
        return new UserFriendlyException("Something broke", inner: exception);
    }

    public static UserFriendlyException RepoNoModSupport()
    {
        return new("The configured game adapter does not support mods");
    }


    private static string AsSentence(string text) => text.EndsWith('.') || text.EndsWith('?') || text.EndsWith('!') ? text : $"{text}.";
}
