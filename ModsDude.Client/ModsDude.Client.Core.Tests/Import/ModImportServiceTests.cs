using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using System.Text;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Import;

public class ModImportServiceTests
{
    private static readonly ModSource _downloads =
        new(ModSourceId.Downloads, "Downloads", @"C:\Downloads", ModSourceKind.Downloads);

    private static readonly ModSource _instance =
        new(ModSourceId.ForInstance(Guid.NewGuid()), "FS25", @"C:\FS25\mods", ModSourceKind.Instance);

    private readonly FakeModsDudeServer _server = new();
    private readonly RecordingModImagePublisher _imagery = new();
    private readonly FakeModFileUploader _uploader;
    private readonly ModImportService _service;


    public ModImportServiceTests()
    {
        _uploader = new FakeModFileUploader(_server);
        _service = new ModImportService(_server, _server, _uploader, _imagery, NullLogger<ModImportService>.Instance);
    }


    [Fact]
    public async Task A_mod_is_linked_uploaded_and_only_then_registered()
    {
        var result = await ImportAsync([Local("FS25_Plough", "1.0")]);

        Assert.Equal(
            [ServerCallKind.Link, ServerCallKind.Upload, ServerCallKind.Register],
            _server.Journal.Where(x => x.Kind is not ServerCallKind.GetMods).Select(x => x.Kind));

        Assert.Equal(ModImportStatus.Registered, Assert.Single(result.Items).Status);
    }

    [Fact]
    public async Task The_file_is_in_storage_before_every_registration_even_with_many_mods_at_once()
    {
        var versions = Enumerable.Range(0, 12).Select(x => Local($"FS25_Mod{x}", "1.0")).ToList();

        var result = await ImportAsync(versions);

        Assert.All(result.Items, x => Assert.Equal(ModImportStatus.Registered, x.Status));

        // Concurrency across mods is what must not weaken the invariant, so it is asserted per mod
        // rather than over the journal as a whole.
        foreach (var version in versions)
        {
            var journal = _server.Journal;

            var upload = journal.ToList().FindIndex(x => x.Kind is ServerCallKind.Upload && x.Identity == version.Identity);
            var register = journal.ToList().FindIndex(x => x.Kind is ServerCallKind.Register && x.Identity == version.Identity);

            Assert.True(upload >= 0, $"{version.ModId} was registered without ever being uploaded");
            Assert.True(upload < register, $"{version.ModId} was registered before its file was stored");
        }
    }

    [Fact]
    public async Task Several_mods_import_at_once_but_never_more_than_asked_for()
    {
        var versions = Enumerable.Range(0, 12).Select(x => Local($"FS25_Mod{x}", "1.0")).ToList();

        await ImportAsync(versions, x => x with { MaxConcurrentMods = 3 });

        Assert.True(_uploader.PeakConcurrency > 1, "nothing ran concurrently, so the bound proves nothing");
        Assert.True(_uploader.PeakConcurrency <= 3, $"{_uploader.PeakConcurrency} mods were in flight at once");
    }


    [Fact]
    public async Task Versions_of_one_mod_register_one_at_a_time_and_oldest_first()
    {
        var versions = new[]
        {
            Local("FS25_Plough", "1.2"),
            Local("FS25_Plough", "1.0"),
            Local("FS25_Plough", "1.1")
        };

        await ImportAsync(versions);

        Assert.Equal(Vs("1.0", "1.1", "1.2"), _server.CallsOf(ServerCallKind.Register).Select(x => x.VersionId));
        Assert.Equal(Vs("1.0", "1.1", "1.2"), _server.VersionsOf(Mod("FS25_Plough")));
    }

    /// <summary>
    /// The worked example from docs/09: v1 and v4 registered, v2 in an instance's mod folder and v3
    /// in Downloads. Each insert names the version it goes before, and v4's position moves.
    /// </summary>
    [Fact]
    public async Task Two_new_versions_of_one_mod_are_placed_against_the_final_intended_order()
    {
        _server.Seed(Mod("FS25_Plough"), "1.0", "4.0");

        await ImportAsync(
        [
            Local("FS25_Plough", "2.0", source: _instance),
            Local("FS25_Plough", "3.0", source: _downloads)
        ]);

        Assert.Equal(Vs("2.0", "3.0"), _server.CallsOf(ServerCallKind.Register).Select(x => x.VersionId));
        Assert.Equal(Vs("1.0", "2.0", "3.0", "4.0"), _server.VersionsOf(Mod("FS25_Plough")));
    }


    [Fact]
    public async Task A_version_the_repo_already_has_is_a_success_that_uploads_nothing()
    {
        _server.Seed(Mod("FS25_Plough"), "1.0");

        var result = await ImportAsync([Local("FS25_Plough", "1.0")]);

        Assert.Equal(ModImportStatus.AlreadyRegistered, Assert.Single(result.Items).Status);
        Assert.Empty(_server.CallsOf(ServerCallKind.Upload));
        Assert.Empty(_server.CallsOf(ServerCallKind.Register));
    }

    [Fact]
    public async Task An_orphaned_file_matching_ours_is_adopted_without_uploading_it_again()
    {
        var version = Local("FS25_Plough", "1.0");

        // What a run that uploaded and then died leaves behind: no upload link can be minted for
        // this address again, so the only way to finish it is to register against what is there.
        _server.PlaceOrphan(version.Identity, await HashOf(version));

        var result = await ImportAsync([version]);

        Assert.Equal(ModImportStatus.Registered, Assert.Single(result.Items).Status);
        Assert.Empty(_server.CallsOf(ServerCallKind.Upload));
        Assert.Equal(Vs("1.0"), _server.VersionsOf(Mod("FS25_Plough")));
    }

    [Fact]
    public async Task An_orphaned_file_that_is_not_ours_is_reported_and_nothing_is_registered()
    {
        var version = Local("FS25_Plough", "1.0", content: "our build");

        _server.PlaceOrphan(version.Identity, await HashOf(Local("FS25_Plough", "1.0", content: "somebody else's build")));

        var result = await ImportAsync([version]);

        // Registering here would record a hash describing bytes nobody can download, and the blob
        // exists so no upload link could ever be minted to repair it.
        Assert.Equal(ModImportStatus.ContentMismatch, Assert.Single(result.Items).Status);
        Assert.Empty(_server.CallsOf(ServerCallKind.Register));
        Assert.Empty(_server.VersionsOf(Mod("FS25_Plough")));
    }

    [Fact]
    public async Task A_stored_file_whose_contents_nobody_recorded_is_not_registered_against()
    {
        var version = Local("FS25_Plough", "1.0");

        _server.PlaceOrphan(version.Identity, null);

        var result = await ImportAsync([version]);

        Assert.Equal(ModImportStatus.ContentMismatch, Assert.Single(result.Items).Status);
        Assert.Empty(_server.CallsOf(ServerCallKind.Register));
    }

    [Fact]
    public async Task Importing_the_same_selection_twice_does_nothing_the_second_time()
    {
        // The same rows, because a catalog snapshot taken before the first run still says these
        // versions are local-only.
        var versions = new[] { Local("FS25_Plough", "1.0"), Local("FS25_Trailer", "2.1") };

        await ImportAsync(versions);

        var registrations = _server.CallsOf(ServerCallKind.Register).Count;

        var second = await ImportAsync(versions);

        Assert.All(second.Items, x => Assert.Equal(ModImportStatus.AlreadyRegistered, x.Status));
        Assert.Equal(registrations, _server.CallsOf(ServerCallKind.Register).Count);
        Assert.Equal(2, _server.CallsOf(ServerCallKind.Upload).Count);
    }


    [Fact]
    public async Task A_placement_that_lost_its_race_is_recomputed_against_the_new_order_and_retried()
    {
        _server.Seed(Mod("FS25_Plough"), "1.0", "4.0");

        var landed = false;

        _server.BeforeRegister = identity =>
        {
            if (landed is false && identity.VersionId == V("2.0"))
            {
                // Somebody else inserts between the two neighbours this import named, so 1.0 and
                // 4.0 are no longer adjacent and the placement is refused.
                landed = true;
                _server.RegisterElsewhere(new ModVersionIdentity(Mod("FS25_Plough"), V("3.0")), V("1.0"), V("4.0"));
            }

            return Task.CompletedTask;
        };

        var result = await ImportAsync([Local("FS25_Plough", "2.0")]);

        Assert.Equal(ModImportStatus.Registered, Assert.Single(result.Items).Status);
        Assert.Equal(Vs("1.0", "2.0", "3.0", "4.0"), _server.VersionsOf(Mod("FS25_Plough")));

        // Refetched rather than retried blind: the second attempt asserts different neighbours.
        Assert.Equal(2, _server.CallsOf(ServerCallKind.Register).Count);
        Assert.True(_server.Journal.Count(x => x.Kind is ServerCallKind.GetMods) >= 2);
    }

    [Fact]
    public async Task A_placement_that_keeps_losing_is_reported_rather_than_retried_forever()
    {
        _server.Seed(Mod("FS25_Plough"), "1.0", "4.0");

        var inserted = 0;

        _server.BeforeRegister = identity =>
        {
            if (identity.VersionId == V("2.0"))
            {
                // Always straight after 1.0, so every recomputed placement loses its race too.
                inserted++;
                _server.RegisterElsewhere(
                    new ModVersionIdentity(Mod("FS25_Plough"), V($"3.{inserted}")),
                    V("1.0"),
                    _server.VersionsOf(Mod("FS25_Plough"))[1]);
            }

            return Task.CompletedTask;
        };

        var result = await ImportAsync([Local("FS25_Plough", "2.0")], x => x with { MaxPlacementRetries = 2 });

        Assert.Equal(ModImportStatus.Failed, Assert.Single(result.Items).Status);
        Assert.Equal(3, _server.CallsOf(ServerCallKind.Register).Count);
    }


    [Fact]
    public async Task One_mod_failing_does_not_take_the_rest_of_the_batch_with_it()
    {
        _uploader.OnUpload = link => link.Contains("fs25_broken")
            ? throw new IOException("The file is in use by another process.")
            : Task.CompletedTask;

        var result = await ImportAsync(
        [
            Local("FS25_Plough", "1.0"),
            Local("FS25_Broken", "1.0"),
            Local("FS25_Trailer", "1.0")
        ]);

        Assert.Equal(2, result.Succeeded.Count);

        var failure = Assert.Single(result.Unfinished);
        Assert.Equal(ModImportStatus.Failed, failure.Status);
        Assert.Equal(Mod("FS25_Broken"), failure.Identity.ModId);
        Assert.IsType<IOException>(failure.Exception);
    }

    [Fact]
    public async Task A_version_whose_predecessor_failed_is_placed_where_that_leaves_it()
    {
        _uploader.OnUpload = link => link.EndsWith("/1.0")
            ? throw new IOException("The file is in use by another process.")
            : Task.CompletedTask;

        var result = await ImportAsync([Local("FS25_Plough", "1.0"), Local("FS25_Plough", "1.1")]);

        // 1.1 was going to be placed after 1.0, which never landed. The assertion catches that and
        // the recomputed placement does not mention it.
        Assert.Equal(Vs("1.1"), _server.VersionsOf(Mod("FS25_Plough")));
        Assert.Equal(ModImportStatus.Registered, Assert.Single(result.Succeeded).Status);
        Assert.Equal(ModImportStatus.Failed, Assert.Single(result.Unfinished).Status);
    }

    [Fact]
    public async Task A_version_two_sources_disagree_about_is_refused_rather_than_guessed_at()
    {
        var conflicted = Local("FS25_Plough", "1.0") with
        {
            FoundIn =
            [
                Occurrence(_downloads, "one build"),
                Occurrence(_instance, "a different build of the same version")
            ]
        };

        var result = await ImportAsync([conflicted, Local("FS25_Trailer", "1.0")]);

        Assert.Equal(ModImportStatus.SourceConflict, Assert.Single(result.Unfinished).Status);
        Assert.DoesNotContain(_server.CallsOf(ServerCallKind.Link), x => x.ModId == Mod("FS25_Plough"));
        Assert.Single(result.Succeeded);
    }

    [Fact]
    public async Task Cancelling_stops_the_import_before_anything_else_registers()
    {
        using var cancellation = new CancellationTokenSource();

        _uploader.OnUpload = _ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        var versions = Enumerable.Range(0, 5).Select(x => Local($"FS25_Mod{x}", "1.0")).ToList();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ImportAsync(versions, x => x with { MaxConcurrentMods = 1 }, cancellation.Token));

        Assert.Empty(_server.CallsOf(ServerCallKind.Register));
    }


    [Fact]
    public async Task Imagery_is_published_only_once_the_version_is_registered()
    {
        _imagery.OnPublish = (identity, mod) =>
        {
            Assert.Contains(identity.VersionId, _server.VersionsOf(identity.ModId));
            Assert.Equal(@"C:\Downloads\FS25_Plough.zip", mod.FilePath);

            return Task.CompletedTask;
        };

        await ImportAsync([Local("FS25_Plough", "1.0")]);

        Assert.Single(_imagery.Published);
    }

    [Fact]
    public async Task Imagery_that_will_not_publish_does_not_fail_the_version()
    {
        _imagery.OnPublish = (_, _) => throw new HttpRequestException("The image upload timed out.");

        var result = await ImportAsync([Local("FS25_Plough", "1.0")]);

        var item = Assert.Single(result.Items);
        Assert.Equal(ModImportStatus.Registered, item.Status);
        Assert.Contains("Imagery", item.Message);
        Assert.Equal(Vs("1.0"), _server.VersionsOf(Mod("FS25_Plough")));
    }


    [Fact]
    public async Task A_mod_needing_arbitration_is_skipped_without_holding_up_the_others()
    {
        _server.Seed(Mod("FS25_Awkward"), "1.0");

        var result = await ImportAsync(
        [
            Local("FS25_Plough", "1.0"),
            Local("FS25_Awkward", "v1")
        ]);

        Assert.Equal(ModImportStatus.Registered, Assert.Single(result.Succeeded).Status);
        Assert.Equal(ModImportStatus.NeedsArbitration, Assert.Single(result.Unfinished).Status);
        Assert.Equal(Vs("1.0"), _server.VersionsOf(Mod("FS25_Awkward")));
    }

    [Fact]
    public async Task An_arbitrated_order_places_the_versions_the_comparer_could_not()
    {
        _server.Seed(Mod("FS25_Awkward"), "1.0");

        var result = await ImportAsync(
            [Local("FS25_Awkward", "v1")],
            x => x with
            {
                ResolveArbitration = (items, _) => Task.FromResult<IReadOnlyDictionary<ModKey, IReadOnlyList<ModVersionKey>>?>(
                    items.ToDictionary(item => item.ModId, IReadOnlyList<ModVersionKey> (_) => Vs("v1", "1.0")))
            });

        Assert.Equal(ModImportStatus.Registered, Assert.Single(result.Items).Status);
        Assert.Equal(Vs("v1", "1.0"), _server.VersionsOf(Mod("FS25_Awkward")));
    }


    private Task<ModImportResult> ImportAsync(
        IReadOnlyList<CatalogModVersion> versions,
        Func<ModImportRequest, ModImportRequest>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ModImportRequest(_server.RepoId, versions, DefaultModVersionComparer.Instance);

        return _service.ImportAsync(configure?.Invoke(request) ?? request, cancellationToken);
    }

    private static async Task<string> HashOf(CatalogModVersion version)
    {
        using var content = version.OpenStream!();

        return await ModContentHasher.ComputeAsync(content, CancellationToken.None);
    }

    private static CatalogModVersion Local(string modId, string version, string content = "mod bytes", ModSource? source = null)
    {
        return new CatalogModVersion(Mod(modId), V(version), modId, "A mod.", IsLocal: true, IsOnServer: false, Locked: false)
        {
            FoundIn = [Occurrence(source ?? _downloads, $"{content} of {modId} {version}", modId)]
        };
    }

    private static ModOccurrence Occurrence(ModSource source, string content, string modId = "FS25_Plough")
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        return new ModOccurrence(source, Path.Combine(source.Path, $"{modId}.zip"), bytes.Length, () => new MemoryStream(bytes));
    }
}
