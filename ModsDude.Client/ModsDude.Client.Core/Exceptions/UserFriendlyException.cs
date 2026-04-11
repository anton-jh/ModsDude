namespace ModsDude.Client.Core.Exceptions;
public class UserFriendlyException(
    string userMessage,
    string? developerMessage = null,
    Exception? inner = null)
    : Exception(userMessage, inner)
{
    public string UserMessage { get; } = userMessage;
    public string DeveloperMessage { get; } = developerMessage ?? userMessage;


    public static UserFriendlyException WrapUnknown(Exception exception)
    {
        return new UserFriendlyException("Something broke", exception.Message, exception);
    }

    public static UserFriendlyException RepoNoModSupport()
    {
        return new("The configured game adapter does not support mods");
    }
}
