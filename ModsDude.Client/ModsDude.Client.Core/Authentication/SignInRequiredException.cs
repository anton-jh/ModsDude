namespace ModsDude.Client.Core.Authentication;

/// <summary>
/// No token could be had without somebody at the keyboard: nobody is signed in yet, or the signed-in
/// account's session has run out. Ends by the user signing in again.
/// </summary>
public sealed class SignInRequiredException(Exception? innerException = null)
    : Exception("Signing in again is needed before the server can be asked anything.", innerException)
{
    /// <summary>Whether this is anywhere down the chain, as it is once a typed client has wrapped it.</summary>
    public static bool Is(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SignInRequiredException)
            {
                return true;
            }
        }

        return false;
    }
}
