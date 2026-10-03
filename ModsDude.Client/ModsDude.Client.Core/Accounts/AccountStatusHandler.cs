using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.ModsDudeServer;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Net;

namespace ModsDude.Client.Core.Accounts;

/// <summary>
/// Reports to <see cref="IAccountStatus"/> whether the server accepted the signed-in account. The
/// response is passed on untouched, so the caller still sees a refusal as the API error it is.
/// </summary>
public sealed class AccountStatusHandler(
    IAccountStatus status,
    ILogger<AccountStatusHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            status.ReportAccepted();
        }
        else if (response.StatusCode == HttpStatusCode.Forbidden
            && await ProblemResponse.ReadAsync(response, logger, cancellationToken) is { Type: ProblemType.UserBlocked })
        {
            status.ReportBlocked();
        }

        return response;
    }
}
