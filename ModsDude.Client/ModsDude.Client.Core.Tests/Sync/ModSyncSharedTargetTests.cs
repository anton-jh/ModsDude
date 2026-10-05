using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// A folder other programs keep mods in too - a launcher's download cache - where sync touches only
/// what it installed.
/// </summary>
public class ModSyncSharedTargetTests
{
    [Fact]
    public async Task A_mod_somebody_else_keeps_there_is_left_alone()
    {
        using var fixture = Fixture();
        fixture.Install("other.zip", Mod("2.0.0", "theirs"));
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync();

        Assert.DoesNotContain(plan.Items, x => x.ModId.Value == "other");
        Assert.Contains("other.zip", plan.UnmanagedFileNames);

        var result = await fixture.ExecuteAsync(plan);

        Assert.True(result.Completed);
        Assert.Equal(Mod("2.0.0", "theirs"), fixture.ReadInstalled("other.zip"));
        Assert.Empty(fixture.RecycleBin.Recycled);
    }

    [Fact]
    public async Task Another_file_of_a_pinned_mod_is_left_alone_and_the_pinned_one_installed_beside_it()
    {
        using var fixture = Fixture(suffixed: true);
        fixture.Install("fs25_a-00000000.zip", Mod("0.9.0", "another server's"));
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync();

        var item = Assert.Single(plan.Items);
        Assert.Equal(ModSyncAction.Install, item.Action);

        await fixture.ExecuteAsync(plan);

        Assert.Equal(Mod("0.9.0", "another server's"), fixture.ReadInstalled("fs25_a-00000000.zip"));
        Assert.Equal(Mod("1.0.0", "a"), fixture.ReadInstalled(Suffixed("fs25_a", Mod("1.0.0", "a"))));
    }

    [Fact]
    public async Task A_mod_sync_installed_and_the_profile_dropped_is_removed()
    {
        using var fixture = Fixture();
        fixture.Install("other.zip", Mod("2.0.0", "theirs"));
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        fixture.Server.Unpin("fs25_a");
        fixture.Server.Pin("fs25_b", "1.0.0", Mod("1.0.0", "b"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.Equal(["fs25_b.zip", "other.zip"], fixture.FolderContents());
    }

    [Fact]
    public async Task The_right_file_already_at_the_placement_name_is_kept()
    {
        using var fixture = Fixture();
        fixture.Install("fs25_a.zip", Mod("1.0.0", "a"));
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync();

        Assert.Equal(ModSyncAction.Keep, Assert.Single(plan.Items).Action);
    }

    [Fact]
    public async Task Clearing_removes_only_what_sync_installed()
    {
        using var fixture = Fixture();
        fixture.Install("other.zip", Mod("2.0.0", "theirs"));
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        await fixture.ExecuteAsync(await fixture.PlanClearAsync());

        Assert.Equal(["other.zip"], fixture.FolderContents());
    }

    [Fact]
    public async Task Files_arriving_afterwards_are_not_drift()
    {
        using var fixture = Fixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        fixture.Install("downloaded-later.zip", Mod("1.0.0", "theirs"));

        Assert.Equal(DriftStatus.InSync, fixture.CheckDrift().Status);
    }

    [Fact]
    public async Task Removing_what_sync_installed_is_still_drift()
    {
        using var fixture = Fixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        File.Delete(fixture.Folder.Combine("fs25_a.zip"));

        Assert.Contains("fs25_a.zip", fixture.CheckDrift().Removed);
    }


    /// <param name="suffixed">
    /// Whether files are named the way the BeamMP launcher's cache names them: the mod, then the start
    /// of its hash.
    /// </param>
    private static SyncFixture Fixture(bool suffixed = false)
    {
        var fixture = new SyncFixture();
        fixture.Adapter.Target = fixture.Adapter.Target with { Shared = true };

        if (suffixed)
        {
            fixture.Adapter.ModNameOf = x => x.Contains('-') ? x[..x.IndexOf('-')] : x;
            fixture.Adapter.PlaceAs = x => $"{x.ModId.Value}-{x.ContentHash[..8]}.zip";
        }

        return fixture;
    }

    private static string Suffixed(string modId, string content) => $"{modId}-{SyncTestContent.HashOf(content)[..8]}.zip";

    private static string Mod(string version, string build) => SyncTestContent.File(version, build);
}
