namespace ModsDude.Server.Api.Accounts.Email;

/// <summary>
/// The emails the account pages send. Sent within the request rather than as background jobs, because
/// a job's stored arguments would hold the link and the token in it.
/// </summary>
public interface IAccountEmails
{
    Task SendEmailConfirmationAsync(string to, string link, CancellationToken cancellationToken);

    Task SendPasswordResetAsync(string to, string link, CancellationToken cancellationToken);

    Task SendEmailChangeAsync(string to, string link, CancellationToken cancellationToken);
}
