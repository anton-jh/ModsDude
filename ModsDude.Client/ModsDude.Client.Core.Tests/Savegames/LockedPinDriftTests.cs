using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Savegames;

public class LockedPinDriftTests
{
    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _profileId = Guid.NewGuid();


    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task A_locked_pin_that_moved_version_is_drift(bool fromLocked, bool toLocked, bool versionLocked)
    {
        var comparer = new FakeComparer { Changes = [Moved(fromLocked, toLocked, versionLocked)] };

        Assert.True(await Drift(comparer).HasMovedAsync(_repoId, _profileId, 3, 5, CancellationToken.None));
    }

    [Fact]
    public async Task An_unlocked_pin_that_moved_version_is_not_drift()
    {
        var comparer = new FakeComparer { Changes = [Moved(false, false, false)] };

        Assert.False(await Drift(comparer).HasMovedAsync(_repoId, _profileId, 3, 5, CancellationToken.None));
    }

    [Fact]
    public async Task A_lock_toggled_without_a_version_move_is_not_drift()
    {
        var change = new ProfileModChange(Version(locked: false), ProfileModChangeKind.Changed, V("1.0"), V("1.0"), false, true);
        var comparer = new FakeComparer { Changes = [change] };

        Assert.False(await Drift(comparer).HasMovedAsync(_repoId, _profileId, 3, 5, CancellationToken.None));
    }

    [Fact]
    public async Task An_answer_is_remembered_per_profile_and_revision_pair()
    {
        var comparer = new FakeComparer { Changes = [Moved(true, true, false)] };
        var drift = Drift(comparer);

        await drift.HasMovedAsync(_repoId, _profileId, 3, 5, CancellationToken.None);
        comparer.Changes = [];

        Assert.True(await drift.HasMovedAsync(_repoId, _profileId, 3, 5, CancellationToken.None));
        Assert.False(await drift.HasMovedAsync(_repoId, _profileId, 3, 6, CancellationToken.None));
        Assert.Equal(2, comparer.Calls);
    }

    [Fact]
    public async Task A_failed_read_answers_no_and_is_asked_again_next_time()
    {
        var comparer = new FakeComparer { Fail = true, Changes = [Moved(true, true, false)] };
        var drift = Drift(comparer);

        Assert.False(await drift.HasMovedAsync(_repoId, _profileId, 3, 5, CancellationToken.None));

        comparer.Fail = false;

        Assert.True(await drift.HasMovedAsync(_repoId, _profileId, 3, 5, CancellationToken.None));
        Assert.Equal(2, comparer.Calls);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var comparer = new FakeComparer { Cancel = true };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Drift(comparer).HasMovedAsync(_repoId, _profileId, 3, 5, CancellationToken.None));
    }


    private static LockedPinDrift Drift(FakeComparer comparer) => new(comparer, NullLogger<LockedPinDrift>.Instance);

    private static CatalogModVersion Version(bool locked)
        => new(Mod("map"), V("2.0"), "Map", "", IsLocal: false, IsOnServer: true, Locked: locked);

    private static ProfileModChange Moved(bool fromLocked, bool toLocked, bool versionLocked)
        => new(Version(versionLocked), ProfileModChangeKind.Changed, V("1.0"), V("2.0"), fromLocked, toLocked);


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
