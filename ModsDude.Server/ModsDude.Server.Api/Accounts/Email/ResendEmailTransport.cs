using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace ModsDude.Server.Api.Accounts.Email;

/// <summary>Sends through Resend's REST API. Every failure to send, a timeout included, is an <see cref="HttpRequestException"/>.</summary>
public class ResendEmailTransport(HttpClient httpClient, IOptions<EmailOptions> options) : IEmailTransport
{
    public const string BaseAddress = "https://api.resend.com/";


    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new SendEmailRequest(options.Value.From, [message.To], message.Subject, message.Html, message.Text))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ResendApiKey);

        HttpResponseMessage sent;

        try
        {
            sent = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("Resend did not answer in time.", exception);
        }

        using var response = sent;

        if (!response.IsSuccessStatusCode)
        {
            // Resend's own error, which never repeats the message and its links.
            var error = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new HttpRequestException($"Resend refused the email with {(int)response.StatusCode}: {error}", null, response.StatusCode);
        }
    }


    private sealed record SendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text);
}
