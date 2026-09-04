using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The one rule that makes a checkout mean anything: at most one open claim per savegame, enforced
/// by a filtered unique index rather than by whoever wrote the endpoint.
/// </summary>
/// <remarks>
/// There is no <c>Checkout</c> field on <see cref="Savegame"/> to disagree with the log, so "the
/// current holder is the open row" is the whole model - and it is only true while the database
/// refuses the second open row. Two people taking a save in the same second is not a rare case; it
/// is the case the feature exists for. A filter clause is also the part of an index a migration can
/// quietly drop, and nothing but a real PostgreSQL will notice.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class SavegameCheckoutQueryTests(DatabaseFixture fixture)
{
    private static readonly DateTime _takenAt = new(2026, 3, 1, 18, 0, 0, DateTimeKind.Utc);
    private static readonly UserId _author = new("author");
    private static readonly UserId _otherHolder = new("other-holder");


    /// <summary>
    /// The most important test in the suite for this aggregate. Both writers read no open claim,
    /// both insert one, and exactly one of them commits - which is the only reason an endpoint may
    /// treat the open row as the holder instead of taking a lock over the savegame.
    /// </summary>
    [Fact]
    public async Task Two_open_claims_on_one_savegame_are_refused_by_the_database()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenAnOpenCheckout(repoId, savegameId, _author, _takenAt);

        using var dbContext = fixture.CreateDbContext();

        dbContext.SavegameCheckouts.Add(new SavegameCheckout(repoId, savegameId, _otherHolder, _takenAt.AddMinutes(1)));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
    }

    /// <summary>
    /// The other half of the same rule. The index has to constrain open rows only, or a savegame
    /// could be checked out exactly once in its life.
    /// </summary>
    [Fact]
    public async Task An_ended_claim_does_not_stand_in_the_way_of_a_new_one()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        var first = await GivenAnOpenCheckout(repoId, savegameId, _author, _takenAt);

        await GivenTheCheckoutEnds(first, _takenAt.AddHours(2), SavegameCheckoutEndReason.CheckedIn);
        await GivenAnOpenCheckout(repoId, savegameId, _otherHolder, _takenAt.AddHours(3));

        using var dbContext = fixture.CreateDbContext();

        Assert.Equal(2, await dbContext.SavegameCheckouts.CountCheckoutsAsync(repoId, savegameId, CancellationToken.None));
    }

    /// <summary>
    /// A savegame played every evening accumulates one ended claim per evening, and the log is never
    /// pruned with the versions. Nothing about the index may make that history cost anything.
    /// </summary>
    [Fact]
    public async Task A_savegame_may_carry_any_number_of_ended_claims()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        foreach (var evening in Enumerable.Range(0, 5))
        {
            var checkout = await GivenAnOpenCheckout(repoId, savegameId, _author, _takenAt.AddDays(evening));

            await GivenTheCheckoutEnds(checkout, _takenAt.AddDays(evening).AddHours(2), SavegameCheckoutEndReason.CheckedIn);
        }

        using var dbContext = fixture.CreateDbContext();

        Assert.Equal(5, await dbContext.SavegameCheckouts.CountCheckoutsAsync(repoId, savegameId, CancellationToken.None));
        Assert.Null(await dbContext.SavegameCheckouts.GetOpenCheckoutAsync(repoId, savegameId, CancellationToken.None));
    }

    /// <summary>
    /// The index is keyed by savegame, not by repo. Getting that wrong would mean one person playing
    /// locks the whole repo, which is a mistake nobody would find until two people tried at once.
    /// </summary>
    [Fact]
    public async Task Two_savegames_in_one_repo_may_each_be_held_at_the_same_time()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var first = await GivenASavegame(repoId, profileId);
        var second = await GivenASavegame(repoId, profileId);

        await GivenAnOpenCheckout(repoId, first, _author, _takenAt);
        await GivenAnOpenCheckout(repoId, second, _otherHolder, _takenAt);

        using var dbContext = fixture.CreateDbContext();

        var held = await dbContext.SavegameCheckouts.GetOpenCheckoutsAsync(repoId, CancellationToken.None);

        Assert.Equal(
            new[] { first, second }.OrderBy(x => x.Value),
            held.Select(x => x.SavegameId).OrderBy(x => x.Value));
    }

    /// <summary>
    /// The predicate tests the <c>EndedAt</c> column rather than <see cref="SavegameCheckout.IsOpen"/>,
    /// which is computed and has nothing for a provider to translate. A version of this query that
    /// compiled but read the newest row instead would answer correctly right up until somebody was
    /// taken over.
    /// </summary>
    [Fact]
    public async Task The_open_claim_is_the_row_that_has_not_ended_rather_than_the_newest_one()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        var ended = await GivenAnOpenCheckout(repoId, savegameId, _author, _takenAt);

        await GivenTheCheckoutEnds(ended, _takenAt.AddHours(2), SavegameCheckoutEndReason.TakenOver);

        var open = await GivenAnOpenCheckout(repoId, savegameId, _otherHolder, _takenAt.AddHours(2));

        using var dbContext = fixture.CreateDbContext();

        var found = await dbContext.SavegameCheckouts.GetOpenCheckoutAsync(repoId, savegameId, CancellationToken.None);

        Assert.Equal(open, found!.Id);
        Assert.Equal(_otherHolder, found.UserId);
    }

    [Fact]
    public async Task A_savegame_nobody_has_ever_taken_reads_as_unheld_rather_than_throwing()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        using var dbContext = fixture.CreateDbContext();

        Assert.Null(await dbContext.SavegameCheckouts.GetOpenCheckoutAsync(repoId, savegameId, CancellationToken.None));
        Assert.Empty(await dbContext.SavegameCheckouts.GetOpenCheckoutsAsync(repoId, CancellationToken.None));
    }

    /// <summary>
    /// A claim taken on Friday is still the open row on Monday. Expiry is not an end reason - nothing
    /// runs to close it - so the query must not quietly filter on it and report the savegame free.
    /// </summary>
    [Fact]
    public async Task An_expired_claim_is_still_the_open_row()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenAnOpenCheckout(repoId, savegameId, _author, _takenAt);

        using var dbContext = fixture.CreateDbContext();

        var found = await dbContext.SavegameCheckouts.GetOpenCheckoutAsync(repoId, savegameId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(SavegameCheckoutStatus.Stale, found.GetStatus(_takenAt + SavegameCheckout.Lifetime + TimeSpan.FromHours(1)));
        Assert.Equal(SavegameCheckoutStatus.Held, found.GetStatus(_takenAt.AddHours(1)));
    }

    /// <summary>
    /// The end reason is stored through a string conversion, so a value that never survives the round
    /// trip would be a silently wrong word in somebody's history rather than a failure.
    /// </summary>
    [Fact]
    public async Task An_ended_claim_reads_back_with_the_reason_it_ended_for()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        var checkout = await GivenAnOpenCheckout(repoId, savegameId, _author, _takenAt);

        await GivenTheCheckoutEnds(checkout, _takenAt.AddHours(2), SavegameCheckoutEndReason.Discarded);

        using var dbContext = fixture.CreateDbContext();

        var history = await dbContext.SavegameCheckouts.GetHistoryAsync(repoId, savegameId, 0, 50, CancellationToken.None);
        var row = Assert.Single(history);

        Assert.Equal(SavegameCheckoutEndReason.Discarded, row.EndedReason);
        Assert.Equal(_takenAt.AddHours(2), row.EndedAt);
        Assert.False(row.IsOpen);
    }


    private async Task<(RepoId RepoId, ProfileId ProfileId)> GivenARepoWithAProfile()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        var profile = new Profile(repo.Id, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);
        var revision = profile.CreateRevision([], [], _author, DateTime.UtcNow, origin: ProfileRevisionOrigin.Created);

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);
        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(revision);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, profile.Id);
    }

    private async Task<SavegameId> GivenASavegame(RepoId repoId, ProfileId profileId)
    {
        using var dbContext = fixture.CreateDbContext();

        var savegame = new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow);

        dbContext.Savegames.Add(savegame);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return savegame.Id;
    }

    private async Task<SavegameCheckoutId> GivenAnOpenCheckout(RepoId repoId, SavegameId savegameId, UserId userId, DateTime takenAt)
    {
        using var dbContext = fixture.CreateDbContext();

        var checkout = new SavegameCheckout(repoId, savegameId, userId, takenAt);

        dbContext.SavegameCheckouts.Add(checkout);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return checkout.Id;
    }

    private async Task GivenTheCheckoutEnds(SavegameCheckoutId checkoutId, DateTime endedAt, SavegameCheckoutEndReason reason)
    {
        using var dbContext = fixture.CreateDbContext();

        var checkout = await dbContext.SavegameCheckouts.SingleAsync(x => x.Id == checkoutId, CancellationToken.None);

        checkout.End(endedAt, reason);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
