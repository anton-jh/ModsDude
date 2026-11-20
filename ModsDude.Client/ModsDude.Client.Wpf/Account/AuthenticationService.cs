using ModsDude.Client.Core.Authentication;
using System.Windows;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// The app's one account. Tokens are only ever had silently; a browser opens only for a sign-in the
/// user asked for, through <see cref="PromptAsync"/>.
/// </summary>
/// <remarks>
/// There is no signing out. A new sign-in is prompted for and held as a <see cref="PendingSignIn"/>
/// before the current account is replaced, so a cancelled or refused switch leaves the current user
/// exactly where they were.
/// </remarks>
public sealed class AuthenticationService(
    ITokenSession session,
    IInteractiveSignIn interactiveSignIn)
    : IAuthenticationService
{
    private CancellationTokenSource? _prompt;


    public event EventHandler<SignedInAccount>? AccountChanged;

    public event EventHandler? NeedsSignInChanged;


    public SignedInAccount? CurrentAccount => session.Identity is SignedInIdentity identity ? Describe(identity) : null;

    public bool NeedsSignIn { get; private set; }


    public async Task<string> Get(CancellationToken cancellationToken)
    {
        try
        {
            return await session.GetAccessTokenAsync(cancellationToken);
        }
        catch (SignInRequiredException)
        {
            if (CurrentAccount is not null)
            {
                SetNeedsSignIn(true);
            }

            throw;
        }
    }

    public async Task<bool> TrySignInSilentlyAsync(CancellationToken cancellationToken)
    {
        var before = session.Identity?.Subject;

        if (!await session.TryResumeAsync(cancellationToken))
        {
            return false;
        }

        Adopted(before);
        return true;
    }

    /// <remarks>
    /// A browser tab closed without finishing never answers, so the attempt behind it would wait
    /// forever. Asking again is how the user says they gave up, so a new prompt ends the old one.
    /// </remarks>
    public async Task<PendingSignIn?> PromptAsync(SignInPrompt prompt, CancellationToken cancellationToken)
    {
        // Called from the UI thread and resumed on it, so the one open prompt is never raced.
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _prompt?.Cancel();
        _prompt = attempt;

        try
        {
            return await interactiveSignIn.SignInAsync(prompt, attempt.Token) is TokenSet tokens
                ? new PendingSignIn(tokens, Describe(tokens.Identity))
                : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (_prompt == attempt)
            {
                _prompt = null;
            }
        }
    }

    public async Task AdoptAsync(PendingSignIn signIn)
    {
        var before = session.Identity?.Subject;

        await session.AdoptAsync(signIn.Tokens, CancellationToken.None);

        Adopted(before);
    }


    private void Adopted(string? previousSubject)
    {
        SetNeedsSignIn(false);

        if (CurrentAccount is SignedInAccount account && account.Id != previousSubject)
        {
            OnUiThread(() => AccountChanged?.Invoke(this, account));
        }
    }

    private void SetNeedsSignIn(bool value)
    {
        if (NeedsSignIn == value)
        {
            return;
        }

        NeedsSignIn = value;
        OnUiThread(() => NeedsSignInChanged?.Invoke(this, EventArgs.Empty));
    }

    private static void OnUiThread(Action action)
    {
        if (Application.Current is Application app && app.CheckAccess() is false)
        {
            app.Dispatcher.Invoke(action);

            return;
        }

        action();
    }

    private static SignedInAccount Describe(SignedInIdentity identity)
        => new(identity.Subject, identity.Name);
}
