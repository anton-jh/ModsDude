using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The record of which request created a repo, which is what lets a repeated create find the repo it
/// made rather than making a second one.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class RepoCreateRequestTests(DatabaseFixture fixture)
{
    /// <summary>What a concurrent repeat of the same create runs into after both passed the lookup.</summary>
    [Fact]
    public async Task The_database_refuses_a_second_repo_for_the_same_request_of_a_person()
    {
        var user = await GivenAUser();
        var requestId = new RepoCreateRequestId(Guid.NewGuid());
        await GivenARepo(user, requestId);

        await Assert.ThrowsAsync<DbUpdateException>(() => GivenARepo(user, requestId));
    }

    [Fact]
    public async Task Somebody_else_may_use_the_same_request_id()
    {
        var requestId = new RepoCreateRequestId(Guid.NewGuid());
        await GivenARepo(await GivenAUser(), requestId);

        await GivenARepo(await GivenAUser(), requestId);
    }

    [Fact]
    public async Task Deleting_the_repo_takes_its_request_with_it()
    {
        var user = await GivenAUser();
        var requestId = new RepoCreateRequestId(Guid.NewGuid());
        var repoId = await GivenARepo(user, requestId);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.DeleteWithContentsAsync((await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!, CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();

        Assert.False(await verification.RepoCreateRequests.AnyAsync(x => x.UserId == user && x.RequestId == requestId, CancellationToken.None));
    }


    private async Task<UserId> GivenAUser()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return userId;
    }

    private async Task<RepoId> GivenARepo(UserId userId, RepoCreateRequestId requestId)
    {
        using var dbContext = fixture.CreateDbContext();

        var user = (await dbContext.Users.GetAsync(userId, CancellationToken.None))!;
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, user)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Repos.Add(repo);
        dbContext.RepoCreateRequests.Add(new RepoCreateRequest(userId, requestId, repo.Id, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }
}
