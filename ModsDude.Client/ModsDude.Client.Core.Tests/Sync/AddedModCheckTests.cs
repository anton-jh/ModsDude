using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

public class AddedModCheckTests
{
    [Fact]
    public async Task An_added_file_that_is_not_a_mod_is_not_drift_and_an_added_mod_is()
    {
        using var fixture = await AppliedAsync();
        fixture.Install("readme.zip", "notes to self");
        fixture.Install("fs25_b.zip", Mod("1.0.0", "b"));
        fixture.Adapter.Opened.Clear();

        var report = await fixture.CheckDriftAsync();

        Assert.Equal(DriftStatus.Drifted, report.Status);
        Assert.Equal(["fs25_b.zip"], report.Added);

        // Only what was added is opened: the installed mods are answered by the manifest.
        Assert.Equal(["fs25_b.zip", "readme.zip"], fixture.Adapter.Opened.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_folder_with_only_a_non_mod_added_is_in_sync_and_the_next_apply_leaves_it_alone()
    {
        using var fixture = await AppliedAsync();
        fixture.Install("readme.zip", "notes to self");

        Assert.Equal(DriftStatus.InSync, (await fixture.CheckDriftAsync()).Status);

        var plan = await fixture.PlanAsync();

        Assert.Empty(plan.Unrecognised);
        Assert.Contains("readme.zip", plan.UnmanagedFiles.Select(x => x.FileName));
    }

    /// <summary>
    /// A broken download the last apply left alone, finished afterwards under the same name, is a
    /// mod that is not in the profile.
    /// </summary>
    [Fact]
    public async Task A_mod_put_down_under_the_name_of_a_file_the_last_apply_left_alone_is_drift()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Install("fs25_b.zip", "half a download");
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.Equal(DriftStatus.InSync, (await fixture.CheckDriftAsync()).Status);

        fixture.Install("fs25_b.zip", Mod("1.0.0", "b"));

        var report = await fixture.CheckDriftAsync();

        Assert.Equal(DriftStatus.Drifted, report.Status);
        Assert.Equal(["fs25_b.zip"], report.Added);
    }

    [Fact]
    public async Task With_no_adapter_to_ask_every_added_file_is_reported()
    {
        using var folder = new TempDirectory("added-no-adapter");
        var check = new AddedModCheck(new FakeModTargetAdapters(_ => null), new RecordingLogger<AddedModCheck>());

        var mods = await check.FindModsAsync(Manifest(folder.Path), ["readme.zip", "fs25_b.zip"], CancellationToken.None);

        Assert.Equal(["readme.zip", "fs25_b.zip"], mods);
    }

    [Fact]
    public async Task An_adapter_that_refuses_its_settings_reports_every_added_file_and_is_logged()
    {
        using var folder = new TempDirectory("added-refused");
        var logger = new RecordingLogger<AddedModCheck>();
        var check = new AddedModCheck(
            new FakeModTargetAdapters(_ => throw new UserFriendlyException("Settings refused", "The adapter could not read its settings.")),
            logger);

        var mods = await check.FindModsAsync(Manifest(folder.Path), ["readme.zip"], CancellationToken.None);

        Assert.Equal(["readme.zip"], mods);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task A_target_the_settings_moved_elsewhere_reports_every_added_file()
    {
        using var folder = new TempDirectory("added-moved");
        using var elsewhere = new TempDirectory("added-elsewhere");
        var adapter = new FakeModFolderAdapter(elsewhere.Path, supportsHardlinks: false);
        var check = new AddedModCheck(
            new FakeModTargetAdapters(_ => new ResolvedModTarget(adapter, adapter.Target)),
            new RecordingLogger<AddedModCheck>());

        var mods = await check.FindModsAsync(Manifest(folder.Path), ["readme.zip"], CancellationToken.None);

        Assert.Equal(["readme.zip"], mods);
        Assert.Empty(adapter.Opened);
    }


    private static async Task<SyncFixture> AppliedAsync()
    {
        var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        return fixture;
    }

    private static SyncManifest Manifest(string modFolder) => new()
    {
        Target = Keys.Target(),
        RepoId = Guid.NewGuid(),
        ProfileId = Guid.NewGuid(),
        SyncedAt = DateTimeOffset.UtcNow,
        ModFolder = modFolder,
        Entries = []
    };

    private static string Mod(string version, string build) => SyncTestContent.File(version, build);
}
