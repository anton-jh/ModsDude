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
    public async Task A_server_that_never_answers_is_offline_and_thrown_as_a_timeout()
    {
        var time = new TestTimeProvider();
        using var client = new HttpClient(new ServerConnectionHandler(_connection, time) { InnerHandler = new Hanging() })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        var request = client.GetAsync("https://modsdude.test/api/v1/changes");
        await WaitForTimerAsync(time);
        time.Advance(ServerConnectionHandler.RequestTimeout);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);

        Assert.True(ConnectionFailure.Is(exception));
        Assert.False(_connection.IsOnline);
    }

    [Fact]
    public async Task A_caller_giving_up_on_a_slow_server_changes_nothing()
    {
        var time = new TestTimeProvider();
        using var client = new HttpClient(new ServerConnectionHandler(_connection, time) { InnerHandler = new Hanging() })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var cancellation = new CancellationTokenSource();

        var request = client.GetAsync("https://modsdude.test/api/v1/changes", cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.True(_connection.IsOnline);
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
        using var client = new HttpClient(new ServerConnectionHandler(_connection, new TestTimeProvider()) { InnerHandler = new Answering(answer) });

        using var _ = await client.GetAsync("https://modsdude.test/api/v1/changes");
    }


    private static async Task WaitForTimerAsync(TestTimeProvider time)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (time.PendingTimers == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The request never started waiting.");

            await Task.Delay(10);
        }
    }

    private sealed class Hanging : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            throw new InvalidOperationException("Never answers.");
        }
    }

    private sealed class Answering(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(answer(request));
    }
}
