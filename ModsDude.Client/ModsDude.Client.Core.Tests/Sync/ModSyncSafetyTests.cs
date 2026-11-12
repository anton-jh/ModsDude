using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// What an apply does when the world moves under it: a game launched during the fetch, files changed
/// between the plan and the work, and what an earlier apply killed part way left behind.
/// </summary>
public class ModSyncSafetyTests
{
    [Fact]
    public async Task A_game_started_during_the_fetch_stops_the_apply_before_the_folder_is_touched()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Install("fs25_mine.zip", Mod("1.0.0", "the user's own file"));

        var plan = await fixture.PlanAsync();

        // The plan's check and the executor's first pass; the game is up by the one after the fetch.
        fixture.Guard.StartsAfterChecks = fixture.Guard.Checks + 1;

        await Assert.ThrowsAsync<GameRunningException>(() => fixture.ExecuteAsync(plan));

        Assert.Equal(["fs25_mine.zip"], fixture.FolderContents());
        Assert.Empty(fixture.RecycleBin.Recycled);
        Assert.Null(fixture.Manifests.TryRead(fixture.Target));
    }

    [Fact]
    public async Task A_kept_file_changed_before_the_match_is_recorded_is_recorded_as_suspect()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Install("fs25_a.zip", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync();

        Assert.False(plan.HasWork);

        // The game updated it while the confirmation was open.
        fixture.Install("fs25_a.zip", Mod("1.0.0", "a newer build from the game"));

        await fixture.Service.RecordAlreadyMatchedAsync(plan);

        var entry = Assert.Single(fixture.Manifests.TryRead(fixture.Target)!.Entries);

        Assert.True(entry.ContentSuspect);
        Assert.Equal(["fs25_a.zip"], fixture.CheckDrift().Changed);

        // The next plan reads the file rather than trusting the hash the entry no longer vouches for.
        var next = await fixture.PlanAsync();

        Assert.Equal(ModSyncAction.Replace, Assert.Single(next.Items).Action);
    }

    [Fact]
    public async Task A_suspect_entry_is_hashed_again_even_where_its_size_and_time_still_match()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        var manifest = fixture.Manifests.TryRead(fixture.Target)!;
        var hash = manifest.Entries.Single().ContentHash;

        fixture.Manifests.MarkSuspect(fixture.Target, new HashSet<string>([hash]));

        Assert.True(Assert.Single(fixture.Manifests.TryRead(fixture.Target)!.Entries).ContentSuspect);
        Assert.Equal(["fs25_a.zip"], fixture.CheckDrift().Changed);

        // The bytes are still right, so hashing them keeps the file and the re-apply clears the flag.
        var plan = await fixture.PlanAsync();

        Assert.False(plan.HasWork);

        await fixture.Service.RecordAlreadyMatchedAsync(plan);

        Assert.False(Assert.Single(fixture.Manifests.TryRead(fixture.Target)!.Entries).ContentSuspect);
    }

    [Fact]
    public async Task A_recoverable_file_whose_bytes_changed_under_the_same_size_and_time_is_recycled()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Register("fs25_old", "1.0.0", Mod("1.0.0", "old1"));
        fixture.Install("fs25_old.zip", Mod("1.0.0", "old1"));
        await fixture.SeedIntoStoreAsync(Mod("1.0.0", "old1"));

        var plan = await fixture.PlanAsync();

        Assert.Equal(1, plan.UninstallCount);

        // Rewritten by something that put the old size and time back, so only the bytes can tell.
        var path = fixture.Folder.Combine("fs25_old.zip");
        var modified = File.GetLastWriteTimeUtc(path);

        fixture.Install("fs25_old.zip", Mod("1.0.0", "old2"));
        File.SetLastWriteTimeUtc(path, modified);

        var result = await fixture.ExecuteAsync(plan);

        Assert.True(result.Completed);
        Assert.Equal([Mod("1.0.0", "old2")], fixture.RecycleBin.Recycled);
    }

    [Fact]
    public async Task A_file_left_part_way_through_a_rename_is_put_back_before_planning()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Install("fs25_a.zip.modsdude-renaming", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync();

        Assert.False(plan.HasWork);
        Assert.Equal(["fs25_a.zip"], fixture.FolderContents());
        Assert.Equal(Mod("1.0.0", "a"), fixture.ReadInstalled("fs25_a.zip"));
    }

    [Fact]
    public async Task A_file_left_part_way_through_a_rename_beside_its_name_goes_to_the_recycle_bin()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Install("fs25_a.zip", Mod("1.0.0", "a"));
        fixture.Install("fs25_a.zip.modsdude-renaming", Mod("1.0.0", "the other one"));

        await fixture.PlanAsync();

        Assert.Equal(["fs25_a.zip"], fixture.FolderContents());
        Assert.Equal([Mod("1.0.0", "the other one")], fixture.RecycleBin.Recycled);
    }

    [Fact]
    public async Task A_staging_copy_an_interrupted_apply_left_is_deleted_before_planning()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Install($"{Guid.NewGuid():N}.modsdude-copy", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync();

        Assert.Empty(fixture.FolderContents());
        Assert.Empty(plan.UnmanagedFileNames);
        Assert.Empty(fixture.RecycleBin.Recycled);
    }

    [Fact]
    public async Task An_apply_deletes_what_interrupted_fetches_left_in_the_store()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.SeedIntoStoreAsync(Mod("0.1.0", "makes the store exist"));

        var leftover = Path.Combine(fixture.ServingStore.RootPath, "tmp", $"{Guid.NewGuid():N}.part");
        File.WriteAllText(leftover, "half a download");

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.False(File.Exists(leftover));
    }


    private static string Mod(string version, string build) => SyncTestContent.File(version, build);
}
