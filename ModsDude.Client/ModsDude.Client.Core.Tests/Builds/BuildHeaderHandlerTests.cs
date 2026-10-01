using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Builds;
using System.Net;
using System.Text;

namespace ModsDude.Client.Core.Tests.Builds;

public class BuildHeaderHandlerTests
{
    private const string _mismatchBody = """
        {
          "type": "https://server.modsdude.com/api/problems/client-build-mismatch",
          "title": "The client does not match the server",
          "status": 412,
          "clientBuild": 10,
          "serverBuild": 11
        }
        """;

    private readonly ServerCompatibility _compatibility = new(new BuildNumber(10));


    [Fact]
    public async Task Every_request_carries_this_build()
    {
        HttpRequestMessage? sent = null;

        await SendAsync(request =>
        {
            sent = request;

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(["10"], sent!.Headers.GetValues(BuildHeaderHandler.HeaderName));
    }

    [Fact]
    public async Task A_refusal_is_reported_and_its_body_can_still_be_read()
    {
        var response = await SendAsync(_ => Respond(HttpStatusCode.PreconditionFailed, _mismatchBody));

        Assert.Equal(new BuildNumber(11), _compatibility.Mismatch?.Server);
        Assert.Equal(_mismatchBody, await response.Content.ReadAsStringAsync());
        Assert.Equal(_mismatchBody, await new StreamReader(await response.Content.ReadAsStreamAsync()).ReadToEndAsync());
    }

    [Fact]
    public async Task An_acceptance_clears_a_server_that_was_behind()
    {
        _compatibility.ReportRefused(new BuildNumber(9));

        await SendAsync(_ => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.Null(_compatibility.Mismatch);
    }

    [Theory]
    [InlineData("""{ "type": "https://server.modsdude.com/api/problems/not-found", "serverBuild": 11 }""")]
    [InlineData("""{ "type": "https://server.modsdude.com/api/problems/client-build-mismatch" }""")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task A_412_that_is_not_a_readable_build_mismatch_is_left_alone(string body)
    {
        await SendAsync(_ => Respond(HttpStatusCode.PreconditionFailed, body));

        Assert.Null(_compatibility.Mismatch);
    }

    [Fact]
    public async Task Other_failures_change_nothing()
    {
        _compatibility.ReportRefused(new BuildNumber(9));

        await SendAsync(_ => Respond(HttpStatusCode.BadRequest, _mismatchBody));
        await SendAsync(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        Assert.Equal(new BuildNumber(9), _compatibility.Mismatch?.Server);
    }

    [Fact]
    public async Task A_cancelled_request_reports_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SendAsync(_ => Respond(HttpStatusCode.PreconditionFailed, _mismatchBody), cancelled.Token));

        Assert.Null(_compatibility.Mismatch);
    }


    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        CancellationToken cancellationToken = default)
    {
        var handler = new BuildHeaderHandler(new BuildNumber(10), _compatibility, NullLogger<BuildHeaderHandler>.Instance)
        {
            InnerHandler = new StubHandler(respond)
        };

        using var client = new HttpClient(handler);

        return await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://server.test/api/v1/repos"), cancellationToken);
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
