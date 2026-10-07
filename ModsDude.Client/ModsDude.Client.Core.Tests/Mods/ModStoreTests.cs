using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Mods;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Tests.Stores;

namespace ModsDude.Client.Core.Tests.Mods;

public class ModStoreTests
{
    private static readonly Guid _repoId = Guid.NewGuid();


    [Fact]
    public async Task The_first_read_holds_every_version()
    {
        var harness = new Harness();
        harness.Server.Register("b_mod", "1.0");
        harness.Server.Register("a_mod", "2.0");
        harness.Server.Register("a_mod", "1.0");

        var versions = await harness.Store.GetAsync(_repoId, CancellationToken.None);

        Assert.Equal([("a_mod", "1.0"), ("a_mod", "2.0"), ("b_mod", "1.0")], Ids(versions));
        Assert.Equal([0L], harness.Server.ReadsAfter);
    }

    [Fact]
    public async Task A_later_read_asks_only_for_what_changed_and_folds_it_in()
    {
        var harness = new Harness();
        harness.Server.Register("a_mod", "1.0");
        harness.Server.Register("b_mod", "1.0");
        await harness.Store.GetAsync(_repoId, CancellationToken.None);

        harness.Server.Register("c_mod", "1.0");
        harness.Server.Delete("a_mod", "1.0");
        harness.Server.Rename("b_mod", "1.0", "Renamed");

        var versions = await harness.Store.GetAsync(_repoId, CancellationToken.None);

        Assert.Equal([("b_mod", "1.0"), ("c_mod", "1.0")], Ids(versions));
        Assert.Equal("Renamed", versions[0].DisplayName);
        Assert.Equal([0L, 2L], harness.Server.ReadsAfter);
    }

    [Fact]
    public async Task A_version_deleted_and_registered_again_since_the_last_read_is_held()
    {
        var harness = new Harness();
        harness.Server.Register("a_mod", "1.0");
        await harness.Store.GetAsync(_repoId, CancellationToken.None);

        harness.Server.Delete("a_mod", "1.0");
        harness.Server.Register("a_mod", "1.0");

        Assert.Equal([("a_mod", "1.0")], Ids(await harness.Store.GetAsync(_repoId, CancellationToken.None)));
    }

    [Fact]
    public async Task A_long_list_is_read_page_by_page_to_the_end()
    {
        var harness = new Harness(pageSize: 2);

        foreach (var number in Enumerable.Range(1, 5))
        {
            harness.Server.Register("a_mod", $"{number}.0");
        }

        var versions = await harness.Store.GetAsync(_repoId, CancellationToken.None);

        Assert.Equal(5, versions.Count);
        Assert.Equal([0L, 2L, 4L], harness.Server.ReadsAfter);
    }

    [Fact]
    public async Task Callers_arriving_together_share_one_read()
    {
        var harness = new Harness();
        harness.Server.Register("a_mod", "1.0");
        var gate = harness.Server.Hold();

        var first = harness.Store.GetAsync(_repoId, CancellationToken.None);
        var second = harness.Store.GetAsync(_repoId, CancellationToken.None);
        gate.SetResult();

        Assert.Same(await first, await second);
        Assert.Single(harness.Server.ReadsAfter);
    }

    [Fact]
    public async Task A_user_change_forgets_everything_and_the_next_read_starts_over()
    {
        var harness = new Harness();
        harness.Server.Register("a_mod", "1.0");
        await harness.Store.GetAsync(_repoId, CancellationToken.None);

        harness.Store.ClearUserState();
        await harness.Store.GetAsync(_repoId, CancellationToken.None);

        Assert.Equal([0L, 0L], harness.Server.ReadsAfter);
    }

    [Fact]
    public async Task A_read_still_out_when_the_user_changes_is_dropped()
    {
        var harness = new Harness();
        harness.Server.Register("a_mod", "1.0");
        var gate = harness.Server.Hold();

        var read = harness.Store.GetAsync(_repoId, CancellationToken.None);
        harness.Store.ClearUserState();
        gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }


    private static List<(string, string)> Ids(IEnumerable<ModDto> versions)
        => [.. versions.Select(x => (x.ModId, x.VersionId))];


    private sealed class Harness
    {
        public Harness(int pageSize = 100)
        {
            Server = new FakeModFeed(pageSize);
            Store = new ModStore(Server, InlineStoreDispatcher.Instance, NullLogger<ModStore>.Instance);
        }

        public FakeModFeed Server { get; }
        public ModStore Store { get; }
    }

    /// <summary>The server's feed of changes: every change takes the next number, deletions included.</summary>
    private sealed class FakeModFeed(int pageSize) : IModsClient
    {
        private readonly List<(long Sequence, ModDto Version)> _versions = [];
        private readonly List<(long Sequence, ModVersionRefDto Version)> _deleted = [];
        private TaskCompletionSource? _held;
        private long _sequence;

        public List<long> ReadsAfter { get; } = [];

        public TaskCompletionSource Hold()
            => _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Register(string modId, string versionId)
            => _versions.Add((++_sequence, new ModDto { ModId = modId, VersionId = versionId, DisplayName = modId }));

        public void Rename(string modId, string versionId, string name)
        {
            var index = _versions.FindIndex(x => x.Version.ModId == modId && x.Version.VersionId == versionId);
            _versions[index] = (++_sequence, _versions[index].Version with { DisplayName = name });
        }

        public void Delete(string modId, string versionId)
        {
            _versions.RemoveAll(x => x.Version.ModId == modId && x.Version.VersionId == versionId);
            _deleted.Add((++_sequence, new ModVersionRefDto { ModId = modId, VersionId = versionId }));
        }

        public async Task<GetModsResponse> GetModsV1Async(Guid repoId, long? after = null, int? limit = null, CancellationToken cancellationToken = default)
        {
            var from = after ?? 0;
            ReadsAfter.Add(from);

            var page = _versions.Where(x => x.Sequence > from).OrderBy(x => x.Sequence).Take(pageSize + 1).ToList();
            var hasMore = page.Count > pageSize;

            if (hasMore)
            {
                page.RemoveAt(page.Count - 1);
            }

            var to = hasMore ? page[^1].Sequence : _sequence;

            var response = new GetModsResponse
            {
                Mods = [.. page.Select(x => x.Version with { })],
                Deleted = [.. _deleted.Where(x => x.Sequence > from && x.Sequence <= to).Select(x => x.Version)],
                Sequence = to,
                HasMore = hasMore
            };

            if (_held is { } held)
            {
                _held = null;
                await held.Task.WaitAsync(cancellationToken);
            }

            return response;
        }

        public Task<ModDto> RegisterModV1Async(Guid repoId, RegisterModRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GetModUsageResponse> GetModUsageV1Async(Guid repoId, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteModV1Async(Guid repoId, string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ModDependentsDto> GetModDependentsV1Async(Guid repoId, string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GetModVersionsResponse> GetModVersionsV1Async(Guid repoId, string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteModVersionV1Async(Guid repoId, string modId, string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ModDependentsDto> GetModVersionDependentsV1Async(Guid repoId, string modId, string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ModDto> SetModVersionImagesV1Async(Guid repoId, string modId, string versionId, SetModVersionImagesRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MoveModVersionResponse> MoveModVersionV1Async(Guid repoId, string modId, string versionId, MoveModVersionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
