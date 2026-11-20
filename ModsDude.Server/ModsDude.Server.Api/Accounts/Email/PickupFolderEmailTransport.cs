using Microsoft.Extensions.Options;
using System.Net;

namespace ModsDude.Server.Api.Accounts.Email;

/// <summary>Writes each email to an HTML file, for development. Nothing leaves the machine.</summary>
public class PickupFolderEmailTransport(IOptions<EmailOptions> options, ILogger<PickupFolderEmailTransport> logger) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var folder = options.Value.PickupFolder is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "ModsDude", "emails");

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, $"{Guid.CreateVersion7():N}.html");
        var temporary = path + ".tmp";

        var content = $"""
            <!-- To: {WebUtility.HtmlEncode(message.To)} -->
            <!-- Subject: {WebUtility.HtmlEncode(message.Subject)} -->
            {message.Html}
            """;

        await File.WriteAllTextAsync(temporary, content, cancellationToken);
        File.Move(temporary, path);

        logger.LogInformation("Wrote an email to {Path}.", path);
    }
}
