using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// What a profile's history costs to read and what the database will let it hold. Provider
/// questions: the dependencies are an owned collection of an entity that is itself keyed by three
/// columns, the reads project out of it without materializing a revision, and the rule that one
/// revision pins each mod once is a unique index rather than anything the model can enforce across
/// two contexts.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ProfileRevisionQueryTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UserId _author = new("author");
    private static readonly ModId _modId = new("FS25_TestMod");


    [Fact]
    public async Task A_revision_answers_with_what_it_pinned_rather_than_with_what_the_profile_pins_now()
    {
        var repoId = await GivenARepoWithAMod("1.0.0", "2.0.0");
        var profileId = await GivenAProfilePinning(repoId, "1.0.0");

        await GivenAFurtherRevisionPinning(repoId, profileId, "2.0.0");

        using var dbContext = fixture.CreateDbContext();

        var first = await dbContext.ProfileRevisions.GetPinsAsync(repoId, profileId, new RevisionNumber(1), CancellationToken.None);
        var second = await dbContext.ProfileRevisions.GetPinsAsync(repoId, profileId, new RevisionNumber(2), CancellationToken.None);

        Assert.Equal("1.0.0", Assert.Single(first).VersionId.Value);
        Assert.Equal("2.0.0", Assert.Single(second).VersionId.Value);
    }

    /// <summary>
    /// The unique index is per revision, not per profile. Without that, a profile could never pin a
    /// mod at a version any of its earlier revisions already used - which is what every rollback is.
    /// </summary>
    [Fact]
    public async Task Two_revisions_of_one_profile_may_pin_the_same_mod_at_the_same_version()
    {
        var repoId = await GivenARepoWithAMod("1.0.0", "2.0.0");
        var profileId = await GivenAProfilePinning(repoId, "1.0.0");

        await GivenAFurtherRevisionPinning(repoId, profileId, "2.0.0");
        await GivenAFurtherRevisionPinning(repoId, profileId, "1.0.0");

        using var dbContext = fixture.CreateDbContext();

        var restored = await dbContext.ProfileRevisions.GetPinsAsync(repoId, profileId, new RevisionNumber(3), CancellationToken.None);

        Assert.Equal("1.0.0", Assert.Single(restored).VersionId.Value);
    }

    [Fact]
    public async Task The_head_a_profile_reports_is_the_revision_that_was_last_written()
    {
        var repoId = await GivenARepoWithAMod("1.0.0", "2.0.0");
        var profileId = await GivenAProfilePinning(repoId, "1.0.0");

        await GivenAFurtherRevisionPinning(repoId, profileId, "2.0.0");

        using var dbContext = fixture.CreateDbContext();

        var profile = await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None);

        Assert.Equal(new RevisionNumber(2), profile!.HeadRevision);
    }

    [Fact]
    public async Task A_revision_the_profile_does_not_have_reads_as_absent_rather_than_throwing()
    {
        var repoId = await GivenARepoWithAMod("1.0.0");
        var profileId = await GivenAProfilePinning(repoId, "1.0.0");

        using var dbContext = fixture.CreateDbContext();

        Assert.False(await dbContext.ProfileRevisions.ExistsAsync(repoId, profileId, new RevisionNumber(7), CancellationToken.None));
        Assert.Empty(await dbContext.ProfileRevisions.GetPinsAsync(repoId, profileId, new RevisionNumber(7), CancellationToken.None));
    }

    /// <summary>
    /// Sync reads a profile's dependencies rather than the repo's mod list, so the hash has to come
    /// back with them - through two levels of projection, from a revision into its owned collection
    /// and out to the version it names.
    /// </summary>
    [Fact]
    public async Task Dependency_rows_carry_the_content_hash_of_the_version_they_name()
    {
        var repoId = await GivenARepoWithAMod("1.0.0");
        var profileId = await GivenAProfilePinning(repoId, "1.0.0", locked: true);

        using var dbContext = fixture.CreateDbContext();

        var rows = await dbContext.ProfileRevisions.GetDependencyRowsAsync(repoId, profileId, new RevisionNumber(1), CancellationToken.None);
        var row = Assert.Single(rows);

        Assert.Equal("1.0.0", row.ContentHash);
        Assert.True(row.Locked);
    }

    [Fact]
    public async Task Deleting_a_profile_takes_its_whole_history_with_it()
    {
        var repoId = await GivenARepoWithAMod("1.0.0", "2.0.0");
        var profileId = await GivenAProfilePinning(repoId, "1.0.0");

        await GivenAFurtherRevisionPinning(repoId, profileId, "2.0.0");

        using (var dbContext = fixture.CreateDbContext())
        {
            var profile = await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None);

            dbContext.Profiles.Remove(profile!);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();

        Assert.Empty(await verification.ProfileRevisions
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId)
            .Select(x => x.Number)
            .ToListAsync(CancellationToken.None));
    }


    private async Task<RepoId> GivenARepoWithAMod(params string[] versionIds)
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);
        dbContext.ModVersions.AddRange(versionIds.Select((versionId, index) => CreateVersion(repo.Id, versionId, index)));

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }

    private async Task<ProfileId> GivenAProfilePinning(RepoId repoId, string versionId, bool locked = false)
    {
        using var dbContext = fixture.CreateDbContext();

        var version = await dbContext.ModVersions.GetAsync(repoId, _modId, new ModVersionId(versionId), CancellationToken.None);
        var profile = new Profile(repoId, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);

        var revision = profile.CreateRevision(
            [new ModDependency { ModVersion = version!, Locked = locked }],
            [],
            _author,
            DateTime.UtcNow,
            origin: ProfileRevisionOrigin.Created);

        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(revision);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return profile.Id;
    }

    private async Task GivenAFurtherRevisionPinning(RepoId repoId, ProfileId profileId, string versionId)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;
        var version = await dbContext.ModVersions.GetAsync(repoId, _modId, new ModVersionId(versionId), CancellationToken.None);
        var previous = await dbContext.ProfileRevisions.GetPinsAsync(repoId, profileId, profile.HeadRevision, CancellationToken.None);

        var revision = profile.CreateRevision(
            [new ModDependency { ModVersion = version!, Locked = false }],
            previous,
            _author,
            DateTime.UtcNow);

        dbContext.ProfileRevisions.Add(revision);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ModVersion CreateVersion(RepoId repoId, string versionId, int sequenceNumber) => new()
    {
        RepoId = repoId,
        ModId = _modId,
        Id = new ModVersionId(versionId),
        SequenceNumber = sequenceNumber,
        DisplayName = versionId,
        Description = "",
        ContentHash = versionId,
        Locked = false,
        Attributes = [],
        Created = _timestamp,
        Updated = _timestamp
    };
}
