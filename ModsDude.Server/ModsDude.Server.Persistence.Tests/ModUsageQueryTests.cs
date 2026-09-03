using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The query behind the Manage page's "Unused" filter. A provider question rather than a model one:
/// it groups an owned collection reached through its owner, projects value-object keys out of the
/// group, and windows the result — none of which an in-memory substitute would answer for
/// PostgreSQL.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ModUsageQueryTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UserId _author = new("author");


    [Fact]
    public async Task A_version_no_profile_pins_is_absent_rather_than_zero()
    {
        // Absence is the encoding, which is what keeps the response proportional to what is used
        // rather than to the size of the catalog.
        var repoId = await GivenARepoWithAMod("A", "1.0.0", "2.0.0");
        await GivenAProfilePinning(repoId, ("A", "1.0.0"));

        using var dbContext = fixture.CreateDbContext();

        var usage = await dbContext.ProfileRevisions.GetModUsageAsync(repoId, 0, 100, CancellationToken.None);

        Assert.Equal(["1.0.0"], usage.Select(x => x.VersionId.Value));
    }

    [Fact]
    public async Task Every_profile_pinning_a_version_is_counted_once()
    {
        var repoId = await GivenARepoWithAMod("A", "1.0.0");
        await GivenAProfilePinning(repoId, ("A", "1.0.0"));
        await GivenAProfilePinning(repoId, ("A", "1.0.0"));

        using var dbContext = fixture.CreateDbContext();

        var usage = await dbContext.ProfileRevisions.GetModUsageAsync(repoId, 0, 100, CancellationToken.None);

        Assert.Equal(2, Assert.Single(usage).ProfileCount);
    }

    /// <summary>
    /// Otherwise a profile that has held a version across ten saves would read as ten profiles, and
    /// the Manage page's "used by" number would grow with the history rather than with the use.
    /// </summary>
    [Fact]
    public async Task A_profile_that_pinned_a_version_in_several_revisions_is_still_counted_once()
    {
        var repoId = await GivenARepoWithAMod("A", "1.0.0", "2.0.0");
        var profileId = await GivenAProfilePinning(repoId, ("A", "1.0.0"));

        await GivenAProfilePinning(repoId, profileId, ("A", "2.0.0"));
        await GivenAProfilePinning(repoId, profileId, ("A", "1.0.0"));

        using var dbContext = fixture.CreateDbContext();

        var usage = await dbContext.ProfileRevisions.GetModUsageAsync(repoId, 0, 100, CancellationToken.None);

        Assert.Equal([1, 1], usage.Select(x => x.ProfileCount));
    }

    /// <summary>
    /// A version the profile has moved off is still pinned by the revision that pinned it, and the
    /// foreign key will not let it go - so reporting it as unused would offer a delete that is
    /// certain to be refused.
    /// </summary>
    [Fact]
    public async Task A_version_only_an_older_revision_pins_is_still_counted()
    {
        var repoId = await GivenARepoWithAMod("A", "1.0.0", "2.0.0");
        var profileId = await GivenAProfilePinning(repoId, ("A", "1.0.0"));

        await GivenAProfilePinning(repoId, profileId, ("A", "2.0.0"));

        using var dbContext = fixture.CreateDbContext();

        var usage = await dbContext.ProfileRevisions.GetModUsageAsync(repoId, 0, 100, CancellationToken.None);

        Assert.Equal(["1.0.0", "2.0.0"], usage.Select(x => x.VersionId.Value));
    }

    [Fact]
    public async Task Usage_is_scoped_to_the_repo_that_was_asked_about()
    {
        var repoId = await GivenARepoWithAMod("A", "1.0.0");
        var otherRepoId = await GivenARepoWithAMod("A", "1.0.0");

        await GivenAProfilePinning(otherRepoId, ("A", "1.0.0"));

        using var dbContext = fixture.CreateDbContext();

        Assert.Empty(await dbContext.ProfileRevisions.GetModUsageAsync(repoId, 0, 100, CancellationToken.None));
    }

    [Fact]
    public async Task The_listing_is_ordered_and_windowed_so_it_can_be_paged()
    {
        var repoId = await GivenARepoWithAMod("A", "1.0.0", "2.0.0");
        await GivenAMod(repoId, "B", "1.0.0");

        await GivenAProfilePinning(repoId, ("A", "1.0.0"), ("B", "1.0.0"));
        await GivenAProfilePinning(repoId, ("A", "2.0.0"));

        using var dbContext = fixture.CreateDbContext();

        var first = await dbContext.ProfileRevisions.GetModUsageAsync(repoId, 0, 2, CancellationToken.None);
        var second = await dbContext.ProfileRevisions.GetModUsageAsync(repoId, 2, 2, CancellationToken.None);

        Assert.Equal([("A", "1.0.0"), ("A", "2.0.0")], first.Select(x => (x.ModId.Value, x.VersionId.Value)));
        Assert.Equal([("B", "1.0.0")], second.Select(x => (x.ModId.Value, x.VersionId.Value)));
    }


    private async Task<RepoId> GivenARepoWithAMod(string modId, params string[] versionIds)
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);
        dbContext.ModVersions.AddRange(versionIds.Select((versionId, index) => CreateVersion(repo.Id, modId, versionId, index)));

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }

    private async Task GivenAMod(RepoId repoId, string modId, params string[] versionIds)
    {
        using var dbContext = fixture.CreateDbContext();

        dbContext.ModVersions.AddRange(versionIds.Select((versionId, index) => CreateVersion(repoId, modId, versionId, index)));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private Task<ProfileId> GivenAProfilePinning(RepoId repoId, params (string ModId, string VersionId)[] pinned)
        => GivenAProfilePinning(repoId, null, pinned);

    /// <summary>
    /// <paramref name="existing"/> saves another revision of a profile that is already there, which
    /// is what separates "two profiles pin this" from "one profile pinned it twice".
    /// </summary>
    private async Task<ProfileId> GivenAProfilePinning(RepoId repoId, ProfileId? existing, params (string ModId, string VersionId)[] pinned)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = existing is ProfileId profileId
            ? (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!
            : new Profile(repoId, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);

        var dependencies = new List<ModDependency>();

        foreach (var (modId, versionId) in pinned)
        {
            var version = await dbContext.ModVersions.GetAsync(repoId, new ModId(modId), new ModVersionId(versionId), CancellationToken.None);

            dependencies.Add(new ModDependency { ModVersion = version!, Locked = false });
        }

        var previous = existing is null
            ? []
            : await dbContext.ProfileRevisions.GetPinsAsync(repoId, profile.Id, profile.HeadRevision, CancellationToken.None);

        var revision = profile.CreateRevision(dependencies, previous, _author, DateTime.UtcNow);

        if (existing is null)
        {
            dbContext.Profiles.Add(profile);
        }

        dbContext.ProfileRevisions.Add(revision);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return profile.Id;
    }

    private static ModVersion CreateVersion(RepoId repoId, string modId, string versionId, int sequenceNumber) => new()
    {
        RepoId = repoId,
        ModId = new ModId(modId),
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
