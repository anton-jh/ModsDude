using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.GameProcesses;

namespace ModsDude.Client.Core.Tests.Sync;

internal sealed class SyncFixture : IDisposable
{
    private readonly TempDirectory _serving = new("sync-store");
    private readonly TempDirectory _other = new("sync-other-store");
    private readonly TempDirectory _manifests = new("sync-manifests");
    private readonly TempDirectory _second = new("sync-second-target");


    /// <param name="withSecondTarget">
    /// Whether this game reaches a second folder, as a dedicated server beside an MP client does.
    /// Off by default: the sync engine's unit of work is one folder either way, and what the
    /// second one is for is what the pin pass and the manifest keying do about it.
    /// </param>
    public SyncFixture(
        bool supportsHardlinks = false,
        bool recycleBinAvailable = true,
        long storeMaxSizeBytes = 1024L * 1024 * 1024,
        bool withSecondTarget = false)
    {
        ServingStore = new ContentStore("C:\\", _serving.Path, storeMaxSizeBytes);
        OtherStore = new ContentStore("D:\\", _other.Path, storeMaxSizeBytes);

        Adapter = new FakeModFolderAdapter(Folder.Path, supportsHardlinks);
        Downloader = new FakeModFileDownloader(Server);
        RecycleBin = new FakeRecycleBin(recycleBinAvailable);
        Manifests = new SyncManifestStore(_manifests.Path);
        Drift = new DriftService(Manifests, NullLogger<DriftService>.Instance);
        Held = new FakeHeldSavegames(Manifests);

        // Same game, same disk, another folder - which is what makes its manifest something the
        // sweep has to consult rather than something it may skip along with this game.
        GameModFolder[] folders = withSecondTarget
            ? [new(Target, Folder.Path), new(SecondTarget, _second.Path)]
            : [new(Target, Folder.Path)];

        Service = new ModSyncService(
            Server,
            Server,
            Server,
            Downloader,
            new FakeStoreProvider(ServingStore, OtherStore),
            Manifests,
            RecycleBin,
            new FakeModFolders(folders),
            Held,
            Held,
            Leases,
            new GameFileEditor(RecycleBin, NullLogger<GameFileEditor>.Instance),
            Guard,
            TimeProvider.System,
            NullLogger<ModSyncService>.Instance);
    }


    public TempDirectory Folder { get; } = new("sync-mods");
    public FakeSyncServer Server { get; } = new();
    public FakeModFolderAdapter Adapter { get; }
    public FakeModFileDownloader Downloader { get; }
    public FakeRecycleBin RecycleBin { get; }
    public SyncManifestStore Manifests { get; }
    public DriftService Drift { get; }
    public FakeHeldSavegames Held { get; }

    /// <summary>
    /// The real thing rather than a fake: what the sync does about a busy store is the behaviour
    /// under test, and half of it is the primitive's own queueing.
    /// </summary>
    public ResourceLeases Leases { get; } = new();
    public FakeGameRunningGuard Guard { get; } = new();

    public ModSyncService Service { get; }
    public ContentStore ServingStore { get; }
    public ContentStore OtherStore { get; }
    public ModTargetRef Target { get; } = Keys.Target();

    /// <summary>Another folder of the same game - present only where the fixture was built with one.</summary>
    public ModTargetRef SecondTarget { get; } = Keys.Target("server");

    public GameIdentity Game => Target.Game;


    public Task<ModSyncPlan> PlanAsync(int? revision = null)
        => Service.PlanAsync(
            new ModSyncRequest(Game, "Test Game", Adapter.Target, Adapter, Server.RepoId, Server.ProfileId) { Revision = revision },
            CancellationToken.None);

    /// <summary>The same request pointed at an empty list, as deactivating and clearing plans it.</summary>
    public Task<ModSyncPlan> PlanClearAsync()
        => Service.PlanAsync(
            new ModSyncRequest(Game, "Test Game", Adapter.Target, Adapter, Server.RepoId, Guid.Empty) { ClearAll = true },
            CancellationToken.None);

    public Task<ModSyncResult> ExecuteAsync(ModSyncPlan plan)
        => Service.ExecuteAsync(plan, null, CancellationToken.None);

    public void Install(string name, string content) => Folder.WriteFile(name, content);

    /// <summary>Every name in the mod folder, which is where casing is visible at all.</summary>
    public IReadOnlyList<string> FolderContents()
        => [.. Directory.EnumerateFiles(Folder.Path).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    public string ReadInstalled(string name) => File.ReadAllText(Folder.Combine(name));

    /// <summary>A file where the store's quarantine folder would be created.</summary>
    public void BlockStoreQuarantine() => File.WriteAllText(ServingStore.QuarantinePath, "");

    public DriftReport CheckDrift()
        => Drift.Check(Target, new ActiveProfile(Server.RepoId, Server.ProfileId), Folder.Path);

    /// <summary>Puts content in the serving store and answers with its address.</summary>
    public async Task<string> SeedIntoStoreAsync(string content)
    {
        var hash = SyncTestContent.HashOf(content);

        await ServingStore.IngestAsync(
            new MemoryStream(SyncTestContent.Bytes(content)),
            hash,
            null,
            CancellationToken.None);

        return hash;
    }

    /// <summary>What this folder's own manifest says it is running - the list it is leaving.</summary>
    public void WriteManifest(string hash) => WriteManifest(Target, Folder.Path, hash);

    /// <summary>
    /// The manifest as it stands, rewritten as another profile's and without the names an older
    /// client never recorded - so the next plan moves the folder off it, and any title it shows
    /// came from the repo rather than from here.
    /// </summary>
    public void WriteManifestForAnotherProfile()
    {
        var manifest = Manifests.TryRead(Target)!;

        Manifests.Write(manifest with
        {
            ProfileId = Guid.NewGuid(),
            Entries = [.. manifest.Entries.Select(x => x with { DisplayName = null })]
        });
    }

    /// <summary>What the game's other folder is running, which no sweep may take back.</summary>
    public void WriteSecondTargetManifest(string hash) => WriteManifest(SecondTarget, _second.Path, hash);

    public void Dispose()
    {
        Folder.Dispose();
        _serving.Dispose();
        _other.Dispose();
        _second.Dispose();
        _manifests.Dispose();
    }


    private void WriteManifest(ModTargetRef target, string modFolder, string hash)
    {
        Manifests.Write(new SyncManifest
        {
            Target = target,
            RepoId = Server.RepoId,
            ProfileId = Server.ProfileId,
            SyncedAt = DateTimeOffset.UtcNow,
            ModFolder = modFolder,
            Entries = [new SyncManifestEntry("fs25_other", "3.0.0", hash, "fs25_other.zip", 1, DateTimeOffset.UtcNow)]
        });
    }
}
