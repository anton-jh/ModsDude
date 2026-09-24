using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Net;
using System.Net.Sockets;

namespace ModsDude.Client.Core.Tests.Connectivity;

/// <summary>
/// Nobody answering is worth waiting out; somebody answering no is not.
/// </summary>
public class ConnectionFailureTests
{
    [Fact]
    public void A_refused_connection_counts()
    {
        var exception = new HttpRequestException("No connection could be made.", new SocketException((int)SocketError.ConnectionRefused));

        Assert.True(ConnectionFailure.Is(exception));
    }

    [Fact]
    public void An_http_client_timeout_counts()
    {
        var exception = new TaskCanceledException("The request was canceled.", new TimeoutException());

        Assert.True(ConnectionFailure.Is(exception));
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void A_gateway_with_nothing_behind_it_counts(int status)
    {
        Assert.True(ConnectionFailure.Is(Api(status)));
        Assert.True(ConnectionFailure.Is(new HttpRequestException("Bad gateway", null, (HttpStatusCode)status)));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(500)]
    public void An_answer_from_the_server_does_not(int status)
    {
        Assert.False(ConnectionFailure.Is(Api(status)));
        Assert.False(ConnectionFailure.Is(new HttpRequestException("Answered", null, (HttpStatusCode)status)));
    }

    [Fact]
    public void A_cancellation_nobody_timed_out_does_not()
    {
        Assert.False(ConnectionFailure.Is(new OperationCanceledException()));
    }

    [Fact]
    public void A_failure_wrapped_by_whatever_asked_for_a_token_still_counts()
    {
        var exception = new InvalidOperationException("Token acquisition failed.",
            new HttpRequestException("No such host is known.", new SocketException((int)SocketError.HostNotFound)));

        Assert.True(ConnectionFailure.Is(exception));
    }

    [Fact]
    public void What_the_caller_also_counts_is_asked_of_the_whole_chain()
    {
        var exception = new InvalidOperationException("Outer", new ProviderUnreachable());

        Assert.False(ConnectionFailure.Is(exception));
        Assert.True(ConnectionFailure.Is(exception, x => x is ProviderUnreachable));
    }


    private static ApiException Api(int status)
        => new("The HTTP status code of the response was not expected.", status, null, new Dictionary<string, IEnumerable<string>>(), null);

    private sealed class ProviderUnreachable : Exception;
}
