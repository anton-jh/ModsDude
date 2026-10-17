using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Mods;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.RemoteSources;
using ModsDude.Client.Core.Tests.Stores;
using ModsDude.Client.Core.Tests.Sync;
using System.Text;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Import;

/// <summary>Mods whose names look like accidental copies, such as 'x (1)': asked about first, and left out unless confirmed.</summary>
public class ModImportLikelyCopyTests : IDisposable
{
    private static readonly ModSource _downloads =
        new(ModSourceId.Downloads, "Downloads", @"C:\Downloads", ModSourceKind.Downloads);

    private readonly FakeModsDudeServer _server = new();
    private readonly TempDirectory _temp = new("import-copy");
    private readonly ModImportService _service;


    public ModImportLikelyCopyTests()
    {
        _service = new ModImportService(
            _server,
            _server,
            new ModStore(_server, InlineStoreDispatcher.Instance, NullLogger<ModStore>.Instance),
            new FakeModFileUploader(_server),
            new RecordingModImagePublisher(),
            new FakeRecycleBin(),
            new FakeStoreProvider(new ContentStore(@"C:\", _temp.CreateSubdirectory("store"), long.MaxValue)),
            new FakeRemoteModDownloader(_temp),
            new InMemoryRemoteDownloadNotes(),
            NullLogger<ModImportService>.Instance);
    }


    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }


    [Fact]
    public async Task Nothing_is_asked_when_no_mod_looks_like_a_copy()
    {
        var asked = false;

        var result = await ImportAsync([Local("FS25_Plough", "1.0")], Answer(true, () => asked = true));

        Assert.False(asked);
        Assert.Equal(ModImportStatus.Registered, Assert.Single(result.Items).Status);
    }

    [Fact]
    public async Task Every_likely_copy_is_asked_about_at_once_and_in_order()
    {
        IReadOnlyList<CatalogModVersion>? asked = null;

        await ImportAsync(
            [Copy("FS25_Tractor (1)", "2.0"), Local("FS25_Plough", "1.0"), Copy("FS25_Baler (1)", "1.0"), Copy("FS25_Baler (1)", "0.9")],
            (versions, _) =>
            {
                asked = versions;
                return Task.FromResult(false);
            });

        Assert.NotNull(asked);
        Assert.Equal(
            ["fs25_baler (1) 0.9", "fs25_baler (1) 1.0", "fs25_tractor (1) 2.0"],
            asked.Select(x => $"{x.ModId} {x.VersionId}"));
    }

    [Fact]
    public async Task A_no_leaves_the_likely_copies_out_and_imports_the_rest()
    {
        var result = await ImportAsync([Copy("FS25_Plough (1)", "1.0"), Local("FS25_Baler", "1.0")], Answer(false));

        var copy = result.Items.Single(x => x.Identity.ModId == Mod("FS25_Plough (1)"));
        Assert.Equal(ModImportStatus.LeftOut, copy.Status);
        Assert.True(copy.IsSettled);

        Assert.Equal(ModImportStatus.Registered, result.Items.Single(x => x.Identity.ModId == Mod("FS25_Baler")).Status);
        Assert.Empty(_server.VersionsOf(Mod("FS25_Plough (1)")));
    }

    [Fact]
    public async Task A_yes_imports_them_under_the_names_they_have()
    {
        var result = await ImportAsync([Copy("FS25_Plough (1)", "1.0")], Answer(true));

        Assert.Equal(ModImportStatus.Registered, Assert.Single(result.Items).Status);
        Assert.Equal(Vs("1.0"), _server.VersionsOf(Mod("FS25_Plough (1)")));
    }

    [Fact]
    public async Task Without_anyone_to_ask_the_likely_copies_are_left_out()
    {
        var result = await ImportAsync([Copy("FS25_Plough (1)", "1.0")], resolver: null);

        Assert.Equal(ModImportStatus.LeftOut, Assert.Single(result.Items).Status);
        Assert.Empty(_server.CallsOf(ServerCallKind.Register));
    }

    [Fact]
    public async Task A_question_that_fails_leaves_the_likely_copies_out()
    {
        var result = await ImportAsync(
            [Copy("FS25_Plough (1)", "1.0"), Local("FS25_Baler", "1.0")],
            (_, _) => throw new InvalidOperationException("The modal could not be shown."));

        Assert.Equal(ModImportStatus.LeftOut, result.Items.Single(x => x.Identity.ModId == Mod("FS25_Plough (1)")).Status);
        Assert.Equal(ModImportStatus.Registered, result.Items.Single(x => x.Identity.ModId == Mod("FS25_Baler")).Status);
    }

    [Fact]
    public async Task A_mod_the_repo_already_holds_under_that_name_is_not_asked_about_again()
    {
        _server.Seed(Mod("FS25_Plough (1)"), "1.0");
        var asked = false;

        var result = await ImportAsync([Copy("FS25_Plough (1)", "1.1")], Answer(false, () => asked = true));

        Assert.False(asked);
        Assert.Equal(ModImportStatus.Registered, Assert.Single(result.Items).Status);
        Assert.Equal(Vs("1.0", "1.1"), _server.VersionsOf(Mod("FS25_Plough (1)")));
    }

    [Fact]
    public async Task A_version_the_repo_already_holds_is_not_asked_about()
    {
        _server.Seed(Mod("FS25_Plough (1)"), "1.0");
        var asked = false;

        var result = await ImportAsync([Copy("FS25_Plough (1)", "1.0")], Answer(false, () => asked = true));

        Assert.False(asked);
        Assert.Equal(ModImportStatus.AlreadyRegistered, Assert.Single(result.Items).Status);
    }

    [Fact]
    public async Task Cancelling_while_the_question_is_open_registers_nothing()
    {
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImportAsync(
            [Copy("FS25_Plough (1)", "1.0"), Local("FS25_Baler", "1.0")],
            async (_, cancellationToken) =>
            {
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return true;
            },
            cancellation.Token));

        Assert.Empty(_server.CallsOf(ServerCallKind.Register));
    }


    private Task<ModImportResult> ImportAsync(
        IReadOnlyList<CatalogModVersion> versions,
        ModLikelyCopyResolver? resolver,
        CancellationToken cancellationToken = default)
    {
        var request = new ModImportRequest(_server.RepoId, versions, DefaultModVersionComparer.Instance)
        {
            ResolveLikelyCopies = resolver
        };

        return _service.ImportAsync(request, cancellationToken);
    }

    private static ModLikelyCopyResolver Answer(bool importAnyway, Action? onAsked = null)
    {
        return (_, _) =>
        {
            onAsked?.Invoke();
            return Task.FromResult(importAnyway);
        };
    }

    private static CatalogModVersion Copy(string modId, string version)
        => Local(modId, version) with { LikelyCopyOf = Mod(modId[..modId.LastIndexOf(" (", StringComparison.Ordinal)]) };

    private static CatalogModVersion Local(string modId, string version)
    {
        var bytes = Encoding.UTF8.GetBytes($"mod bytes of {modId} {version}");

        return new CatalogModVersion(Mod(modId), V(version), modId, "A mod.", IsLocal: true, IsOnServer: false, Locked: false)
        {
            FoundIn = [new ModOccurrence(_downloads, Path.Combine(_downloads.Path, $"{modId}.zip"), bytes.Length, () => new MemoryStream(bytes))]
        };
    }
}
