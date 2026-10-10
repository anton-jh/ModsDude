using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Tests.Stores;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ProfileStoreTests
{
    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _otherRepoId = Guid.NewGuid();


    [Fact]
    public async Task A_read_fills_the_repos_list_and_its_profiles_are_found_by_repo_and_id()
    {
        var harness = new Harness();
        var season = harness.Add(_repoId, "Season 4", 3);

        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);

        Assert.True(harness.Store.IsLoaded(_repoId));
        Assert.Equal("Season 4", Assert.Single(harness.Store.Live(_repoId)).Name);
        Assert.Equal(3, harness.Store.Find(_repoId, season.Id)?.HeadRevision);
        Assert.Null(harness.Store.Find(_otherRepoId, season.Id));
        Assert.Null(harness.Store.Find(_repoId, null));
    }

    [Fact]
    public async Task Two_repos_are_held_side_by_side()
    {
        var harness = new Harness();
        var mine = harness.Add(_repoId, "Mine", 1);
        var theirs = harness.Add(_otherRepoId, "Theirs", 7);

        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        await harness.Store.RefreshAsync(_otherRepoId, CancellationToken.None);

        Assert.Equal(1, harness.Store.GetHeadRevision(new ActiveProfile(_repoId, mine.Id)));
        Assert.Equal(7, harness.Store.GetHeadRevision(new ActiveProfile(_otherRepoId, theirs.Id)));
        Assert.Equal(new[] { _repoId, _otherRepoId }.Order(), harness.Store.LoadedRepos);
    }

    [Fact]
    public async Task A_read_updates_the_profiles_it_holds_in_place()
    {
        var harness = new Harness();
        var season = harness.Add(_repoId, "Season 4", 3);
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        var held = harness.Store.Find(_repoId, season.Id)!;
        var changed = new List<string?>();
        held.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        season.Name = "Season 5";
        season.HeadRevision = 4;
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);

        Assert.Same(held, harness.Store.Find(_repoId, season.Id));
        Assert.Equal("Season 5", held.Name);
        Assert.Equal([nameof(Profile.Name), nameof(Profile.HeadRevision)], changed);
    }

    [Fact]
    public async Task A_read_that_finds_nothing_new_announces_nothing()
    {
        var harness = new Harness();
        harness.Add(_repoId, "Season 4", 3);
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        var announced = harness.Announced();

        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);

        Assert.Empty(announced);
    }

    [Fact]
    public async Task A_saved_revision_becomes_the_head_the_drift_check_compares_against()
    {
        var harness = new Harness();
        var season = harness.Add(_repoId, "Season 4", 3);
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        var announced = harness.Announced();

        await harness.Store.ApplyRevisionSavedAsync(_repoId, season.Id, 4);
        await harness.Store.ApplyRevisionSavedAsync(_repoId, season.Id, 4);

        Assert.Equal(4, harness.Store.GetHeadRevision(new ActiveProfile(_repoId, season.Id)));
        Assert.Equal([_repoId], announced);
    }

    [Fact]
    public async Task A_revision_saved_for_a_profile_not_held_is_ignored()
    {
        var harness = new Harness();
        var announced = harness.Announced();

        await harness.Store.ApplyRevisionSavedAsync(_repoId, Guid.NewGuid(), 4);

        Assert.Empty(announced);
        Assert.Empty(harness.Store.Live(_repoId));
    }

    [Fact]
    public async Task A_profile_created_while_the_list_was_being_read_is_listed_once()
    {
        var harness = new Harness();
        var gate = harness.Server.Hold();
        Profile? announced = null;
        harness.Store.ProfileCreated += x => announced = x;

        var read = harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        var created = await harness.Store.CreateAsync(_repoId, Guid.NewGuid(), "New", null, CancellationToken.None);
        gate.SetResult();
        await read;

        Assert.Same(created, Assert.Single(harness.Store.Live(_repoId)));
        Assert.Same(created, announced);
    }

    /// <summary>A retry or a second click after the first create's answer was lost.</summary>
    [Fact]
    public async Task Creating_again_with_the_same_request_id_holds_one_profile()
    {
        var harness = new Harness();
        var requestId = Guid.NewGuid();

        var first = await harness.Store.CreateAsync(_repoId, requestId, "New", null, CancellationToken.None);
        var second = await harness.Store.CreateAsync(_repoId, requestId, "New", null, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Same(first, Assert.Single(harness.Store.Live(_repoId)));
    }

    [Fact]
    public async Task Renaming_updates_the_held_profile()
    {
        var harness = new Harness();
        var season = harness.Add(_repoId, "Season 4", 3);
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        var held = harness.Store.Find(_repoId, season.Id)!;

        await harness.Store.RenameAsync(held, "Harvest", CancellationToken.None);

        Assert.Equal("Harvest", held.Name);
    }

    /// <summary>
    /// Somebody else renamed it since this client read it, so the rename is refused rather than writing
    /// over theirs - and the profile is read again, so looking again shows their name.
    /// </summary>
    [Fact]
    public async Task A_rename_made_against_an_old_version_is_refused_and_the_profile_read_again()
    {
        var harness = new Harness();
        var season = harness.Add(_repoId, "Season 4", 3);
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        var held = harness.Store.Find(_repoId, season.Id)!;

        season.Name = "Theirs";
        season.Version++;

        await Assert.ThrowsAsync<UserFriendlyException>(() => harness.Store.RenameAsync(held, "Mine", CancellationToken.None));

        Assert.Equal("Theirs", held.Name);
        Assert.Equal("Theirs", season.Name);
    }

    [Fact]
    public async Task A_rename_against_the_version_held_goes_through_and_moves_it_on()
    {
        var harness = new Harness();
        var season = harness.Add(_repoId, "Season 4", 3);
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        var held = harness.Store.Find(_repoId, season.Id)!;

        await harness.Store.RenameAsync(held, "Harvest", CancellationToken.None);
        await harness.Store.RenameAsync(held, "Winter", CancellationToken.None);

        Assert.Equal("Winter", held.Name);
        Assert.Equal(2, held.Version);
    }

    [Fact]
    public async Task Archiving_takes_the_profile_out_of_the_live_list_and_restoring_puts_it_back()
    {
        var harness = new Harness();
        var season = harness.Add(_repoId, "Season 4", 3);
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);

        await harness.Store.ArchiveAsync(harness.Store.Find(_repoId, season.Id)!, CancellationToken.None);

        Assert.Empty(harness.Store.Live(_repoId));
        Assert.Null(harness.Store.Find(_repoId, season.Id));

        var restored = await harness.Store.RestoreAsync(_repoId, season.Id, "Season 4 again", CancellationToken.None);

        Assert.Same(restored, Assert.Single(harness.Store.Live(_repoId)));
        Assert.Equal("Season 4 again", restored.Name);
    }

    [Fact]
    public async Task Restoring_a_revision_moves_the_head()
    {
        var harness = new Harness();
        var season = harness.Add(_repoId, "Season 4", 3);
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        var held = harness.Store.Find(_repoId, season.Id)!;

        var restored = await harness.Store.RestoreRevisionAsync(held, 1, basedOn: 3, CancellationToken.None);

        Assert.Equal(4, restored.Number);
        Assert.Equal(4, held.HeadRevision);
        Assert.Equal(3, Assert.Single(harness.Server.RevisionRestores).BasedOn);
    }

    /// <summary>Each restore is its own action, so a second one is not mistaken for a repeat of the first.</summary>
    [Fact]
    public async Task Two_restores_send_different_request_ids_and_each_moves_the_head()
    {
        var harness = new Harness();
        var season = harness.Add(_repoId, "Season 4", 3);
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        var held = harness.Store.Find(_repoId, season.Id)!;

        await harness.Store.RestoreRevisionAsync(held, 1, basedOn: 3, CancellationToken.None);
        var second = await harness.Store.RestoreRevisionAsync(held, 2, basedOn: 4, CancellationToken.None);

        Assert.Equal(5, second.Number);
        Assert.Equal(2, harness.Server.RevisionRestores.Select(x => x.RequestId).Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, harness.Server.RevisionRestores.Select(x => x.RequestId));
    }

    /// <summary>
    /// Somebody saved while the history was on screen. The restore is refused rather than undoing a
    /// save nobody here has seen, and the store reads the profiles again so the newer head shows.
    /// </summary>
    [Fact]
    public async Task A_restore_based_on_an_old_head_is_refused_and_the_list_is_read_again()
    {
        var harness = new Harness();
        var season = harness.Add(_repoId, "Season 4", 3);
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        var held = harness.Store.Find(_repoId, season.Id)!;
        season.HeadRevision = 4;

        await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Store.RestoreRevisionAsync(held, 1, basedOn: 3, CancellationToken.None));

        Assert.Equal(4, held.HeadRevision);
        Assert.Equal(4, season.HeadRevision);
    }

    [Fact]
    public async Task A_read_still_out_when_the_user_changes_lists_nothing_of_theirs()
    {
        var harness = new Harness();
        harness.Add(_repoId, "Season 4", 3);
        var gate = harness.Server.Hold();

        var read = harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        harness.Store.ClearUserState();
        gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Empty(harness.Store.Live(_repoId));
        Assert.False(harness.Store.IsLoaded(_repoId));
        Assert.Empty(harness.Store.LoadedRepos);
    }

    [Fact]
    public async Task Ensuring_a_repo_is_loaded_reads_it_once()
    {
        var harness = new Harness();
        harness.Add(_repoId, "Season 4", 3);

        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);
        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);

        Assert.Equal(1, harness.Server.Reads);
    }


    private sealed class Harness
    {
        public Harness()
        {
            Store = new ProfileStore(Server, InlineStoreDispatcher.Instance, NullLogger<ProfileStore>.Instance);
        }

        public FakeProfilesServer Server { get; } = new();
        public ProfileStore Store { get; }

        public ProfileDto Add(Guid repoId, string name, int head)
        {
            var profile = new ProfileDto { Id = Guid.NewGuid(), RepoId = repoId, Name = name, HeadRevision = head };
            Server.Profiles.Add(profile);

            return profile;
        }

        public List<Guid> Announced()
        {
            var announced = new List<Guid>();
            Store.Changed += announced.Add;

            return announced;
        }
    }
}
