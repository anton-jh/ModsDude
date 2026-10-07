using ModsDude.Client.Core.Connectivity;
using System.Net;
using System.Net.Sockets;

namespace ModsDude.Client.Core.Tests.Connectivity;

/// <summary>Every server request sorted into "answered" and "nobody answered".</summary>
public class ServerConnectionHandlerTests
{
    private readonly ServerConnection _connection = new();


    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Any_answer_from_the_server_is_online(HttpStatusCode status)
    {
        _connection.ReportUnreachable();

        await SendAsync(_ => new HttpResponseMessage(status));

        Assert.True(_connection.IsOnline);
    }

    /// <summary>The proxy in front of the server answering for it means the server is not there.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_gateway_answering_for_the_server_is_offline(HttpStatusCode status)
    {
        await SendAsync(_ => new HttpResponseMessage(status));

        Assert.False(_connection.IsOnline);
    }

    [Fact]
    public async Task Nobody_answering_is_offline_and_still_thrown()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(_ =>
            throw new HttpRequestException("No connection could be made.", new SocketException((int)SocketError.ConnectionRefused))));

        Assert.False(_connection.IsOnline);
    }

    [Fact]
    public async Task A_cancelled_request_changes_nothing()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(_ => throw new TaskCanceledException()));
        Assert.True(_connection.IsOnline);

        _connection.ReportUnreachable();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(_ => throw new TaskCanceledException()));
        Assert.False(_connection.IsOnline);
    }

    [Fact]
    public void The_connection_says_it_changed_only_when_it_did()
    {
        var changes = 0;
        _connection.Changed += (_, _) => changes++;

        _connection.ReportReachable();
        _connection.ReportUnreachable();
        _connection.ReportUnreachable();
        _connection.ReportReachable();

        Assert.Equal(2, changes);
    }


    private async Task SendAsync(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        using var client = new HttpClient(new ServerConnectionHandler(_connection) { InnerHandler = new Answering(answer) });

        using var _ = await client.GetAsync("https://modsdude.test/api/v1/changes");
    }


    private sealed class Answering(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(answer(request));
    }
}
