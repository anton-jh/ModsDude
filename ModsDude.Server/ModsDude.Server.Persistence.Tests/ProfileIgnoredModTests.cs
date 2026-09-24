using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// What a profile ignores, and the rules that keeps it apart from what the profile pins. Provider
/// questions: the set is keyed by three columns, the release is a query over a list of ids, and the
/// cascade from a profile is the database's.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ProfileIgnoredModTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UserId _author = new("author");
    private static readonly ModId _pinned = new("FS25_Pinned");
    private static readonly ModId _noise = new("FS25_Noise");
    private static readonly ModId _moreNoise = new("FS25_MoreNoise");


    [Fact]
    public async Task Ignoring_a_mod_the_repo_has_never_registered_is_allowed()
    {
        var (repoId, profileId) = await GivenAProfilePinning();

        await WhenReplacing(repoId, profileId, _noise);

        Assert.Equal([_noise], await ReadIgnored(repoId, profileId));
    }

    /// <summary>The list is replaced, not added to: what the page showed is what is recorded.</summary>
    [Fact]
    public async Task Replacing_the_list_drops_what_it_no_longer_names()
    {
        var (repoId, profileId) = await GivenAProfilePinning();

        await WhenReplacing(repoId, profileId, _noise, _moreNoise);
        await WhenReplacing(repoId, profileId, _moreNoise, new ModId("FS25_Other"));

        Assert.Equal(
            [_moreNoise, new ModId("FS25_Other")],
            (await ReadIgnored(repoId, profileId)).OrderBy(x => x.Value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Writing_the_same_list_twice_changes_nothing()
    {
        var (repoId, profileId) = await GivenAProfilePinning();

        await WhenReplacing(repoId, profileId, _noise, _moreNoise);
        await WhenReplacing(repoId, profileId, _noise, _moreNoise);

        Assert.Equal(2, (await ReadIgnored(repoId, profileId)).Count);
    }

    [Fact]
    public async Task Replacing_with_nothing_clears_the_list()
    {
        var (repoId, profileId) = await GivenAProfilePinning();

        await WhenReplacing(repoId, profileId, _noise);
        await WhenReplacing(repoId, profileId);

        Assert.Empty(await ReadIgnored(repoId, profileId));
    }

    [Fact]
    public async Task Two_profiles_ignore_independently()
    {
        var (repoId, profileId) = await GivenAProfilePinning();
        var other = await GivenAnotherProfileInRepo(repoId);

        await WhenReplacing(repoId, profileId, _noise);

        Assert.Empty(await ReadIgnored(repoId, other));
    }

    [Fact]
    public async Task What_the_head_pins_is_found_when_it_is_asked_to_be_ignored()
    {
        var (repoId, profileId) = await GivenAProfilePinning();

        using var dbContext = fixture.CreateDbContext();

        var profile = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;
        var overlap = await dbContext.FindPinnedAsync(profile, [_pinned, _noise], CancellationToken.None);

        Assert.Equal([_pinned], overlap);
    }

    /// <summary>
    /// Only the head counts. A mod an older revision pinned is exactly the sort of thing somebody
    /// wants to ignore, so it must not be refused for having once been in the profile.
    /// </summary>
    [Fact]
    public async Task A_mod_only_an_older_revision_pinned_may_be_ignored()
    {
        var (repoId, profileId) = await GivenAProfilePinning();

        await GivenARevisionPinning(repoId, profileId, _noise);

        using var dbContext = fixture.CreateDbContext();

        var profile = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;

        Assert.Empty(await dbContext.FindPinnedAsync(profile, [_pinned], CancellationToken.None));
    }

    /// <summary>
    /// The other half of the rule. A save writes a revision without mentioning the ignore list, and
    /// what it newly pins has to stop being ignored - or a mod ignored yesterday and pinned today
    /// would be both.
    /// </summary>
    [Fact]
    public async Task Pinning_an_ignored_mod_in_a_revision_stops_ignoring_it()
    {
        var (repoId, profileId) = await GivenAProfilePinning();

        await WhenReplacing(repoId, profileId, _noise, _moreNoise);
        await GivenARevisionPinning(repoId, profileId, _pinned, _noise);

        Assert.Equal([_moreNoise], await ReadIgnored(repoId, profileId));
    }

    /// <summary>
    /// A mod that is removed and imported again must not come back hidden, in any profile of the repo
    /// - and another repo's mod of the same id is not this one.
    /// </summary>
    [Fact]
    public async Task Removing_a_mod_takes_it_off_every_ignore_list_in_the_repo()
    {
        var (repoId, profileId) = await GivenAProfilePinning();
        var other = await GivenAnotherProfileInRepo(repoId);
        var (elsewhere, elsewhereProfile) = await GivenAProfilePinning();

        await WhenReplacing(repoId, profileId, _noise, _moreNoise);
        await WhenReplacing(repoId, other, _noise);
        await WhenReplacing(elsewhere, elsewhereProfile, _noise);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.ReleaseModAsync(repoId, _noise, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal([_moreNoise], await ReadIgnored(repoId, profileId));
        Assert.Empty(await ReadIgnored(repoId, other));
        Assert.Equal([_noise], await ReadIgnored(elsewhere, elsewhereProfile));
    }

    [Fact]
    public async Task Deleting_a_profile_takes_what_it_ignored()
    {
        var (repoId, profileId) = await GivenAProfilePinning();

        await WhenReplacing(repoId, profileId, _noise);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.ProfileRevisions.Where(x => x.RepoId == repoId && x.ProfileId == profileId).ExecuteDeleteAsync();
            await dbContext.Profiles.Where(x => x.RepoId == repoId && x.Id == profileId).ExecuteDeleteAsync();
        }

        using var verification = fixture.CreateDbContext();

        Assert.Equal(0, await verification.ProfileIgnoredMods.CountAsync(x => x.RepoId == repoId));
    }


    private async Task WhenReplacing(RepoId repoId, ProfileId profileId, params ModId[] modIds)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;

        await dbContext.ReplaceIgnoredAsync(profile, modIds, CancellationToken.None);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<HashSet<ModId>> ReadIgnored(RepoId repoId, ProfileId profileId)
    {
        using var dbContext = fixture.CreateDbContext();

        return await dbContext.ProfileIgnoredMods.GetModIdsAsync(repoId, profileId, CancellationToken.None);
    }

    private async Task<(RepoId RepoId, ProfileId ProfileId)> GivenAProfilePinning()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);
        var pinnedVersion = CreateVersion(repo.Id, _pinned);

        dbContext.ModVersions.AddRange(pinnedVersion, CreateVersion(repo.Id, _noise));

        var profile = new Profile(repo.Id, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);

        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(profile.CreateRevision(
            [new ModDependency { ModVersion = pinnedVersion, Locked = false }],
            [],
            _author,
            DateTime.UtcNow,
            origin: ProfileRevisionOrigin.Created));

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, profile.Id);
    }

    private async Task<ProfileId> GivenAnotherProfileInRepo(RepoId repoId)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = new Profile(repoId, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);

        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(profile.CreateRevision([], [], _author, DateTime.UtcNow, origin: ProfileRevisionOrigin.Created));

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return profile.Id;
    }

    /// <summary>The write a save makes: the new revision, and the release of whatever it pins.</summary>
    private async Task GivenARevisionPinning(RepoId repoId, ProfileId profileId, params ModId[] modIds)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;
        var previous = await dbContext.ProfileRevisions.GetPinsAsync(repoId, profileId, profile.HeadRevision, CancellationToken.None);

        var dependencies = new List<ModDependency>();

        foreach (var modId in modIds)
        {
            var version = await dbContext.ModVersions.GetAsync(repoId, modId, new ModVersionId("1.0.0"), CancellationToken.None);

            dependencies.Add(new ModDependency { ModVersion = version!, Locked = false });
        }

        var revision = profile.CreateRevision(dependencies, previous, _author, DateTime.UtcNow);
        var pins = dependencies.Select(x => x.ToPin()).ToList();

        dbContext.ProfileRevisions.Add(revision);
        await dbContext.ReleasePinnedAsync(profile, pins, CancellationToken.None);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ModVersion CreateVersion(RepoId repoId, ModId modId) => new()
    {
        RepoId = repoId,
        ModId = modId,
        Id = new ModVersionId("1.0.0"),
        SequenceNumber = 0,
        DisplayName = modId.Value,
        Description = "",
        FileName = $"{modId.Value}.zip",
        ContentHash = modId.Value,
        SizeBytes = 1024,
        Locked = false,
        Attributes = [],
        Created = _timestamp,
        Updated = _timestamp
    };
}
