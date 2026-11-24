using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Storage;
using Npgsql;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// Whole maintenance runs against a real database and storage in memory. What must hold: nothing is
/// deleted that the database did not queue, that is still referred to, that changed since it was read,
/// or while the storage is not provably this database's and able to give a delete back.
/// </summary>
public class StorageMaintenanceTests(StorageDatabaseFixture fixture) : IClassFixture<StorageDatabaseFixture>, IAsyncLifetime
{
    private static readonly StorageMaintenanceSettings _settings = new(TimeSpan.FromDays(1), 14, 30);

    private readonly FakeBlobContainers _blobs = new();
    private DateTime _now = new(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc);


    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;


    [Fact]
    public async Task Empty_storage_is_bound_to_a_database_with_no_binding()
    {
        var outcome = await RunAsync();

        Assert.Empty(outcome.Refusals);

        var binding = await BindingAsync();
        Assert.NotNull(binding);
        Assert.Equal(_blobs.StorageAccount, binding.StorageAccount);
        Assert.All(StorageContainerNames.All, x => Assert.Equal(binding.Marker, _blobs.MarkerOf(x)));
    }

    [Fact]
    public async Task Storage_that_holds_blobs_is_never_bound_by_a_run()
    {
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, name, lastModified: _now.AddDays(-30));
        await QueueAsync(StorageContainer.Mods, name);

        var outcome = await RunAsync();

        Assert.NotEmpty(outcome.Refusals);
        Assert.Null(await BindingAsync());
        Assert.Empty(_blobs.Deleted);
        Assert.All(StorageContainerNames.All, x => Assert.Null(_blobs.MarkerOf(x)));
    }

    [Fact]
    public async Task A_queued_blob_nothing_refers_to_is_deleted_by_the_next_run()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, name, length: 42, lastModified: _now.AddDays(-2), metadata: new Dictionary<string, string> { ["sha256"] = "abc" });
        await QueueAsync(StorageContainer.Mods, name);

        var outcome = await RunAsync();

        Assert.Equal(1, outcome.Deleted);
        Assert.Equal([(StorageContainer.Mods, name)], _blobs.Deleted);

        using var dbContext = fixture.CreateDbContext();
        var deleted = await dbContext.DeletedBlobs.SingleAsync();
        Assert.Equal((name, 42L, BlobDeletionReason.UploadIssued, "abc"), (deleted.BlobName, deleted.SizeBytes, deleted.Reason, deleted.Metadata["sha256"]));
        Assert.Empty(await dbContext.BlobDeletionCandidates.ToListAsync());
    }

    [Fact]
    public async Task A_queued_blob_that_was_registered_is_dropped_and_kept()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, name, lastModified: _now.AddDays(-30));
        await QueueAsync(StorageContainer.Mods, name);
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1");

        await RunAsync();

        Assert.Empty(_blobs.Deleted);
        Assert.NotNull(_blobs.Find(StorageContainer.Mods, name));
        Assert.Empty(await CandidatesAsync());
    }

    [Fact]
    public async Task A_deleted_mod_version_has_its_file_deleted_by_the_next_run()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, name, lastModified: _now.AddDays(-30));
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1");

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.ModVersions.Where(x => x.RepoId == repoId).ExecuteDeleteAsync();
        }

        await RunAsync();

        Assert.Equal([(StorageContainer.Mods, name)], _blobs.Deleted);
    }

    [Fact]
    public async Task A_blob_shared_by_a_surviving_snapshot_is_kept()
    {
        await GivenBoundStorageAsync();
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var shared = TestHashes.Of("shared");
        var savegameId = await StorageTestData.GivenASavegameAsync(fixture.CreateDbContext, repoId, userId, shared, shared);
        var name = BlobNames.Savegame(repoId, savegameId, shared);
        _blobs.Put(StorageContainer.Savegames, name, lastModified: _now.AddDays(-30));

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.SavegameSnapshots.Where(x => x.SavegameId == savegameId && x.Number == new Domain.Savegames.SavegameSnapshotNumber(1)).ExecuteDeleteAsync();
        }

        await RunAsync();

        Assert.Empty(_blobs.Deleted);
        Assert.Empty(await CandidatesAsync());
    }

    [Fact]
    public async Task A_missing_blob_stops_every_delete()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "lost", "1");
        var garbage = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, garbage, lastModified: _now.AddDays(-30));
        await QueueAsync(StorageContainer.Mods, garbage, admin: true);

        var outcome = await RunAsync();

        Assert.Contains("1 blobs the database refers to are missing.", outcome.Refusals);
        Assert.Empty(_blobs.Deleted);

        using var dbContext = fixture.CreateDbContext();
        var finding = await dbContext.StorageAuditFindings.SingleAsync(x => x.Kind == StorageFindingKind.Missing);
        Assert.Equal(BlobNames.Mod(repoId, new ModId("lost"), new ModVersionId("1")), finding.BlobName);
        Assert.Contains("1 blobs the database refers to are missing.", (await dbContext.StorageMaintenanceStatuses.SingleAsync()).Refusals);
    }

    [Fact]
    public async Task Soft_delete_kept_too_short_stops_deletes_but_not_the_audit_or_labels()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1");
        var referenced = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, referenced, lastModified: _now.AddDays(-30));
        var garbage = BlobNames.Mod(repoId, new ModId("b_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, garbage, lastModified: _now.AddDays(-30));
        await QueueAsync(StorageContainer.Mods, garbage, admin: true);
        _blobs.SoftDeleteDays = 7;

        var outcome = await RunAsync();

        Assert.Single(outcome.Refusals);
        Assert.Empty(_blobs.Deleted);
        Assert.True(BlobLabel.IsPresentIn(_blobs.Find(StorageContainer.Mods, referenced)!.Metadata));

        using var dbContext = fixture.CreateDbContext();
        Assert.Equal(garbage, (await dbContext.StorageAuditFindings.SingleAsync(x => x.Kind == StorageFindingKind.Unreferenced)).BlobName);
    }

    [Fact]
    public async Task A_database_behind_the_markers_writes_nothing()
    {
        // A database restored from a backup taken before the last run.
        await GivenBoundStorageAsync();
        var binding = (await BindingAsync())!;
        foreach (var container in StorageContainerNames.All)
        {
            _blobs.SetMarkerText(container, new ContainerMarker(binding.BindingId, binding.Generation + 5).Format());
        }

        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1");
        var referenced = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, referenced, lastModified: _now.AddDays(-30));
        var garbage = BlobNames.Mod(repoId, new ModId("b_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, garbage, lastModified: _now.AddDays(-30));
        await QueueAsync(StorageContainer.Mods, garbage, admin: true);

        var outcome = await RunAsync();

        Assert.Equal(3, outcome.Refusals.Count);
        Assert.Empty(_blobs.Deleted);
        Assert.False(BlobLabel.IsPresentIn(_blobs.Find(StorageContainer.Mods, referenced)!.Metadata));
        Assert.Equal(binding.Generation + 5, _blobs.MarkerOf(StorageContainer.Mods)!.Generation);
    }

    [Fact]
    public async Task Markers_from_before_generations_are_upgraded_and_the_account_recorded()
    {
        var bindingId = Guid.NewGuid();

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlAsync($"""
                INSERT INTO "StorageBindings" ("Id", "BindingId", "Created", "Generation") VALUES (1, {bindingId}, {_now}, 0)
                """);
        }

        foreach (var container in StorageContainerNames.All)
        {
            _blobs.SetMarkerText(container, bindingId.ToString());
        }

        var outcome = await RunAsync();

        Assert.Empty(outcome.Refusals);

        var binding = (await BindingAsync())!;
        Assert.Equal(_blobs.StorageAccount, binding.StorageAccount);
        Assert.All(StorageContainerNames.All, x => Assert.Equal(binding.Marker, _blobs.MarkerOf(x)));
    }

    [Fact]
    public async Task A_blob_written_after_it_was_read_is_kept()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, name, lastModified: _now.AddDays(-30));
        await QueueAsync(StorageContainer.Mods, name);

        // A registration claiming the blob between the run's read and its delete.
        _blobs.BeforeDelete = (container, blobName) => _blobs.Touch(container, blobName);
        var outcome = await RunAsync();

        Assert.Equal(0, outcome.Deleted);
        Assert.NotNull(_blobs.Find(StorageContainer.Mods, name));
        Assert.Single(await CandidatesAsync());
    }

    [Fact]
    public async Task An_admin_choice_is_deleted_at_the_next_run_only_while_it_is_unchanged()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var unchanged = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        var changed = BlobNames.Mod(repoId, new ModId("b_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, unchanged, lastModified: _now.AddDays(-30));
        _blobs.Put(StorageContainer.Mods, changed, lastModified: _now.AddDays(-30));

        await RunAsync();
        var findings = await FindingsAsync(StorageFindingKind.Unreferenced);

        using (var dbContext = fixture.CreateDbContext())
        {
            var maintenance = Maintenance(dbContext);
            var queued = await maintenance.QueueForDeletionAsync([.. findings.Select(x => x.Id)], "anton", CancellationToken.None);
            Assert.Equal(2, queued.Count);
        }

        _now = _now.AddDays(2);
        _blobs.Now = _now.AddDays(-1);
        _blobs.Touch(StorageContainer.Mods, changed);

        await RunAsync();

        Assert.Equal([(StorageContainer.Mods, unchanged)], _blobs.Deleted);
        Assert.NotNull(_blobs.Find(StorageContainer.Mods, changed));
        Assert.Empty(await CandidatesAsync());

        using var verification = fixture.CreateDbContext();
        Assert.Equal("anton", (await verification.DeletedBlobs.SingleAsync()).RequestedBy);
    }

    [Fact]
    public async Task Only_unreferenced_findings_can_be_chosen_for_deletion()
    {
        await GivenBoundStorageAsync();
        _blobs.Put(StorageContainer.Mods, "stray.txt", lastModified: _now.AddDays(-30));
        await RunAsync();

        var findings = await FindingsAsync(StorageFindingKind.Unrecognised);

        using var dbContext = fixture.CreateDbContext();
        var queued = await Maintenance(dbContext).QueueForDeletionAsync([.. findings.Select(x => x.Id)], "anton", CancellationToken.None);

        Assert.Equal(0, queued.Count);
        Assert.Empty(await CandidatesAsync());
    }

    [Fact]
    public async Task Referenced_blobs_with_no_label_are_labelled_from_the_database()
    {
        await GivenBoundStorageAsync();
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var hash = TestHashes.Of("save");
        var savegameId = await StorageTestData.GivenASavegameAsync(fixture.CreateDbContext, repoId, userId, hash);
        var name = BlobNames.Savegame(repoId, savegameId, hash);
        _blobs.Put(StorageContainer.Savegames, name, lastModified: _now.AddDays(-30), metadata: new Dictionary<string, string> { ["sha256"] = hash });

        await RunAsync();

        var read = BlobLabel.Read(_blobs.Find(StorageContainer.Savegames, name)!.Metadata).ToDictionary();
        Assert.Equal(("Åsa", userId.Value, hash), (read[BlobLabel.AddedByNameKey], read[BlobLabel.AddedByKey], read["sha256"]));
        Assert.StartsWith("Höst", read[BlobLabel.SavegameNameKey]);
    }

    [Fact]
    public async Task A_run_while_another_holds_the_lock_does_nothing()
    {
        await GivenBoundStorageAsync();

        await using var other = UnpooledConnection();
        await other.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(2, 0)", other))
        {
            await command.ExecuteNonQueryAsync();
        }

        var generation = (await BindingAsync())!.Generation;
        var outcome = await RunAsync();

        Assert.True(outcome.WasBusy);
        Assert.Equal(generation, (await BindingAsync())!.Generation);
    }

    [Fact]
    public async Task A_run_that_dies_between_the_phases_of_an_advance_is_finished_by_the_next()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, name, lastModified: _now.AddDays(-30));
        await QueueAsync(StorageContainer.Mods, name, admin: true);

        _blobs.RefuseMarkerWrite = x => x == StorageContainer.Images;
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync());

        Assert.NotNull((await BindingAsync())!.PendingGeneration);
        Assert.Empty(_blobs.Deleted);

        _blobs.RefuseMarkerWrite = _ => false;
        var outcome = await RunAsync();

        Assert.Empty(outcome.Refusals);
        Assert.Equal([(StorageContainer.Mods, name)], _blobs.Deleted);
        var binding = (await BindingAsync())!;
        Assert.Null(binding.PendingGeneration);
        Assert.All(StorageContainerNames.All, x => Assert.Equal(binding.Marker, _blobs.MarkerOf(x)));
    }

    [Fact]
    public async Task A_cancelled_run_leaves_the_rest_for_the_next()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var names = Enumerable.Range(1, 3).Select(x => BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId(x.ToString()))).ToList();
        foreach (var name in names)
        {
            _blobs.Put(StorageContainer.Mods, name, lastModified: _now.AddDays(-30));
            await QueueAsync(StorageContainer.Mods, name, admin: true);
        }

        using var cancellation = new CancellationTokenSource();
        _blobs.BeforeDelete = (_, _) =>
        {
            if (_blobs.Deleted.Count == 1)
            {
                cancellation.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(cancellation.Token));
        Assert.True(_blobs.Deleted.Count < names.Count);

        _blobs.BeforeDelete = null;
        await RunAsync();

        Assert.Equal(names.Order(StringComparer.Ordinal), _blobs.Deleted.Select(x => x.Name));
        Assert.Empty(await CandidatesAsync());

        using var dbContext = fixture.CreateDbContext();
        Assert.Equal(names.Count, await dbContext.DeletedBlobs.CountAsync());
    }

    [Fact]
    public async Task Binding_by_hand_takes_over_storage_another_database_marked()
    {
        foreach (var container in StorageContainerNames.All)
        {
            _blobs.SetMarkerText(container, new ContainerMarker(Guid.NewGuid(), 12).Format());
        }

        Assert.NotEmpty((await RunAsync()).Refusals);

        using (var dbContext = fixture.CreateDbContext())
        {
            Assert.Empty(await Maintenance(dbContext).BindByHandAsync("anton", CancellationToken.None));
        }

        var binding = (await BindingAsync())!;
        Assert.Equal(13, binding.Generation);
        Assert.Empty((await RunAsync()).Refusals);
    }


    [Fact]
    public async Task A_blob_queued_again_while_a_run_looks_at_it_is_kept()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, name, lastModified: _now.AddDays(-30));
        await QueueAsync(StorageContainer.Mods, name);

        _blobs.AfterGet = async (container, blobName) =>
        {
            _blobs.AfterGet = null;
            await QueueAsync(container, blobName);
        };
        await RunAsync();

        Assert.Empty(_blobs.Deleted);
        Assert.Equal(1, Assert.Single(await CandidatesAsync()).Requeued);
    }

    [Fact]
    public async Task An_upload_whose_blob_is_not_there_yet_stays_queued_and_is_collected_once_it_lands()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        await QueueAsync(StorageContainer.Mods, name);
        _now = Assert.Single(await CandidatesAsync()).QueuedAt.AddHours(1);

        await RunAsync();
        Assert.Single(await CandidatesAsync());

        _blobs.Put(StorageContainer.Mods, name, lastModified: _now);
        _now = _now.AddDays(2);
        var outcome = await RunAsync();

        Assert.Equal(1, outcome.Deleted);
        Assert.Equal([(StorageContainer.Mods, name)], _blobs.Deleted);
    }

    [Fact]
    public async Task An_upload_whose_blob_never_came_is_dropped_after_the_minimum_age()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        await QueueAsync(StorageContainer.Mods, BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1")));
        _now = Assert.Single(await CandidatesAsync()).QueuedAt.AddDays(1);

        await RunAsync();

        Assert.Empty(await CandidatesAsync());
    }

    [Fact]
    public async Task A_removed_reference_queued_again_while_its_drop_is_decided_stays_queued()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1");

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.ModVersions.Where(x => x.RepoId == repoId).ExecuteDeleteAsync();
        }

        // There is no blob, so the run drops the row it read; the row queued again in between stays.
        _blobs.AfterGet = async (container, blobName) =>
        {
            _blobs.AfterGet = null;
            await QueueAsync(container, blobName);
        };
        await RunAsync();

        Assert.Equal(1, Assert.Single(await CandidatesAsync()).Requeued);
    }

    [Fact]
    public async Task A_delete_whose_answer_was_lost_is_still_recorded()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        _blobs.Put(StorageContainer.Mods, name, lastModified: _now.AddDays(-30));
        await QueueAsync(StorageContainer.Mods, name, admin: true);
        _blobs.LoseDeleteAnswers = true;

        var outcome = await RunAsync();

        Assert.Equal(1, outcome.Deleted);
        Assert.Empty(await CandidatesAsync());

        using var dbContext = fixture.CreateDbContext();
        Assert.Equal(name, (await dbContext.DeletedBlobs.SingleAsync()).BlobName);
    }

    [Fact]
    public async Task A_run_that_fails_partway_records_each_delete_when_it_happened_and_counts_them()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var names = Enumerable.Range(1, 3).Select(x => BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId(x.ToString()))).Order(StringComparer.Ordinal).ToList();
        foreach (var name in names)
        {
            _blobs.Put(StorageContainer.Mods, name, lastModified: _now.AddDays(-30));
            await QueueAsync(StorageContainer.Mods, name, admin: true);
        }

        var started = _now;
        _blobs.BeforeDelete = (_, _) =>
        {
            _now = _now.AddMinutes(1);

            if (_blobs.Deleted.Count == 2)
            {
                throw new InvalidOperationException("Storage broke.");
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync());

        using var dbContext = fixture.CreateDbContext();
        Assert.Equal(2, (await dbContext.StorageMaintenanceStatuses.SingleAsync()).DeletedInLastRun);
        Assert.Equal(
            [(names[0], started.AddMinutes(1)), (names[1], started.AddMinutes(2))],
            (await dbContext.DeletedBlobs.OrderBy(x => x.BlobName).ToListAsync()).Select(x => (x.BlobName, x.Deleted)));
    }

    [Fact]
    public async Task Deleted_blobs_are_forgotten_once_storage_no_longer_keeps_them()
    {
        await GivenBoundStorageAsync();

        using (var dbContext = fixture.CreateDbContext())
        {
            var candidate = new BlobDeletionCandidate(StorageContainer.Images, "ab/forgotten", BlobDeletionReason.ReferenceRemoved);
            dbContext.DeletedBlobs.Add(new DeletedBlob(candidate, new StoredBlob("ab/forgotten", _now, 1, "v1", new Dictionary<string, string>()), _now.AddDays(-46)));
            dbContext.DeletedBlobs.Add(new DeletedBlob(candidate, new StoredBlob("ab/kept", _now, 1, "v1", new Dictionary<string, string>()), _now.AddDays(-44)));
            await dbContext.SaveChangesAsync();
        }

        _blobs.SoftDeleteDays = null;
        await RunAsync();

        using (var dbContext = fixture.CreateDbContext())
        {
            Assert.Equal(2, await dbContext.DeletedBlobs.CountAsync());
        }

        _blobs.SoftDeleteDays = 45;
        await RunAsync();

        using (var dbContext = fixture.CreateDbContext())
        {
            Assert.Equal("ab/kept", (await dbContext.DeletedBlobs.SingleAsync()).BlobName);
        }
    }

    [Fact]
    public async Task The_file_of_a_deleted_mod_version_is_deleted_at_once_for_a_new_upload()
    {
        await GivenBoundStorageAsync();
        var (repoId, name) = await GivenADeletedModVersionAsync();
        var generation = (await BindingAsync())!.Generation;

        Assert.Equal(ReleasedModFileDeletion.Deleted, await DeleteReleasedAsync(repoId));

        Assert.Equal([(StorageContainer.Mods, name)], _blobs.Deleted);
        Assert.Empty(await CandidatesAsync());
        Assert.Equal(generation + 2, (await BindingAsync())!.Generation);

        using var dbContext = fixture.CreateDbContext();
        Assert.Equal(name, (await dbContext.DeletedBlobs.SingleAsync()).BlobName);
    }

    [Fact]
    public async Task A_file_a_failed_upload_left_behind_is_not_deleted_at_once()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        await QueueAsync(StorageContainer.Mods, name);
        _blobs.Put(StorageContainer.Mods, name, lastModified: _now.AddDays(-30));

        Assert.Equal(ReleasedModFileDeletion.NotReleased, await DeleteReleasedAsync(repoId));
        Assert.Empty(_blobs.Deleted);
    }

    [Fact]
    public async Task The_file_of_a_deleted_mod_version_stays_while_a_run_would_refuse()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await GivenADeletedModVersionAsync();
        _blobs.SoftDeleteDays = 7;

        Assert.Equal(ReleasedModFileDeletion.Refused, await DeleteReleasedAsync(repoId));
        Assert.Empty(_blobs.Deleted);
        Assert.Single(await CandidatesAsync());
    }

    [Fact]
    public async Task The_file_of_a_deleted_mod_version_stays_while_the_last_run_was_refused()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await GivenADeletedModVersionAsync();
        _blobs.SoftDeleteDays = 7;
        await RunAsync();
        _blobs.SoftDeleteDays = 31;

        Assert.Equal(ReleasedModFileDeletion.Refused, await DeleteReleasedAsync(repoId));
        Assert.Empty(_blobs.Deleted);
    }

    [Fact]
    public async Task The_file_of_a_deleted_mod_version_stays_while_a_run_is_in_progress()
    {
        await GivenBoundStorageAsync();
        var (repoId, _) = await GivenADeletedModVersionAsync();

        await using var other = UnpooledConnection();
        await other.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(2, 0)", other))
        {
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal(ReleasedModFileDeletion.Refused, await DeleteReleasedAsync(repoId));
        Assert.Empty(_blobs.Deleted);
    }

    [Fact]
    public async Task The_file_of_a_deleted_mod_version_that_is_already_gone_counts_as_deleted()
    {
        await GivenBoundStorageAsync();
        var (repoId, name) = await GivenADeletedModVersionAsync();
        _blobs.Remove(StorageContainer.Mods, name);

        Assert.Equal(ReleasedModFileDeletion.Deleted, await DeleteReleasedAsync(repoId));
        Assert.Empty(await CandidatesAsync());
    }

    [Fact]
    public async Task Deleting_the_file_of_a_deleted_mod_version_waits_for_a_registration_holding_its_lock()
    {
        await GivenBoundStorageAsync();
        var (repoId, name) = await GivenADeletedModVersionAsync();

        await using var registration = UnpooledConnection();
        await registration.OpenAsync();
        Assert.True(await TryBlobLockAsync(registration, $"{StorageContainer.Mods}/{name}"));

        var deletion = DeleteReleasedAsync(repoId);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(deletion.IsCompleted);

        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1");
        await UnlockBlobAsync(registration, $"{StorageContainer.Mods}/{name}");

        Assert.Equal(ReleasedModFileDeletion.Registered, await deletion);
        Assert.Empty(_blobs.Deleted);
        Assert.Empty(await CandidatesAsync());
    }

    [Fact]
    public async Task A_blob_lock_is_held_until_it_is_released()
    {
        const string name = "repo/mod/1";
        var key = $"{StorageContainer.Mods}/{name}";

        await using var other = UnpooledConnection();
        await other.OpenAsync();

        using (var dbContext = fixture.CreateDbContext())
        {
            await using (await dbContext.LockBlobAsync(StorageContainer.Mods, name, CancellationToken.None))
            {
                Assert.False(await TryBlobLockAsync(other, key));
            }
        }

        Assert.True(await TryBlobLockAsync(other, key));
    }


    private async Task GivenBoundStorageAsync()
    {
        var outcome = await RunAsync();
        Assert.Empty(outcome.Refusals);
    }

    private async Task<StorageMaintenanceOutcome> RunAsync(CancellationToken cancellationToken = default)
    {
        using var dbContext = fixture.CreateDbContext();

        return await Maintenance(dbContext).RunAsync(_settings, cancellationToken);
    }

    private StorageMaintenance Maintenance(ApplicationDbContext dbContext)
    {
        var time = new FixedTime(() => _now);

        return new StorageMaintenance(
            dbContext,
            new StorageGatekeeper(dbContext, _blobs, time, NullLogger<StorageGatekeeper>.Instance),
            new StorageAuditor(dbContext, _blobs, NullLogger<StorageAuditor>.Instance),
            new BlobDeleter(dbContext, _blobs, time, NullLogger<BlobDeleter>.Instance),
            time,
            NullLogger<StorageMaintenance>.Instance);
    }

    private async Task QueueAsync(StorageContainer container, string name, bool admin = false)
    {
        using var dbContext = fixture.CreateDbContext();

        if (admin)
        {
            var version = _blobs.Find(container, name)!.Version!;
            await dbContext.QueueAdminAsync(container, name, version, "anton", CancellationToken.None);
        }
        else
        {
            await dbContext.QueueUploadAsync(container, name, BlobNames.OwningRepo(container, name), new UserId("uploader"), CancellationToken.None);
        }
    }

    private async Task<(RepoId RepoId, string Name)> GivenADeletedModVersionAsync()
    {
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1"));
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1");

        // Written this instant, which only deleting at once ignores.
        _blobs.Put(StorageContainer.Mods, name, lastModified: _now);

        using var dbContext = fixture.CreateDbContext();
        await dbContext.ModVersions.Where(x => x.RepoId == repoId).ExecuteDeleteAsync();

        return (repoId, name);
    }

    private async Task<ReleasedModFileDeletion> DeleteReleasedAsync(RepoId repoId)
    {
        using var dbContext = fixture.CreateDbContext();

        return await Maintenance(dbContext).DeleteReleasedModFileAsync(
            repoId, new ModId("a_mod"), new ModVersionId("1"), _settings.MinimumSoftDeleteDays, CancellationToken.None);
    }

    private static async Task<bool> TryBlobLockAsync(NpgsqlConnection connection, string key)
    {
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(3, hashtext(@key))", connection);
        command.Parameters.AddWithValue("key", key);

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>A session of its own, whose locks end with it: a pooled one keeps them until it is reused.</summary>
    private NpgsqlConnection UnpooledConnection()
    {
        return new NpgsqlConnection(new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Pooling = false }.ConnectionString);
    }

    private static async Task UnlockBlobAsync(NpgsqlConnection connection, string key)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(3, hashtext(@key))", connection);
        command.Parameters.AddWithValue("key", key);

        await command.ExecuteScalarAsync();
    }

    private async Task<StorageBinding?> BindingAsync()
    {
        using var dbContext = fixture.CreateDbContext();

        return await dbContext.StorageBindings.AsNoTracking().SingleOrDefaultAsync();
    }

    private async Task<List<BlobDeletionCandidate>> CandidatesAsync()
    {
        using var dbContext = fixture.CreateDbContext();

        return await dbContext.BlobDeletionCandidates.AsNoTracking().ToListAsync();
    }

    private async Task<List<StorageAuditFinding>> FindingsAsync(StorageFindingKind kind)
    {
        using var dbContext = fixture.CreateDbContext();

        return await dbContext.StorageAuditFindings.AsNoTracking().Where(x => x.Kind == kind).ToListAsync();
    }


    private sealed class FixedTime(Func<DateTime> now) : ITimeService
    {
        public DateTime Now() => now();
    }
}
