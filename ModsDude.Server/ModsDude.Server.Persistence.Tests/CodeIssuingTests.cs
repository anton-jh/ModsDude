using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Invites;

namespace ModsDude.Server.Persistence.Tests;

[Collection(nameof(DatabaseCollection))]
public class CodeIssuingTests(DatabaseFixture fixture)
{
    private static readonly DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);


    [Fact]
    public async Task The_same_request_issued_twice_at_once_saves_one_invite()
    {
        var repoId = await GivenARepo();
        var request = new RepoInviteRequestId(Guid.NewGuid());

        using var firstContext = fixture.CreateDbContext();
        using var secondContext = fixture.CreateDbContext();

        var first = await Issue(firstContext, repoId, request);
        var second = await Issue(secondContext, repoId, request);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Code, second.Code);
        Assert.Equal(1, await CountInvites(repoId));
    }

    [Fact]
    public async Task The_same_request_issued_in_parallel_saves_one_invite()
    {
        var repoId = await GivenARepo();
        var request = new RepoInviteRequestId(Guid.NewGuid());

        var contexts = Enumerable.Range(0, 4).Select(_ => fixture.CreateDbContext()).ToList();
        try
        {
            var invites = await Task.WhenAll(contexts.Select(x => Issue(x, repoId, request)));

            Assert.Single(invites.Select(x => x.Id).Distinct());
            Assert.Equal(1, await CountInvites(repoId));
        }
        finally
        {
            contexts.ForEach(x => x.Dispose());
        }
    }

    [Fact]
    public async Task A_request_id_used_in_another_repo_issues_a_separate_invite()
    {
        var firstRepo = await GivenARepo();
        var secondRepo = await GivenARepo();
        var request = new RepoInviteRequestId(Guid.NewGuid());

        using var dbContext = fixture.CreateDbContext();

        var first = await Issue(dbContext, firstRepo, request);
        var second = await Issue(dbContext, secondRepo, request);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(secondRepo, second.RepoId);
    }

    [Fact]
    public async Task A_code_that_is_taken_is_replaced_by_a_new_one()
    {
        var repoId = await GivenARepo();
        var taken = (await GivenAnInvite(repoId)).Code;
        var attempts = 0;

        using var dbContext = fixture.CreateDbContext();
        var request = new RepoInviteRequestId(Guid.NewGuid());

        var invite = await dbContext.IssueAsync(
            code => NewInvite(repoId, ++attempts == 1 ? taken : code, request),
            ct => dbContext.RepoInvites.GetByRequestIdAsync(repoId, request, ct),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.NotEqual(taken, invite.Code);
        Assert.Equal(2, await CountInvites(repoId));
    }

    [Fact]
    public async Task A_code_that_keeps_colliding_gives_up()
    {
        var repoId = await GivenARepo();
        var taken = (await GivenAnInvite(repoId)).Code;

        using var dbContext = fixture.CreateDbContext();
        var request = new RepoInviteRequestId(Guid.NewGuid());

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.IssueAsync(
            _ => NewInvite(repoId, taken, request),
            ct => dbContext.RepoInvites.GetByRequestIdAsync(repoId, request, ct),
            NullLogger.Instance,
            CancellationToken.None));

        Assert.Equal(1, await CountInvites(repoId));
    }


    private static Task<RepoInvite> Issue(ApplicationDbContext dbContext, RepoId repoId, RepoInviteRequestId request)
    {
        return dbContext.IssueAsync(
            code => NewInvite(repoId, code, request),
            ct => dbContext.RepoInvites.GetByRequestIdAsync(repoId, request, ct),
            NullLogger.Instance,
            CancellationToken.None);
    }

    private static RepoInvite NewInvite(RepoId repoId, InviteCode code, RepoInviteRequestId request)
    {
        return new RepoInvite(repoId, code, request, RepoMembershipLevel.Member, createdBy: null, _now, expiresAt: null, maximumUses: null);
    }

    private async Task<RepoInvite> GivenAnInvite(RepoId repoId)
    {
        using var dbContext = fixture.CreateDbContext();

        return await Issue(dbContext, repoId, new RepoInviteRequestId(Guid.NewGuid()));
    }

    private async Task<RepoId> GivenARepo()
    {
        using var dbContext = fixture.CreateDbContext();

        var user = new User(new UserId($"user-{Guid.NewGuid()}"), new DisplayName("user"), _now);
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), _now, user)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(user);
        dbContext.Repos.Add(repo);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }

    private async Task<int> CountInvites(RepoId repoId)
    {
        using var dbContext = fixture.CreateDbContext();

        return await dbContext.RepoInvites.CountAsync(x => x.RepoId == repoId);
    }
}
