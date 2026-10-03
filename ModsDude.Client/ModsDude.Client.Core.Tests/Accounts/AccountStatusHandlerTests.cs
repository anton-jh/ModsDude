using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Accounts;
using System.Net;
using System.Text;

namespace ModsDude.Client.Core.Tests.Accounts;

public class AccountStatusHandlerTests
{
    private const string _blockedBody = """
        {
          "type": "https://server.modsdude.com/api/problems/user-blocked",
          "title": "User blocked",
          "status": 403
        }
        """;

    private readonly AccountStatus _status = new();


    [Fact]
    public async Task A_block_is_reported_and_its_body_can_still_be_read()
    {
        var response = await SendAsync(_ => Respond(HttpStatusCode.Forbidden, _blockedBody));

        Assert.True(_status.IsBlocked);
        Assert.Equal(_blockedBody, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_accepted_request_clears_a_block()
    {
        _status.ReportBlocked();

        await SendAsync(_ => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.False(_status.IsBlocked);
    }

    [Theory]
    [InlineData("""{ "type": "https://server.modsdude.com/api/problems/insufficient-repo-access" }""")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task A_403_that_is_not_a_block_is_left_alone(string body)
    {
        await SendAsync(_ => Respond(HttpStatusCode.Forbidden, body));

        Assert.False(_status.IsBlocked);
    }

    [Fact]
    public async Task Other_failures_change_nothing()
    {
        _status.ReportBlocked();

        await SendAsync(_ => Respond(HttpStatusCode.BadRequest, _blockedBody));
        await SendAsync(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        Assert.True(_status.IsBlocked);
    }

    [Fact]
    public void Changed_is_raised_only_when_the_state_changes()
    {
        var raised = 0;
        _status.Changed += (_, _) => raised++;

        _status.ReportAccepted();
        _status.ReportBlocked();
        _status.ReportBlocked();
        _status.ReportAccepted();

        Assert.Equal(2, raised);
    }


    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new AccountStatusHandler(_status, NullLogger<AccountStatusHandler>.Instance)
        {
            InnerHandler = new StubHandler(respond)
        };

        using var client = new HttpClient(handler);

        return await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://server.test/api/v1/repos"));
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };


    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(respond(request));
        }
    }
}
