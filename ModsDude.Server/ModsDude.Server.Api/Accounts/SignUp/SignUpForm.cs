namespace ModsDude.Server.Api.Accounts.SignUp;

/// <param name="RequestId">Chosen when the page was loaded, and repeated by a resubmit.</param>
/// <param name="InviteCodes">Blank rows are ignored.</param>
public sealed record SignUpForm(
    Guid RequestId,
    string Email,
    string DisplayName,
    string Password,
    string? TrustCode,
    IReadOnlyList<string?> InviteCodes);

public abstract record SignUpOutcome
{
    /// <param name="IsRepeat">Whether this was a resubmit of a sign-up that had already succeeded.</param>
    public sealed record Succeeded(string AccountId, string Email, bool IsRepeat) : SignUpOutcome;

    public sealed record Refused(IReadOnlyList<SignUpError> Errors) : SignUpOutcome;
}

/// <param name="InviteIndex">Which invite row, where <paramref name="Field"/> is <see cref="SignUpField.InviteCode"/>.</param>
public sealed record SignUpError(SignUpField Field, string Message, int? InviteIndex = null);

public enum SignUpField
{
    Form,
    Email,
    DisplayName,
    Password,
    TrustCode,
    InviteCode
}
