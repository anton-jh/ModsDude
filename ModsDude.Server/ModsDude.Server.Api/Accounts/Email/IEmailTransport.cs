namespace ModsDude.Server.Api.Accounts.Email;

public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string Html, string Text);
