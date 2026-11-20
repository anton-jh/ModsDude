using System.Net;

namespace ModsDude.Server.Api.Accounts.Email;

public class AccountEmails(IEmailTransport transport) : IAccountEmails
{
    public Task SendEmailConfirmationAsync(string to, string link, CancellationToken cancellationToken)
        => SendAsync(to, "Confirm your email", "Open this link to confirm your email and finish signing up.", "Confirm email", link, cancellationToken);

    public Task SendPasswordResetAsync(string to, string link, CancellationToken cancellationToken)
        => SendAsync(to, "Reset your password", "Open this link to choose a new password. If you did not ask for this, ignore this email.", "Reset password", link, cancellationToken);

    public Task SendEmailChangeAsync(string to, string link, CancellationToken cancellationToken)
        => SendAsync(to, "Confirm your new email", "Open this link to use this address for your ModsDude account.", "Confirm new email", link, cancellationToken);


    private Task SendAsync(string to, string subject, string body, string action, string link, CancellationToken cancellationToken)
    {
        var encodedLink = WebUtility.HtmlEncode(link);

        var html = $"""
            <!DOCTYPE html>
            <html>
            <body style="margin:0;padding:32px 16px;background:#121212;color:#e8e8e8;font-family:system-ui,-apple-system,Segoe UI,sans-serif;">
            <div style="max-width:32rem;margin:0 auto;padding:24px;background:#1c1c1c;border:1px solid #3a3a3a;border-radius:8px;">
            <h1 style="margin:0 0 16px;font-size:1.25rem;">{WebUtility.HtmlEncode(subject)}</h1>
            <p style="margin:0 0 24px;line-height:1.5;">{WebUtility.HtmlEncode(body)}</p>
            <p style="margin:0 0 24px;"><a href="{encodedLink}" style="display:inline-block;padding:10px 16px;background:#4f7cff;color:#ffffff;text-decoration:none;border-radius:6px;">{WebUtility.HtmlEncode(action)}</a></p>
            <p style="margin:0;color:#a0a0a0;font-size:0.875rem;word-break:break-all;">{encodedLink}</p>
            </div>
            </body>
            </html>
            """;

        var text = $"""
            {subject}

            {body}

            {link}
            """;

        return transport.SendAsync(new EmailMessage(to, subject, html, text), cancellationToken);
    }
}
