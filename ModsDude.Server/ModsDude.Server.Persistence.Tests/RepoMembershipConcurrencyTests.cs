using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The only-Admin rule is checked in memory, against the memberships a request loaded. These are the
/// races where two requests each pass that check against the same memberships, which only the
/// repo's membership revision can stop.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class RepoMembershipConcurrencyTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Two_admins_demoting_each_other_at_once_leave_one_admin()
    {
        var (repoId, first, second) = await GivenARepoWithTwoAdmins();

        using var firstRequest = fixture.CreateDbContext();
        using var secondRequest = fixture.CreateDbContext();

        var firstView = (await firstRequest.Repos.GetAsync(repoId, CancellationToken.None))!;
        var secondView = (await secondRequest.Repos.GetAsync(repoId, CancellationToken.None))!;

        firstView.UpdateMembershipLevel(first, RepoMembershipLevel.Member);
        secondView.UpdateMembershipLevel(second, RepoMembershipLevel.Member);

        await firstRequest.SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondRequest.SaveChangesAsync(CancellationToken.None));

        using var verification = fixture.CreateDbContext();

        Assert.Equal(1, await verification.RepoMemberships.CountAsync(
            x => x.RepoId == repoId && x.Level == RepoMembershipLevel.Admin, CancellationToken.None));
    }

    [Fact]
    public async Task Two_admins_kicking_each_other_at_once_leave_one_admin()
    {
        var (repoId, first, second) = await GivenARepoWithTwoAdmins();

        using var firstRequest = fixture.CreateDbContext();
        using var secondRequest = fixture.CreateDbContext();

        var firstView = (await firstRequest.Repos.GetAsync(repoId, CancellationToken.None))!;
        var secondView = (await secondRequest.Repos.GetAsync(repoId, CancellationToken.None))!;

        firstView.KickMember(first);
        secondView.KickMember(second);

        await firstRequest.SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondRequest.SaveChangesAsync(CancellationToken.None));

        using var verification = fixture.CreateDbContext();

        Assert.Equal([second], await verification.RepoMemberships
            .Where(x => x.RepoId == repoId)
            .Select(x => x.UserId)
            .ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_revision_is_saved_with_each_change()
    {
        var (repoId, first, _) = await GivenARepoWithTwoAdmins();

        int before;
        using (var dbContext = fixture.CreateDbContext())
        {
            var repo = (await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!;
            before = repo.MembersVersion;

            repo.UpdateMembershipLevel(first, RepoMembershipLevel.Guest);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();

        Assert.NotEqual(before, (await verification.Repos.GetAsync(repoId, CancellationToken.None))!.MembersVersion);
    }


    private async Task<(RepoId RepoId, UserId First, UserId Second)> GivenARepoWithTwoAdmins()
    {
        using var dbContext = fixture.CreateDbContext();

        var first = new User(new UserId($"user-{Guid.NewGuid()}"), new DisplayName("First"), DateTime.UtcNow);
        var second = new User(new UserId($"user-{Guid.NewGuid()}"), new DisplayName("Second"), DateTime.UtcNow);

        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, first)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };
        repo.AddMember(second, RepoMembershipLevel.Admin);

        dbContext.Users.AddRange(first, second);
        dbContext.Repos.Add(repo);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, first.Id, second.Id);
    }
}
