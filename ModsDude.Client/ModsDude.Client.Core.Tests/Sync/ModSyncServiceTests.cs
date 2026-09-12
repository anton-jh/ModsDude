using ModsDude.Client.Core.GameAdapters;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

public class ModSyncServiceTests
{
    [Fact]
    public async Task An_empty_mod_folder_is_filled_from_the_repo()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Server.Pin("fs25_b", "2.0.0", Mod("2.0.0", "b"));

        var plan = await fixture.PlanAsync();

        Assert.Equal(2, plan.InstallCount);
        Assert.Equal(2, plan.HashesToFetch.Count);
        Assert.Empty(plan.Unrecognised);

        var result = await fixture.ExecuteAsync(plan);

        Assert.True(result.Completed);
        Assert.Equal(2, fixture.Downloader.Downloads);
        Assert.Equal(Mod("1.0.0", "a"), fixture.ReadInstalled("fs25_a.zip"));
        Assert.Equal(Mod("2.0.0", "b"), fixture.ReadInstalled("fs25_b.zip"));

        // Everything downloaded also stays in the store, so switching away and back costs nothing.
        Assert.True(fixture.ServingStore.Contains(SyncTestContent.HashOf(Mod("1.0.0", "a"))));

        // The manifest is what makes the next check a directory listing rather than 2,000 archives.
        Assert.True(result.ManifestWritten);
        Assert.Equal(InstanceDriftStatus.InSync, fixture.CheckDrift().Status);
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        var plan = await fixture.PlanAsync();

        Assert.False(plan.HasWork);
        Assert.Equal(1, plan.KeepCount);

        // Nothing to remove means the repo's mod list is never asked for, which at thousands of
        // registered versions is the difference between a re-apply being instant and being a fetch.
        Assert.Empty(plan.HashesToFetch);
    }

    [Fact]
    public async Task A_hash_another_disk_already_holds_is_copied_across_rather_than_downloaded()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.OtherStore.IngestAsync(
            new MemoryStream(SyncTestContent.Bytes(Mod("1.0.0", "a"))),
            SyncTestContent.HashOf(Mod("1.0.0", "a")),
            null,
            CancellationToken.None);

        var result = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.True(result.Completed);
        Assert.Equal(0, fixture.Downloader.Downloads);
        Assert.Equal(0, fixture.Server.DownloadLinksMinted);

        // A disk-to-disk copy beats a download every time, and it leaves the blob local for the next
        // install to this disk.
        Assert.True(fixture.ServingStore.Contains(SyncTestContent.HashOf(Mod("1.0.0", "a"))));
        Assert.Equal(Mod("1.0.0", "a"), fixture.ReadInstalled("fs25_a.zip"));
    }

    [Fact]
    public async Task A_download_that_does_not_hash_to_the_declared_address_stops_the_sync_before_anything_is_touched()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Server.Register("fs25_old", "1.0.0", Mod("1.0.0", "old"));
        fixture.Install("fs25_old.zip", Mod("1.0.0", "old"));

        fixture.Server.CorruptDownload = _ => SyncTestContent.Bytes(Mod("1.0.0", "hostile"));

        var plan = await fixture.PlanAsync();
        var result = await fixture.ExecuteAsync(plan);

        Assert.False(result.Completed);
        Assert.Single(result.Failures);

        // The destructive phase never ran, so the game is exactly as it was - the mod that was on
        // its way out is still installed, and nothing new is.
        Assert.True(File.Exists(fixture.Folder.Combine("fs25_old.zip")));
        Assert.False(File.Exists(fixture.Folder.Combine("fs25_a.zip")));
        Assert.False(result.ManifestWritten);
        Assert.False(fixture.ServingStore.Contains(SyncTestContent.HashOf(Mod("1.0.0", "a"))));
    }

    [Fact]
    public async Task An_unpinned_mod_the_repo_holds_is_kept_in_the_store_when_nothing_else_has_it()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Register("fs25_old", "1.0.0", Mod("1.0.0", "old"));
        fixture.Install("fs25_old.zip", Mod("1.0.0", "old"));

        var plan = await fixture.PlanAsync();

        Assert.Equal(1, plan.UninstallCount);

        var result = await fixture.ExecuteAsync(plan);

        Assert.True(result.Completed);
        Assert.False(File.Exists(fixture.Folder.Combine("fs25_old.zip")));

        // Switching to another profile and back must not re-download it.
        Assert.True(fixture.ServingStore.Contains(SyncTestContent.HashOf(Mod("1.0.0", "old"))));
        Assert.Empty(fixture.RecycleBin.Recycled);
    }

    [Fact]
    public async Task An_unpinned_mod_another_disk_already_holds_is_not_duplicated_onto_this_one()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Register("fs25_old", "1.0.0", Mod("1.0.0", "old"));
        fixture.Install("fs25_old.zip", Mod("1.0.0", "old"));

        var hash = SyncTestContent.HashOf(Mod("1.0.0", "old"));

        await fixture.OtherStore.IngestAsync(
            new MemoryStream(SyncTestContent.Bytes(Mod("1.0.0", "old"))),
            hash,
            null,
            CancellationToken.None);

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.False(File.Exists(fixture.Folder.Combine("fs25_old.zip")));

        // The bytes are already recoverable without a download, so a mod that lives on the other disk
        // does not get copied onto this one just to be uninstalled.
        Assert.False(fixture.ServingStore.Contains(hash));
    }

    [Fact]
    public async Task An_unrecognised_mod_goes_to_the_recycle_bin_and_is_named_first()
    {
        using var fixture = new SyncFixture();
        fixture.Install("fs25_mine.zip", Mod("1.0.0", "the user's own file"));

        var plan = await fixture.PlanAsync();

        Assert.Equal(1, plan.QuarantineCount);
        Assert.Equal("fs25_mine", Assert.Single(plan.Unrecognised).DisplayName);

        var result = await fixture.ExecuteAsync(plan);

        Assert.True(result.Completed);
        Assert.False(File.Exists(fixture.Folder.Combine("fs25_mine.zip")));

        // Never a delete: the repo cannot reproduce these bytes, so they leave by a route the user
        // can undo.
        Assert.Equal([Mod("1.0.0", "the user's own file")], fixture.RecycleBin.Recycled);
    }

    [Fact]
    public async Task Where_the_recycle_bin_is_unavailable_the_file_is_moved_to_quarantine_instead()
    {
        using var fixture = new SyncFixture(recycleBinAvailable: false);
        fixture.Install("fs25_mine.zip", Mod("1.0.0", "the user's own file"));

        var result = await fixture.ExecuteAsync(await fixture.PlanAsync());

        var quarantined = Assert.Single(result.Quarantined);

        Assert.Equal(QuarantineDestination.QuarantineFolder, quarantined.Destination);
        Assert.NotNull(quarantined.Path);
        Assert.Equal(Mod("1.0.0", "the user's own file"), File.ReadAllText(quarantined.Path));
        Assert.False(File.Exists(fixture.Folder.Combine("fs25_mine.zip")));
    }

    [Fact]
    public async Task A_file_that_is_not_a_readable_mod_is_left_alone()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Folder.WriteFile("readme.txt", "notes to self");

        var plan = await fixture.PlanAsync();

        Assert.Equal(["readme.txt"], plan.UnmanagedFileNames);

        await fixture.ExecuteAsync(plan);

        Assert.True(File.Exists(fixture.Folder.Combine("readme.txt")));

        // Recorded so the next drift check does not report it as something that appeared.
        Assert.Equal(InstanceDriftStatus.InSync, fixture.CheckDrift().Status);
    }

    [Fact]
    public async Task A_wrong_build_wearing_the_pinned_version_number_is_replaced()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "the build the profile pins"));
        fixture.Server.Register("fs25_a", "1.0.0-other", Mod("1.0.0", "a different build"));
        fixture.Install("fs25_a.zip", Mod("1.0.0", "a different build"));

        var plan = await fixture.PlanAsync();

        // The adapter reads 1.0.0 off both files. Only the hash tells them apart.
        Assert.Equal(1, plan.ReplaceCount);

        await fixture.ExecuteAsync(plan);

        Assert.Equal(Mod("1.0.0", "the build the profile pins"), fixture.ReadInstalled("fs25_a.zip"));
    }

    [Fact]
    public async Task Where_the_adapter_allows_it_installing_is_a_hardlink_into_the_store()
    {
        using var fixture = new SyncFixture(supportsHardlinks: true);
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync();

        Assert.Equal(MaterializationMethod.Hardlink, plan.Materialization.Method);
        Assert.False(plan.Materialization.FellBackToCopy);

        await fixture.ExecuteAsync(plan);

        var installed = fixture.Folder.Combine("fs25_a.zip");

        Assert.Equal(2, FileLinks.TryGetLinkCount(installed));
    }

    [Fact]
    public async Task Uninstalling_from_a_hardlink_served_disk_is_a_plain_delete()
    {
        using var fixture = new SyncFixture(supportsHardlinks: true);
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        // Switching to a profile that does not pin it. The file in the mod folder is the store entry,
        // so there is nothing to move.
        fixture.Server.Unpin("fs25_a");

        var result = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.True(result.Completed);
        Assert.False(File.Exists(fixture.Folder.Combine("fs25_a.zip")));
        Assert.Empty(fixture.RecycleBin.Recycled);

        // Switching back must not re-download it.
        Assert.True(fixture.ServingStore.Contains(SyncTestContent.HashOf(Mod("1.0.0", "a"))));

        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var downloadsBefore = fixture.Downloader.Downloads;

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.Equal(downloadsBefore, fixture.Downloader.Downloads);
        Assert.Equal(Mod("1.0.0", "a"), fixture.ReadInstalled("fs25_a.zip"));
        Assert.Equal(InstanceDriftStatus.InSync, fixture.CheckDrift().Status);
    }

    [Fact]
    public async Task An_adapter_without_hardlink_support_copies_and_is_not_warned_about()
    {
        using var fixture = new SyncFixture(supportsHardlinks: false);
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync();

        Assert.Equal(MaterializationMethod.Copy, plan.Materialization.Method);

        // Not a silent fallback: the game's updater may rewrite mod files in place, which through a
        // hardlink would corrupt a blob shared with every repo on the volume. Only a same-disk store
        // that could not link is worth a warning.
        Assert.False(plan.Materialization.FellBackToCopy);

        await fixture.ExecuteAsync(plan);

        Assert.Equal(1, FileLinks.TryGetLinkCount(fixture.Folder.Combine("fs25_a.zip")));
    }

    [Fact]
    public async Task Cancelling_during_the_fetch_leaves_the_mod_folder_untouched()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Server.Register("fs25_old", "1.0.0", Mod("1.0.0", "old"));
        fixture.Install("fs25_old.zip", Mod("1.0.0", "old"));

        using var cancellation = new CancellationTokenSource();
        fixture.Downloader.BeforeDownload = cancellation.Cancel;

        var plan = await fixture.PlanAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.ExecuteAsync(plan, null, cancellation.Token));

        Assert.True(File.Exists(fixture.Folder.Combine("fs25_old.zip")));
        Assert.False(File.Exists(fixture.Folder.Combine("fs25_a.zip")));
        Assert.Null(fixture.Manifests.TryRead(fixture.Game));
    }

    [Fact]
    public async Task Store_eviction_spares_what_this_profile_needs()
    {
        // A store far too small for what is about to be installed: without the pin, the sweep at the
        // end of the sync would take back the very files it just put in.
        using var fixture = new SyncFixture(storeMaxSizeBytes: 1);
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var result = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.True(result.Completed);
        Assert.True(fixture.ServingStore.Contains(SyncTestContent.HashOf(Mod("1.0.0", "a"))));
    }


    /// <summary>
    /// The sequence that made the drift notice unclearable: a mod dropped into the folder by hand,
    /// then imported and pinned. The folder is right and the manifest is not, so re-applying finds
    /// nothing to do - and if that path records nothing, the notice reports an addition for ever
    /// while the same notice's own status line says the folder already matches.
    /// </summary>
    [Fact]
    public async Task A_mod_added_by_hand_and_then_pinned_stops_being_drift_once_the_match_is_recorded()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());
        Assert.Equal(InstanceDriftStatus.InSync, fixture.CheckDrift().Status);

        // The user drops a mod into the folder from outside, which is what the game itself does.
        fixture.Install("fs25_b.zip", Mod("2.0.0", "b"));

        var drifted = fixture.CheckDrift();
        Assert.Equal(InstanceDriftStatus.Drifted, drifted.Status);
        Assert.Equal(["fs25_b.zip"], drifted.Added);

        // ...then imports it and pins it to the profile, which is what the notice sent them to do.
        fixture.Server.Pin("fs25_b", "2.0.0", Mod("2.0.0", "b"));

        var plan = await fixture.PlanAsync();

        Assert.False(plan.HasWork);
        Assert.Equal(2, plan.KeepCount);

        await fixture.Service.RecordAlreadyMatchedAsync(plan);

        Assert.Equal(InstanceDriftStatus.InSync, fixture.CheckDrift().Status);
    }

    /// <summary>
    /// The whole point of the exercise: the folder ends up holding the name the mod was imported
    /// under, not the normalized id. Farming Simulator's mod list shows filenames, so this is
    /// visible to the user in the game.
    /// </summary>
    [Fact]
    public async Task An_install_uses_the_name_the_repo_registered()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_mymod", "1.0.0", Mod("1.0.0", "a"), fileName: "FS25_MyMod.zip");

        var result = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.True(result.Completed);
        Assert.Equal(["FS25_MyMod.zip"], fixture.FolderContents());
        Assert.Equal(InstanceDriftStatus.InSync, fixture.CheckDrift().Status);
    }

    /// <summary>
    /// A registered name is a string one member of the repo chose, and it decides a path every other
    /// member writes to. A name that does not belong to the mod it is registered under is ignored
    /// outright rather than trusted, so the worst a repo can do is respell its own mods' files.
    /// </summary>
    [Fact]
    public async Task A_registered_name_that_does_not_belong_to_its_mod_is_ignored()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"), fileName: @"..\..\fs25_a.zip");
        fixture.Server.Pin("fs25_b", "1.0.0", Mod("1.0.0", "b"), fileName: "fs25_a.zip");

        var result = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.True(result.Completed);
        Assert.Equal(["fs25_a.zip", "fs25_b.zip"], fixture.FolderContents());
    }

    /// <summary>
    /// A folder an older client lower-cased, corrected on the next apply. The bytes never move - it
    /// is one directory operation - and it is a case-only rename, which is the one a filesystem is
    /// most likely to refuse outright.
    /// </summary>
    [Fact]
    public async Task A_folder_named_by_an_older_client_is_renamed_rather_than_refetched()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"), fileName: "fs25_a.zip");

        await fixture.ExecuteAsync(await fixture.PlanAsync());
        Assert.Equal(["fs25_a.zip"], fixture.FolderContents());

        // Somebody re-imports the mod from the folder it was downloaded to, and the repo learns
        // what it is really called.
        fixture.Server.Rename("fs25_a", "FS25_A.zip");

        var plan = await fixture.PlanAsync();

        Assert.True(plan.HasWork);
        Assert.Equal(1, plan.RenameCount);
        Assert.Empty(plan.HashesToFetch);

        var downloads = fixture.Downloader.Downloads;
        var result = await fixture.ExecuteAsync(plan);

        Assert.True(result.Completed);
        Assert.Equal(downloads, fixture.Downloader.Downloads);
        Assert.Equal(["FS25_A.zip"], fixture.FolderContents());
        Assert.Equal(Mod("1.0.0", "a"), fixture.ReadInstalled("FS25_A.zip"));

        // Recorded under the new name, or the next drift check reports one file added and one gone.
        Assert.Equal(InstanceDriftStatus.InSync, fixture.CheckDrift().Status);
    }

    [Fact]
    public async Task Recording_a_match_is_refused_for_a_plan_that_has_work()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync();

        Assert.True(plan.HasWork);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RecordAlreadyMatchedAsync(plan));
    }


    /// <summary>
    /// Head is what a game following its profile wants, and asking for nothing is how the client
    /// says so. The endpoint has served any revision all along; this is only the client no longer
    /// insisting.
    /// </summary>
    [Fact]
    public async Task Applying_without_a_revision_asks_for_head_and_records_what_head_turned_out_to_be()
    {
        using var fixture = new SyncFixture();
        fixture.Server.HeadRevision = 1004;
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync();

        Assert.Equal([null], fixture.Server.RevisionsRequested);
        Assert.Equal(1004, plan.ProfileRevision);

        await fixture.ExecuteAsync(plan);

        Assert.Equal(1004, fixture.Manifests.TryRead(fixture.Game)?.ProfileRevision);
    }

    /// <summary>
    /// The other half: a game that has to stay on an older list - one holding a past savegame,
    /// whose revision does not move - gets that revision installed and recorded, rather than being
    /// quietly taken to head by the only apply the client used to know how to do.
    /// </summary>
    [Fact]
    public async Task Applying_a_named_revision_asks_for_it_and_records_it()
    {
        using var fixture = new SyncFixture();
        fixture.Server.HeadRevision = 1004;
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync(revision: 4);

        Assert.Equal([4], fixture.Server.RevisionsRequested);
        Assert.Equal(4, plan.ProfileRevision);

        await fixture.ExecuteAsync(plan);

        Assert.Equal(4, fixture.Manifests.TryRead(fixture.Game)?.ProfileRevision);
    }

    /// <summary>
    /// <b>The ordering play attribution rests on.</b> The manifest is the only thing that says which
    /// mod list this folder runs, so an observation taken after it has been rewritten cannot tell an
    /// evening played before the apply from one played after. What it must see is the outgoing
    /// revision, and here that is 4 while the apply is moving the folder to 1004.
    /// </summary>
    [Fact]
    public async Task An_apply_observes_savegame_play_while_the_manifest_still_says_the_old_revision()
    {
        using var fixture = new SyncFixture();
        fixture.Server.HeadRevision = 4;
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.Equal([null], fixture.Held.Observed);

        fixture.Server.HeadRevision = 1004;
        fixture.Server.Pin("fs25_b", "2.0.0", Mod("2.0.0", "b"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.Equal([null, 4], fixture.Held.Observed);
        Assert.Equal(1004, fixture.Manifests.TryRead(fixture.Game)?.ProfileRevision);
    }

    /// <summary>
    /// A revision can move without a single mod doing so - a thousand edits that cancel out, or an
    /// unrelated pin added and removed. The no-work path rewrites the manifest too, so it has to
    /// attribute the play first or an evening would be credited to a list it never ran on.
    /// </summary>
    [Fact]
    public async Task A_plan_with_no_work_observes_before_it_records_the_match()
    {
        using var fixture = new SyncFixture();
        fixture.Server.HeadRevision = 4;
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        fixture.Server.HeadRevision = 1004;

        var plan = await fixture.PlanAsync();

        Assert.False(plan.HasWork);

        await fixture.Service.RecordAlreadyMatchedAsync(plan);

        Assert.Equal([null, 4], fixture.Held.Observed);
        Assert.Equal(1004, fixture.Manifests.TryRead(fixture.Game)?.ProfileRevision);
    }

    /// <summary>
    /// <b>The apply that would take a savegame off its mod list, resolved rather than refused.</b> An
    /// game holding a past savegame is pinned to that savegame's revision, and every caller asks for
    /// nothing in particular - so the one place all of them pass through is where head stops being the
    /// answer. Nobody had to know a savegame was involved.
    /// </summary>
    [Fact]
    public async Task A_game_holding_a_past_savegame_gets_its_revision_rather_than_head()
    {
        using var fixture = new SyncFixture();
        fixture.Server.HeadRevision = 1004;
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Held.Hold(fixture.Game, fixture.Server.ProfileId, targetRevision: 4);

        var plan = await fixture.PlanAsync();

        Assert.Equal([4], fixture.Server.RevisionsRequested);
        Assert.Equal(4, plan.ProfileRevision);

        await fixture.ExecuteAsync(plan);

        // Which is then what the drift check compares against, so the game is not permanently
        // behind head by construction.
        Assert.Equal(4, fixture.Manifests.TryRead(fixture.Game)?.ProfileRevision);
    }

    /// <summary>
    /// A caller that names a revision has said something the game cannot know better than - the
    /// check-out dialog previewing a savegame nothing is holding yet - so it is taken at its word, and
    /// then checked against what <em>is</em> held.
    /// </summary>
    [Fact]
    public async Task A_named_revision_that_a_held_past_savegame_forbids_is_refused()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Held.Hold(fixture.Game, fixture.Server.ProfileId, targetRevision: 4);

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(() => fixture.PlanAsync(revision: 1004));

        Assert.Contains("runs on one revision", exception.UserMessage);
        Assert.Empty(fixture.Server.RevisionsRequested);
    }

    /// <summary>
    /// The active-profile switch, refused at the one place no apply path gets past. Nothing is planned
    /// and the repo is never asked: a savegame following another mod list is not a state a plan could
    /// describe safely.
    /// </summary>
    [Fact]
    public async Task Applying_another_profile_while_a_savegame_is_held_is_refused()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Held.Hold(fixture.Game, Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(() => fixture.PlanAsync());

        Assert.Contains("another mod list", exception.UserMessage);
        Assert.Empty(fixture.Server.RevisionsRequested);
    }

    /// <summary>
    /// A savegame following no mod list claims nothing about the folder, so a game holding one is
    /// a game holding nothing as far as any of this is concerned.
    /// </summary>
    [Fact]
    public async Task A_held_savegame_with_no_profile_constrains_no_apply()
    {
        using var fixture = new SyncFixture();
        fixture.Server.HeadRevision = 1004;
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Held.HoldWithNoProfile(fixture.Game);

        var plan = await fixture.PlanAsync();

        Assert.Equal([null], fixture.Server.RevisionsRequested);
        Assert.Equal(1004, plan.ProfileRevision);
    }

    /// <summary>
    /// A sync that failed never wrote the manifest, so the folder is still on the revision it was -
    /// and there is nothing to close.
    /// </summary>
    [Fact]
    public async Task A_sync_that_fails_observes_nothing()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Server.CorruptDownload = _ => SyncTestContent.Bytes(Mod("1.0.0", "hostile"));

        var result = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.False(result.Completed);
        Assert.Empty(fixture.Held.Observed);
    }


    private static string Mod(string version, string build) => SyncTestContent.File(version, build);


    private sealed class SyncFixture : IDisposable
    {
        private readonly TempDirectory _serving = new("sync-store");
        private readonly TempDirectory _other = new("sync-other-store");
        private readonly TempDirectory _manifests = new("sync-manifests");


        public SyncFixture(
            bool supportsHardlinks = false,
            bool recycleBinAvailable = true,
            long storeMaxSizeBytes = 1024L * 1024 * 1024)
        {
            ServingStore = new ContentStore("C:\\", _serving.Path, storeMaxSizeBytes);
            OtherStore = new ContentStore("D:\\", _other.Path, storeMaxSizeBytes);

            Adapter = new FakeModFolderAdapter(Folder.Path, supportsHardlinks);
            Downloader = new FakeModFileDownloader(Server);
            RecycleBin = new FakeRecycleBin(recycleBinAvailable);
            Manifests = new SyncManifestStore(_manifests.Path);
            Drift = new InstanceDriftService(Manifests, NullLogger<InstanceDriftService>.Instance);
            Held = new FakeHeldSavegames(Manifests);

            Service = new ModSyncService(
                Server,
                Server,
                Server,
                Downloader,
                new FakeStoreProvider(ServingStore, OtherStore),
                Manifests,
                RecycleBin,
                new FakeModFolders(new GameModFolder(Game, Folder.Path)),
                Held,
                NullLogger<ModSyncService>.Instance);
        }


        public TempDirectory Folder { get; } = new("sync-mods");
        public FakeSyncServer Server { get; } = new();
        public FakeModFolderAdapter Adapter { get; }
        public FakeModFileDownloader Downloader { get; }
        public FakeRecycleBin RecycleBin { get; }
        public SyncManifestStore Manifests { get; }
        public InstanceDriftService Drift { get; }
        public FakeHeldSavegames Held { get; }
        public ModSyncService Service { get; }
        public ContentStore ServingStore { get; }
        public ContentStore OtherStore { get; }
        public GameIdentity Game { get; } = Keys.Game();


        public Task<ModSyncPlan> PlanAsync(int? revision = null)
            => Service.PlanAsync(
                new ModSyncRequest(Game, Adapter, Server.RepoId, Server.ProfileId) { Revision = revision },
                CancellationToken.None);

        public Task<ModSyncResult> ExecuteAsync(ModSyncPlan plan)
            => Service.ExecuteAsync(plan, null, CancellationToken.None);

        public void Install(string name, string content) => Folder.WriteFile(name, content);

        /// <summary>Every name in the mod folder, which is where casing is visible at all.</summary>
        public IReadOnlyList<string> FolderContents()
            => [.. Directory.EnumerateFiles(Folder.Path).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

        public string ReadInstalled(string name) => File.ReadAllText(Folder.Combine(name));

        public InstanceDriftReport CheckDrift()
            => Drift.Check(Game, new ActiveProfile(Server.RepoId, Server.ProfileId), Folder.Path);

        public void Dispose()
        {
            Folder.Dispose();
            _serving.Dispose();
            _other.Dispose();
            _manifests.Dispose();
        }
    }
}
