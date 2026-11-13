using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Tests.Services;

public class GoneReposTests
{
    private static readonly Guid _repoId = Guid.NewGuid();


    [Fact]
    public void A_repo_missing_from_the_list_while_the_server_answers_is_gone()
    {
        var (gone, known, _) = Create();

        known.Gone.Add(_repoId);

        Assert.True(gone.IsGone(_repoId));
    }

    [Fact]
    public void A_repo_in_the_list_is_not_gone()
    {
        var (gone, _, _) = Create();

        Assert.False(gone.IsGone(_repoId));
    }

    /// <summary>Offline, the list is whatever the last read said, which is no evidence of anything now.</summary>
    [Fact]
    public void Nothing_is_gone_while_the_server_is_not_answering()
    {
        var (gone, known, connection) = Create();

        known.Gone.Add(_repoId);
        connection.ReportUnreachable();

        Assert.False(gone.IsGone(_repoId));

        connection.ReportReachable();

        Assert.True(gone.IsGone(_repoId));
    }


    private static (GoneRepos Gone, FakeKnownRepos Known, ServerConnection Connection) Create()
    {
        var known = new FakeKnownRepos();
        var connection = new ServerConnection();

        return (new GoneRepos(known, connection), known, connection);
    }
}
