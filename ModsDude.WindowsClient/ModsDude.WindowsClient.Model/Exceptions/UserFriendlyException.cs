namespace ModsDude.WindowsClient.Model.Exceptions;
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
        return new UserFriendlyException("Something went wrong", exception.Message, exception);
    }
}
