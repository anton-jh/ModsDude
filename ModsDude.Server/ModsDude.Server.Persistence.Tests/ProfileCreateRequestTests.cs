using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The request ID a profile is created with, which is what lets a repeated create find the profile it
/// made rather than making a second one.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ProfileCreateRequestTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_profile_is_found_by_the_request_that_created_it()
    {
        var repoId = await GivenARepo();
        var requestId = new ProfileCreateRequestId(Guid.NewGuid());
        var created = await GivenAProfile(repoId, requestId);

        using var dbContext = fixture.CreateDbContext();

        var found = await dbContext.Profiles.GetByCreateRequestIdAsync(repoId, requestId, CancellationToken.None);

        Assert.Equal(created, found?.Id);
    }

    /// <summary>What a concurrent repeat of the same create runs into after both passed the lookup.</summary>
    [Fact]
    public async Task The_database_refuses_a_second_profile_for_the_same_request_in_a_repo()
    {
        var repoId = await GivenARepo();
        var requestId = new ProfileCreateRequestId(Guid.NewGuid());
        await GivenAProfile(repoId, requestId);

        await Assert.ThrowsAsync<DbUpdateException>(() => GivenAProfile(repoId, requestId));
    }

    [Fact]
    public async Task Another_repo_may_use_the_same_request_id()
    {
        var requestId = new ProfileCreateRequestId(Guid.NewGuid());
        await GivenAProfile(await GivenARepo(), requestId);

        await GivenAProfile(await GivenARepo(), requestId);
    }


    private async Task<RepoId> GivenARepo()
    {
        using var dbContext = fixture.CreateDbContext();

        var user = new User(new UserId($"user-{Guid.NewGuid()}"), new DisplayName("user"), DateTime.UtcNow);
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, user)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(user);
        dbContext.Repos.Add(repo);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }

    private async Task<ProfileId> GivenAProfile(RepoId repoId, ProfileCreateRequestId requestId)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = new Profile(repoId, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow, requestId);

        dbContext.Profiles.Add(profile);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return profile.Id;
    }
}
