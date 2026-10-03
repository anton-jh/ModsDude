using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Accounts;

public static class AccountRefusal
{
    /// <summary>Whether the server refused the request because the account is blocked, anywhere down the inner chain.</summary>
    public static bool Is(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ApiException<CustomProblemDetails> { Result.Type: ProblemType.UserBlocked })
            {
                return true;
            }
        }

        return false;
    }
}
