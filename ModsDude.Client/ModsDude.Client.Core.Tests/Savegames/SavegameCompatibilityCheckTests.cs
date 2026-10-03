using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Savegames;

public class SavegameCompatibilityCheckTests
{
    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _profileId = Guid.NewGuid();
    private static readonly SavegameCompatibilityPolicy _policy = new(1, 1, 5, 50);


    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    public async Task A_save_already_on_the_latest_revision_has_nothing_to_assess(int played, int latest)
    {
        var comparer = new FakeComparer();

        Assert.Null(await Check(comparer).AssessAsync(_repoId, _profileId, played, latest, _policy, CancellationToken.None));
        Assert.Equal(0, comparer.Calls);
    }

    [Fact]
    public async Task A_comparison_is_remembered_per_profile_and_revision_pair()
    {
        var comparer = new FakeComparer { Changes = [Removed()] };
        var check = Check(comparer);

        await check.AssessAsync(_repoId, _profileId, 3, 5, _policy, CancellationToken.None);
        comparer.Changes = [];

        Assert.Equal(5, (await check.AssessAsync(_repoId, _profileId, 3, 5, _policy, CancellationToken.None))!.Score);
        Assert.Equal(0, (await check.AssessAsync(_repoId, _profileId, 3, 6, _policy, CancellationToken.None))!.Score);
        Assert.Equal(2, comparer.Calls);
    }

    /// <summary>A dropped connection is not an answer about two revisions, so it is asked again next time.</summary>
    [Fact]
    public async Task A_failed_read_throws_and_is_not_remembered()
    {
        var comparer = new FakeComparer { Fail = true, Changes = [Removed()] };
        var check = Check(comparer);

        await Assert.ThrowsAsync<ApiException>(
            () => check.AssessAsync(_repoId, _profileId, 3, 5, _policy, CancellationToken.None));

        comparer.Fail = false;

        Assert.Equal(5, (await check.AssessAsync(_repoId, _profileId, 3, 5, _policy, CancellationToken.None))!.Score);
        Assert.Equal(2, comparer.Calls);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var comparer = new FakeComparer { Cancel = true };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Check(comparer).AssessAsync(_repoId, _profileId, 3, 5, _policy, CancellationToken.None));
    }


    private static SavegameCompatibilityCheck Check(FakeComparer comparer) => new(comparer);

    private static ProfileModChange Removed()
        => new(
            new CatalogModVersion(Mod("tractor"), V("1.0"), "Tractor", "", IsLocal: false, IsOnServer: true, Locked: false),
            ProfileModChangeKind.Removed,
            V("1.0"),
            null,
            default,
            default);


    private sealed class FakeComparer : IProfileRevisionComparer
    {
        public IReadOnlyList<ProfileModChange> Changes { get; set; } = [];
        public bool Fail { get; set; }
        public bool Cancel { get; set; }
        public int Calls { get; private set; }

        public Task<ProfileRevisionComparison> CompareRevisions(
            Guid repoId, Guid profileId, int from, int to, CancellationToken cancellationToken)
        {
            Calls++;

            if (Cancel)
            {
                throw new OperationCanceledException();
            }

            if (Fail)
            {
                throw new ApiException("Unreachable", 503, null, new Dictionary<string, IEnumerable<string>>(), null);
            }

            return Task.FromResult(new ProfileRevisionComparison(from, to, Changes));
        }
    }
}
