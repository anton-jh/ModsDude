namespace ModsDude.Client.Core.Authentication;

public interface IInteractiveSignIn
{
    /// <summary>
    /// Opens the sign-in or sign-up page in the browser and waits for the user to finish there.
    /// </summary>
    /// <returns>Null where the user turned the sign-in down on the page.</returns>
    /// <exception cref="OperationCanceledException">Cancelled before the browser came back.</exception>
    Task<TokenSet?> SignInAsync(SignInPrompt prompt, CancellationToken cancellationToken);
}

public enum SignInPrompt
{
    SignIn,
    CreateAccount
}
