using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Builds;

public static class BuildRefusal
{
    /// <summary>Whether the server refused the request because this is not its build, anywhere down the inner chain.</summary>
    public static bool Is(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ApiException<CustomProblemDetails> { Result.Type: ProblemType.ClientBuildMismatch })
            {
                return true;
            }
        }

        return false;
    }
}
