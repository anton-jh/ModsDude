using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.EntityTypeConfigurations;
using ModsDude.Server.Persistence.Extensions;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using Npgsql;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The endpoints check before they write, and a concurrent request can slip between the check and the
/// commit. What decides then is a constraint, and the endpoints tell the outcomes apart by which one
/// refused - so the names have to be the ones the database really uses.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ConstraintViolationTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ModId _modId = new("fs25_testmod");

    private readonly Dictionary<RepoId, UserId> _admins = [];


    [Fact]
    public async Task Two_registrations_of_one_version_violate_the_primary_key()
    {
        var repoId = await GivenARepo();
        await GivenVersions(repoId, ("1.0.0", 0));

        using var dbContext = fixture.CreateDbContext();
        dbContext.ModVersions.Add(CreateVersion(repoId, "1.0.0", 1));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));

        Assert.True(exception.IsUniqueViolation(ModVersionConstraints.PrimaryKey));
        Assert.False(exception.IsUniqueViolation(ModVersionConstraints.SequenceIndex));
    }

    [Fact]
    public async Task Two_versions_in_one_place_violate_the_sequence_index()
    {
        var repoId = await GivenARepo();
        await GivenVersions(repoId, ("1.0.0", 0));

        using var dbContext = fixture.CreateDbContext();
        dbContext.ModVersions.Add(CreateVersion(repoId, "2.0.0", 0));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));

        Assert.True(exception.IsUniqueViolation(ModVersionConstraints.SequenceIndex));
        Assert.False(exception.IsUniqueViolation(ModVersionConstraints.PrimaryKey));
    }

    [Fact]
    public async Task A_content_hash_that_is_not_lowercase_hex_is_refused_by_the_database()
    {
        var repoId = await GivenARepo();

        using var dbContext = fixture.CreateDbContext();
        var version = CreateVersion(repoId, "1.0.0", 0);
        version.ContentHash = TestHashes.Of("x").ToUpperInvariant();
        dbContext.ModVersions.Add(version);

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_membership_level_that_is_not_defined_is_refused_by_the_database()
    {
        var repoId = await GivenARepo();

        using var dbContext = fixture.CreateDbContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => dbContext.Database.ExecuteSqlAsync(
            $"""UPDATE "RepoMemberships" SET "Level" = '0' WHERE "RepoId" = {repoId.Value}"""));

        Assert.Equal("CK_RepoMemberships_Level", exception.ConstraintName);
    }

    [Fact]
    public async Task A_repo_is_loaded_with_its_members_users_so_a_blocked_admin_is_seen()
    {
        var repoId = await GivenARepo();

        using (var dbContext = fixture.CreateDbContext())
        {
            var repo = (await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!;
            var other = new User(new UserId($"user-{Guid.NewGuid()}"), new DisplayName("Other"), DateTime.UtcNow);
            dbContext.Users.Add(other);
            repo.AddMember(other, RepoMembershipLevel.Admin);
            other.Block(DateTime.UtcNow);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();
        var loaded = (await verification.Repos.GetAsync(repoId, CancellationToken.None))!;

        Assert.True(loaded.IsOnlyAdmin(_admins[repoId]));
    }

    [Fact]
    public async Task A_locked_revision_history_waits_for_the_lock_holder_to_commit()
    {
        var repoId = await GivenARepo();
        var profileId = await GivenAProfile(repoId);

        using var holder = fixture.CreateDbContext();
        using var waiter = fixture.CreateDbContext();

        await using var held = await holder.Database.BeginTransactionAsync();
        await holder.LockRevisionHistoryAsync(repoId, profileId, CancellationToken.None);

        await using var waiting = await waiter.Database.BeginTransactionAsync();
        var second = waiter.LockRevisionHistoryAsync(repoId, profileId, CancellationToken.None);

        await Task.Delay(300);
        Assert.False(second.IsCompleted);

        await held.CommitAsync();
        await second.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Locking_a_revision_history_outside_a_transaction_is_refused()
    {
        var repoId = await GivenARepo();
        var profileId = await GivenAProfile(repoId);

        using var dbContext = fixture.CreateDbContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.LockRevisionHistoryAsync(repoId, profileId, CancellationToken.None));
    }


    private async Task<RepoId> GivenARepo()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var user = new User(userId, new DisplayName("Admin"), DateTime.UtcNow);
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, user)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(user);
        dbContext.Repos.Add(repo);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        _admins[repo.Id] = userId;

        return repo.Id;
    }

    private async Task<ProfileId> GivenAProfile(RepoId repoId)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = new Profile(repoId, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow, new ProfileCreateRequestId(Guid.NewGuid()));
        dbContext.Profiles.Add(profile);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return profile.Id;
    }

    private async Task GivenVersions(RepoId repoId, params (string VersionId, int SequenceNumber)[] versions)
    {
        using var dbContext = fixture.CreateDbContext();

        dbContext.ModVersions.AddRange(versions.Select(x => CreateVersion(repoId, x.VersionId, x.SequenceNumber)));
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
        FileName = $"{_modId.Value}.zip",
        ContentHash = TestHashes.Of(versionId),
        SizeBytes = 1024,
        Locked = false,
        Attributes = [],
        Created = _timestamp,
        Updated = _timestamp
    };
}
