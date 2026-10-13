using ModsDude.Client.Core.Disks;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// Refusing an apply or a fetch that would not fit on disk, before anything is downloaded or touched.
/// </summary>
public class ModSyncDiskSpaceTests
{
    private const long _reserve = DiskSpaceGuard.ReserveBytes;

    private static readonly string _content = SyncTestContent.File("1.0.0", "a");
    private static readonly long _size = SyncTestContent.Bytes(_content).Length;


    [Fact]
    public async Task An_apply_that_does_not_fit_is_refused_before_anything_is_touched()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", _content);
        var plan = await fixture.PlanAsync();

        // Filled up between the plan and the work: the store's copy and the mod folder's, one byte short.
        fixture.DiskSpace.Available = 2 * _size + _reserve - 1;

        var exception = await Assert.ThrowsAsync<NotEnoughDiskSpaceException>(() => fixture.ExecuteAsync(plan));

        Assert.Equal(1, Assert.Single(exception.Shortfalls).MissingBytes);
        Assert.Equal(0, fixture.Downloader.Downloads);
        Assert.Empty(fixture.FolderContents());
        Assert.Null(fixture.Manifests.TryRead(fixture.Target));
    }

    [Fact]
    public async Task An_apply_that_leaves_the_reserve_free_goes_ahead()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", _content);
        fixture.DiskSpace.Available = 2 * _size + _reserve;

        var result = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.True(result.Completed);
        Assert.Equal(["fs25_a.zip"], fixture.FolderContents());
    }

    [Fact]
    public async Task A_mod_the_store_already_holds_needs_room_only_for_its_copy_in_the_mod_folder()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", _content);
        await fixture.SeedIntoStoreAsync(_content);
        fixture.DiskSpace.Available = _size + _reserve;

        Assert.True((await fixture.ExecuteAsync(await fixture.PlanAsync())).Completed);
    }

    [Fact]
    public async Task A_hardlinked_install_needs_room_only_in_the_store()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", _content);
        var plan = Hardlinked(await fixture.PlanAsync());

        Assert.Equal([new DiskSpaceNeed(VolumeOf(fixture), _size)], ModSyncSpaceNeeds.For([plan]).ByVolume);
    }

    [Fact]
    public async Task Plans_filling_one_store_count_a_mod_they_share_once()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", _content);
        var plan = Hardlinked(await fixture.PlanAsync());

        fixture.DiskSpace.Available = _size + _reserve;
        fixture.Service.EnsureRoomFor([plan, plan]);

        fixture.DiskSpace.Available = _size + _reserve - 1;
        Assert.Throws<NotEnoughDiskSpaceException>(() => fixture.Service.EnsureRoomFor([plan, plan]));
    }

    [Fact]
    public async Task Unlinking_a_file_needs_room_for_its_copy()
    {
        using var fixture = new SyncFixture();
        var plan = await fixture.PlanAsync() with
        {
            Items = [new ModSyncItem
            {
                Action = ModSyncAction.Unlink,
                ModId = ModKey.From("fs25_a"),
                DisplayName = "fs25_a",
                FileName = "fs25_a.zip",
                InstalledPath = fixture.Folder.Combine("fs25_a.zip"),
                InstalledSize = 300
            }]
        };

        Assert.Equal([new DiskSpaceNeed(VolumeOf(fixture), 300)], ModSyncSpaceNeeds.For([plan]).ByVolume);
    }

    [Fact]
    public async Task An_unrecognised_file_moved_to_a_chosen_folder_on_another_volume_needs_room_there()
    {
        using var fixture = new SyncFixture();
        var plan = await fixture.PlanAsync() with
        {
            QuarantineFolder = "Q:\\Kept",
            Items = [new ModSyncItem
            {
                Action = ModSyncAction.Quarantine,
                ModId = ModKey.From("fs25_mine"),
                DisplayName = "fs25_mine",
                InstalledPath = fixture.Folder.Combine("fs25_mine.zip"),
                InstalledSize = 300
            }]
        };

        Assert.Equal([new DiskSpaceNeed("Q:\\", 300)], ModSyncSpaceNeeds.For([plan]).ByVolume);
    }

    [Fact]
    public async Task A_removed_mod_kept_in_a_store_on_another_volume_needs_room_there()
    {
        using var fixture = new SyncFixture();
        var elsewhere = new ContentStore("R:\\", "R:\\ModsDude", long.MaxValue);
        var plan = await fixture.PlanAsync() with
        {
            ServingStore = elsewhere,
            AllStores = [elsewhere],
            Items = [new ModSyncItem
            {
                Action = ModSyncAction.UninstallRecoverable,
                ModId = ModKey.From("fs25_old"),
                DisplayName = "fs25_old",
                InstalledPath = fixture.Folder.Combine("fs25_old.zip"),
                InstalledHash = SyncTestContent.HashOf("old"),
                InstalledIsRecoverable = true,
                InstalledSize = 300
            }]
        };

        Assert.Equal([new DiskSpaceNeed("R:\\", 300)], ModSyncSpaceNeeds.For([plan]).ByVolume);
    }

    [Fact]
    public async Task A_fetch_that_does_not_fit_is_refused_before_downloading()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", _content);
        fixture.DiskSpace.Available = _size + _reserve - 1;

        await Assert.ThrowsAsync<NotEnoughDiskSpaceException>(fixture.FetchAsync);

        Assert.Equal(0, fixture.Downloader.Downloads);
        Assert.False(fixture.ServingStore.Contains(SyncTestContent.HashOf(_content)));
    }

    [Fact]
    public async Task A_disk_filling_up_during_the_fetch_fails_the_apply_as_out_of_space_and_touches_nothing()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", _content);
        fixture.Downloader.BeforeDownload = () => throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));

        var result = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.False(result.Completed);
        Assert.True(Assert.Single(result.Failures).OutOfDiskSpace);
        Assert.Empty(fixture.FolderContents());
    }

    [Fact]
    public async Task A_copied_install_leaves_no_staging_file_behind()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", _content);

        Assert.True((await fixture.ExecuteAsync(await fixture.PlanAsync())).Completed);

        Assert.Equal(["fs25_a.zip"], fixture.FolderContents());
        Assert.Equal(_content, fixture.ReadInstalled("fs25_a.zip"));
    }


    private static ModSyncPlan Hardlinked(ModSyncPlan plan)
        => plan with { Materialization = new ModMaterialization(MaterializationMethod.Hardlink, FellBackToCopy: false) };

    /// <summary>The volume the fixture's mod folder and serving store share.</summary>
    private static string VolumeOf(SyncFixture fixture) => FileSystemHelper.NormalizeVolumeRoot(fixture.Folder.Path);
}
