using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Mods;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.GameAdapters;
using ModsDude.Client.Core.Tests.RemoteSources;
using ModsDude.Client.Core.Tests.Stores;
using ModsDude.Client.Core.Tests.Sync;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Import;

/// <summary>Versions a remote source offers: downloaded as part of the import, and checked before anything is registered.</summary>
public class ModImportDownloadTests : IDisposable
{
    private readonly FakeModsDudeServer _server = new();
    private readonly FakeModFileUploader _uploader;
    private readonly TempDirectory _temp = new("import-download");
    private readonly FakeRemoteModDownloader _downloader;
    private readonly InMemoryRemoteDownloadNotes _notes = new();
    private readonly FakeRemoteModSource _source = new();
    private readonly ContentStore _store;
    private readonly ModImportService _service;


    public ModImportDownloadTests()
    {
        _uploader = new FakeModFileUploader(_server);
        _downloader = new FakeRemoteModDownloader(_temp);
        _store = new ContentStore(Path.GetPathRoot(_temp.Path)!, _temp.CreateSubdirectory("store"), long.MaxValue);
        _service = new ModImportService(
            _server,
            _server,
            new ModStore(_server, InlineStoreDispatcher.Instance, NullLogger<ModStore>.Instance),
            _uploader,
            new RecordingModImagePublisher(),
            new FakeRecycleBin(),
            new FakeStoreProvider(_store),
            _downloader,
            _notes,
            NullLogger<ModImportService>.Instance);
    }


    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }


    [Fact]
    public async Task A_download_that_holds_the_version_offered_is_imported_as_that_version()
    {
        var offer = Offer("FS25_Plough", "1.1");
        _downloader.Serve(offer.Download!.Version, "1.1");

        var result = await ImportAsync([offer]);

        Assert.Equal(ModImportStatus.Registered, Assert.Single(result.Items).Status);
        Assert.Equal(Vs("1.1"), _server.VersionsOf(Mod("FS25_Plough")));
    }

    [Fact]
    public async Task A_download_is_deleted_once_imported_and_the_run_leaves_nothing_behind()
    {
        var offer = Offer("FS25_Plough", "1.1");
        _downloader.Serve(offer.Download!.Version, "1.1");

        await ImportAsync([offer]);

        Assert.Single(_downloader.Discarded);
        Assert.Equal(1, _downloader.Runs);
        Assert.Equal(1, _downloader.RunsDisposed);
        Assert.Empty(Directory.EnumerateDirectories(_temp.Path, "run-*"));
    }

    [Fact]
    public async Task A_run_with_nothing_to_download_starts_no_download_run()
    {
        await ImportAsync([Local("FS25_Plough", "1.0")]);

        Assert.Equal(0, _downloader.Runs);
    }

    [Fact]
    public async Task Every_download_is_written_down_with_what_it_held()
    {
        var offer = Offer("FS25_Plough", "1.1");
        _downloader.Serve(offer.Download!.Version, "1.2");

        await ImportAsync([offer]);

        var note = Assert.Single(_notes.Notes);
        Assert.Equal(offer.Download.Version.DownloadUrl, note.Url);
        Assert.Equal(offer.Identity, note.Offered);
        Assert.Equal(new ModVersionIdentity(Mod("FS25_Plough"), V("1.2")), note.Held);
    }

    [Fact]
    public async Task Downloads_run_at_once_but_never_more_than_asked_for()
    {
        var offers = Enumerable.Range(0, 12).Select(x => Offer($"FS25_Mod{x}", "1.1")).ToList();

        foreach (var offer in offers)
        {
            _downloader.Serve(offer.Download!.Version, "1.1");
        }

        var result = await ImportAsync(offers, x => x with { MaxConcurrentDownloads = 3 });

        Assert.All(result.Items, x => Assert.Equal(ModImportStatus.Registered, x.Status));
        Assert.InRange(_downloader.PeakConcurrency, 2, 3);
    }

    [Fact]
    public async Task A_failed_download_is_reported_and_the_rest_of_the_batch_carries_on()
    {
        var broken = Offer("FS25_Plough", "1.1");
        var fine = Offer("FS25_Trailer", "2.0");

        _downloader.Fail(broken.Download!.Version, RemoteDownloadException.Gone("Hub"));
        _downloader.Serve(fine.Download!.Version, "2.0");

        var result = await ImportAsync([broken, fine]);

        var failure = result.Items.Single(x => x.Identity == broken.Identity);
        Assert.Equal(ModImportStatus.DownloadFailed, failure.Status);
        Assert.Equal("Hub no longer has this file.", failure.Message);
        Assert.Equal(ModImportStatus.Registered, result.Items.Single(x => x.Identity == fine.Identity).Status);
        Assert.Empty(_server.VersionsOf(Mod("FS25_Plough")));
    }

    [Fact]
    public async Task A_download_holding_another_version_is_never_registered_as_the_one_offered()
    {
        var offer = Offer("FS25_Plough", "1.1");
        _downloader.Serve(offer.Download!.Version, "1.2");

        var result = await ImportAsync([offer], x => x with { ResolveDownloadMismatches = null });

        Assert.Equal(ModImportStatus.DownloadMismatch, Assert.Single(result.Items).Status);
        Assert.Empty(_server.CallsOf(ServerCallKind.Register));
    }

    [Fact]
    public async Task Mismatches_are_asked_about_once_after_everything_else_has_finished()
    {
        var first = Offer("FS25_Plough", "1.1");
        var second = Offer("FS25_Trailer", "2.0");
        var fine = Offer("FS25_Seeder", "3.0");

        _downloader.Serve(first.Download!.Version, "1.2");
        _downloader.Serve(second.Download!.Version, "2.1");
        _downloader.Serve(fine.Download!.Version, "3.0");

        var asked = new List<IReadOnlyList<ModDownloadMismatch>>();

        await ImportAsync([first, second, fine], x => x with
        {
            ResolveDownloadMismatches = (mismatches, _) =>
            {
                asked.Add(mismatches);
                Assert.Contains(V("3.0"), _server.VersionsOf(Mod("FS25_Seeder")));

                return Task.FromResult<IReadOnlyDictionary<ModVersionIdentity, ModDownloadMismatchAnswer>?>(null);
            }
        });

        var question = Assert.Single(asked);
        Assert.Equal([first.Identity, second.Identity], question.Select(x => x.Offered.Identity));
    }

    [Fact]
    public async Task Using_what_was_downloaded_imports_that_version_in_place_of_the_one_picked()
    {
        _server.Seed(Mod("FS25_Plough"), "1.0");

        var offer = Offer("FS25_Plough", "1.1");
        _downloader.Serve(offer.Download!.Version, "1.2");

        var result = await ImportAsync([offer], x => x with { ResolveDownloadMismatches = Answer(ModDownloadMismatchAnswer.UseHeld) });

        var held = new ModVersionIdentity(Mod("FS25_Plough"), V("1.2"));
        var replaced = result.Items.Single(x => x.Identity == offer.Identity);

        Assert.Equal(ModImportStatus.Replaced, replaced.Status);
        Assert.Equal(held, replaced.ReplacedBy);
        Assert.True(replaced.IsSettled);
        Assert.Equal(ModImportStatus.Registered, result.Items.Single(x => x.Identity == held).Status);
        Assert.Equal(Vs("1.0", "1.2"), _server.VersionsOf(Mod("FS25_Plough")));
        Assert.Equal(1, _downloader.RunsDisposed);
    }

    [Fact]
    public async Task Leaving_a_mismatch_out_imports_nothing_for_it()
    {
        var offer = Offer("FS25_Plough", "1.1");
        _downloader.Serve(offer.Download!.Version, "1.2");

        var result = await ImportAsync([offer], x => x with { ResolveDownloadMismatches = Answer(ModDownloadMismatchAnswer.LeaveOut) });

        var item = Assert.Single(result.Items);
        Assert.Equal(ModImportStatus.LeftOut, item.Status);
        Assert.True(item.IsSettled);
        Assert.Empty(result.Unfinished);
        Assert.Empty(_server.VersionsOf(Mod("FS25_Plough")));
        Assert.Single(_downloader.Discarded);
    }

    [Fact]
    public async Task A_download_that_is_not_a_mod_can_only_be_left_out()
    {
        var offer = Offer("FS25_Plough", "1.1");
        _downloader.ServeNothing(offer.Download!.Version);

        IReadOnlyList<ModDownloadMismatch>? asked = null;

        var result = await ImportAsync([offer], x => x with
        {
            ResolveDownloadMismatches = (mismatches, cancellationToken) =>
            {
                asked = mismatches;
                return Answer(ModDownloadMismatchAnswer.UseHeld)(mismatches, cancellationToken);
            }
        });

        var mismatch = Assert.Single(asked!);
        Assert.Null(mismatch.Held);
        Assert.False(mismatch.CanUseHeld);
        Assert.Equal(ModImportStatus.LeftOut, Assert.Single(result.Items).Status);
        Assert.Null(Assert.Single(_notes.Notes).Held);
    }

    [Fact]
    public async Task A_download_holding_another_mod_cannot_be_used_in_its_place()
    {
        var offer = Offer("FS25_Plough", "1.1");
        _downloader.Serve(offer.Download!.Version, "1.1", fileName: "FS25_Trailer.zip");

        var result = await ImportAsync([offer], x => x with { ResolveDownloadMismatches = Answer(ModDownloadMismatchAnswer.UseHeld) });

        Assert.Equal(ModImportStatus.LeftOut, Assert.Single(result.Items).Status);
        Assert.Empty(_server.CallsOf(ServerCallKind.Register));
    }

    [Fact]
    public async Task Without_an_adapter_to_read_downloads_nothing_is_downloaded()
    {
        var offer = Offer("FS25_Plough", "1.1");

        var result = await ImportAsync([offer], x => x with { ModAdapter = null });

        Assert.Equal(ModImportStatus.NoLocalFile, Assert.Single(result.Items).Status);
        Assert.Equal(0, _downloader.Runs);
    }

    [Fact]
    public async Task A_version_the_repo_already_holds_is_not_downloaded()
    {
        _server.Seed(Mod("FS25_Plough"), "1.1");

        var result = await ImportAsync([Offer("FS25_Plough", "1.1")]);

        Assert.Equal(ModImportStatus.AlreadyRegistered, Assert.Single(result.Items).Status);
        Assert.Equal(0, _downloader.Runs);
    }

    [Fact]
    public async Task Cancelling_mid_download_registers_nothing_and_leaves_no_files()
    {
        using var cancellation = new CancellationTokenSource();

        var offer = Offer("FS25_Plough", "1.1");
        _downloader.Serve(offer.Download!.Version, "1.1");
        _downloader.OnDownload = () =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImportAsync([offer], cancellationToken: cancellation.Token));

        Assert.Empty(_server.CallsOf(ServerCallKind.Register));
        Assert.Equal(1, _downloader.RunsDisposed);
        Assert.Empty(Directory.EnumerateDirectories(_temp.Path, "run-*"));
    }


    private Task<ModImportResult> ImportAsync(
        IReadOnlyList<CatalogModVersion> versions,
        Func<ModImportRequest, ModImportRequest>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ModImportRequest(_server.RepoId, versions, DefaultModVersionComparer.Instance)
        {
            ModAdapter = new FakeMultiTargetBaseModAdapter()
        };

        return _service.ImportAsync(configure?.Invoke(request) ?? request, cancellationToken);
    }

    private CatalogModVersion Offer(string modId, string version)
        => CatalogModVersion.FromRemote(new RemoteOffer(_source, FakeRemoteModSource.Version(modId, version)));

    private static ModDownloadMismatchResolver Answer(ModDownloadMismatchAnswer answer)
        => (mismatches, _) => Task.FromResult<IReadOnlyDictionary<ModVersionIdentity, ModDownloadMismatchAnswer>?>(
            mismatches.ToDictionary(x => x.Offered.Identity, _ => answer));

    private static CatalogModVersion Local(string modId, string version)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{modId} {version}");
        var source = new ModSource(ModSourceId.Downloads, "Downloads", @"C:\Downloads", ModSourceKind.Downloads);

        return new CatalogModVersion(Mod(modId), V(version), modId, "A mod.", IsLocal: true, IsOnServer: false, Locked: false)
        {
            FoundIn = [new ModOccurrence(source, Path.Combine(source.Path, $"{modId}.zip"), bytes.Length, () => new MemoryStream(bytes))]
        };
    }
}
