using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Sync;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// Files other than mods that an adapter needs changed to match them - a game's own list of enabled
/// mods, say. The engine writes them; these tests stand in a plain list for one.
/// </summary>
public class ModSyncManagedFileTests
{
    private const string _listName = "modlist.txt";


    [Fact]
    public async Task A_managed_file_is_brought_in_line_with_the_mods_applied()
    {
        using var fixture = Fixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Server.Pin("fs25_b", "1.0.0", Mod("1.0.0", "b"));

        var result = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.True(result.Completed);
        Assert.Equal(["managed:fs25_a", "managed:fs25_b"], ReadList(fixture));
    }

    [Fact]
    public async Task A_managed_file_keeps_what_the_adapter_does_not_manage()
    {
        using var fixture = Fixture();
        fixture.Install(_listName, "mine\n");
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.Equal(["mine", "managed:fs25_a"], ReadList(fixture));
    }

    [Fact]
    public async Task A_second_apply_leaves_the_managed_file_alone()
    {
        using var fixture = Fixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());
        var written = File.GetLastWriteTimeUtc(fixture.Folder.Combine(_listName));

        var plan = await fixture.PlanAsync();

        Assert.False(plan.HasWork);
        await fixture.ExecuteAsync(plan);
        Assert.Equal(written, File.GetLastWriteTimeUtc(fixture.Folder.Combine(_listName)));
    }

    [Fact]
    public async Task A_managed_file_out_of_date_is_work_even_when_every_mod_matches()
    {
        using var fixture = Fixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());
        File.WriteAllText(fixture.Folder.Combine(_listName), "");

        var plan = await fixture.PlanAsync();

        Assert.True(plan.HasWork);
        Assert.Equal(1, plan.KeepCount);

        await fixture.ExecuteAsync(plan);

        Assert.Equal(["managed:fs25_a"], ReadList(fixture));
    }

    [Fact]
    public async Task A_managed_file_in_the_mod_folder_is_not_reported_as_an_addition()
    {
        using var fixture = Fixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.Equal(DriftStatus.InSync, fixture.CheckDrift().Status);
    }

    [Fact]
    public async Task Changing_a_managed_file_afterwards_is_drift()
    {
        using var fixture = Fixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        File.AppendAllText(fixture.Folder.Combine(_listName), "added by the game\n");

        var drift = fixture.CheckDrift();

        Assert.Equal(DriftStatus.Drifted, drift.Status);
        Assert.Contains(_listName, drift.Changed);
    }

    [Fact]
    public async Task Deleting_a_managed_file_afterwards_is_drift()
    {
        using var fixture = Fixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        File.Delete(fixture.Folder.Combine(_listName));

        Assert.Contains(_listName, fixture.CheckDrift().Removed);
    }

    [Fact]
    public async Task Replaced_content_goes_to_the_recycle_bin_when_the_adapter_asks()
    {
        using var fixture = Fixture(recycleReplaced: true);
        fixture.Install(_listName, "mine\n");
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.Contains("mine\n", fixture.RecycleBin.Recycled);
    }

    [Fact]
    public async Task A_managed_file_that_cannot_be_written_fails_the_apply_and_a_rerun_converges()
    {
        using var fixture = Fixture();
        fixture.Install(_listName, "");
        var list = new FileInfo(fixture.Folder.Combine(_listName)) { IsReadOnly = true };
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var failed = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.False(failed.Completed);
        Assert.Equal(_listName, Assert.Single(failed.Failures).Subject);
        Assert.False(failed.ManifestWritten);

        list.IsReadOnly = false;
        var rerun = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.True(rerun.Completed);
        Assert.Equal(["managed:fs25_a"], ReadList(fixture));
        Assert.Equal(DriftStatus.InSync, fixture.CheckDrift().Status);
    }

    [Fact]
    public async Task A_managed_file_the_adapter_cannot_work_out_refuses_the_plan_before_anything_is_touched()
    {
        using var fixture = new SyncFixture();
        fixture.Adapter.ManagedFiles = _ => [new GameFileEdit(_listName, _ => throw new InvalidDataException("Simulated: unreadable."))];
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.PlanAsync());

        Assert.Empty(fixture.FolderContents());
    }

    [Fact]
    public async Task Clearing_the_folder_updates_the_managed_file_too()
    {
        using var fixture = Fixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        await fixture.ExecuteAsync(await fixture.PlanClearAsync());

        Assert.Empty(ReadList(fixture));
    }

    [Fact]
    public async Task Planning_is_refused_while_the_game_is_running()
    {
        using var fixture = Fixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Guard.Running = true;

        var refusal = await Assert.ThrowsAsync<GameRunningException>(() => fixture.PlanAsync());

        Assert.Equal("Test Game", refusal.GameName);
    }

    [Fact]
    public async Task Executing_is_refused_while_the_game_is_running_and_nothing_is_touched()
    {
        using var fixture = Fixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        var plan = await fixture.PlanAsync();
        fixture.Guard.Running = true;

        await Assert.ThrowsAsync<GameRunningException>(() => fixture.ExecuteAsync(plan));

        Assert.Empty(fixture.FolderContents());
    }


    private static SyncFixture Fixture(bool recycleReplaced = false)
    {
        var fixture = new SyncFixture();
        fixture.Adapter.ManagedFiles = context => [new GameFileEdit(_listName, ListFor(context)) { RecycleReplaced = recycleReplaced }];

        return fixture;
    }

    /// <summary>A list of the desired mods, keeping every line it does not own.</summary>
    private static Func<byte[]?, byte[]?> ListFor(ModLayoutContext context) => current =>
    {
        var kept = current is null
            ? []
            : Encoding.UTF8.GetString(current)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.StartsWith("managed:", StringComparison.Ordinal) is false);

        var managed = context.Desired
            .Select(x => $"managed:{x.ModId.Value}")
            .Order(StringComparer.Ordinal);

        return Encoding.UTF8.GetBytes(string.Concat(kept.Concat(managed).Select(x => x + "\n")));
    };

    private static IReadOnlyList<string> ReadList(SyncFixture fixture)
        => File.ReadAllText(fixture.Folder.Combine(_listName)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string Mod(string version, string build) => SyncTestContent.File(version, build);
}
