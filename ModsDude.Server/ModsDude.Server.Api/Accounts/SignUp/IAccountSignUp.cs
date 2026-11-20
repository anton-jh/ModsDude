namespace ModsDude.Server.Api.Accounts.SignUp;

public interface IAccountSignUp
{
    /// <summary>
    /// Creates the user and their account and redeems every code, all in one transaction, or refuses
    /// with what was wrong. The email still has to be confirmed before the account can sign in.
    /// </summary>
    Task<SignUpOutcome> SignUpAsync(SignUpForm form, CancellationToken cancellationToken);
}
