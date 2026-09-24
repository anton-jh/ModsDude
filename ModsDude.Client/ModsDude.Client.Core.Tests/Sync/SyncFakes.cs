using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// Just enough of the three endpoints sync reads: the profile's dependencies, the repo's registered
/// content, and a download link per mod version. Blob storage is a dictionary keyed by the link.
/// </summary>
internal sealed class FakeSyncServer : IModDependenciesClient, IModsClient, IFilesClient
{
    private readonly List<ModDependencyDto> _dependencies = [];
    private readonly List<ModDto> _registered = [];
    private readonly Dictionary<string, byte[]> _blobs = [];


    public Guid RepoId { get; } = Guid.NewGuid();
    public Guid ProfileId { get; } = Guid.NewGuid();

    private int _downloadLinksMinted;
    private int _modListFetches;

    public int DownloadLinksMinted => Volatile.Read(ref _downloadLinksMinted);

    /// <summary>Set to hand back bytes that do not match the hash the repo declared.</summary>
    public Func<string, byte[]>? CorruptDownload { get; set; }


    /// <summary>Registers a version in the repo, uploads its bytes, and pins it in the profile.</summary>
    /// <param name="fileName">
    /// What the repo says the file is called. Defaults to the name the id alone produces, which is
    /// what a repo whose mods were imported from lower-cased folders holds.
    /// </param>
    public void Pin(string modId, string version, string content, bool locked = false, string? fileName = null, string? title = null)
    {
        Register(modId, version, content, locked, fileName, title);

        _dependencies.Add(new ModDependencyDto
        {
            ModId = modId,
            ModVersionId = version,
            FileName = fileName ?? $"{modId}.zip",
            ContentHash = SyncTestContent.HashOf(content),
            SizeBytes = SyncTestContent.Bytes(content).Length,
            Locked = locked
        });
    }

    /// <summary>
    /// Changes what the repo says a pinned mod's file is called, leaving its bytes alone - what a
    /// re-import from a correctly-cased source does to everybody else's next apply.
    /// </summary>
    public void Rename(string modId, string fileName)
    {
        foreach (var dependency in _dependencies.Where(x => x.ModId == modId))
        {
            dependency.FileName = fileName;
        }

        foreach (var version in _registered.Where(x => x.ModId == modId))
        {
            version.FileName = fileName;
        }
    }

    /// <summary>Takes a mod out of the profile, leaving it registered - what switching profiles looks like.</summary>
    public void Unpin(string modId)
        => _dependencies.RemoveAll(x => x.ModId == modId);

    /// <summary>Registers a version without pinning it - what the repo can reproduce but does not want here.</summary>
    public void Register(string modId, string version, string content, bool locked = false, string? fileName = null, string? title = null)
    {
        var hash = SyncTestContent.HashOf(content);

        _registered.Add(new ModDto
        {
            ModId = modId,
            VersionId = version,
            SequenceNumber = _registered.Count,
            DisplayName = title ?? modId,
            Description = "",
            FileName = fileName ?? $"{modId}.zip",
            ContentHash = hash,
            SizeBytes = SyncTestContent.Bytes(content).Length,
            Locked = locked,
            Attributes = [],
            Images = [],
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });

        _blobs[$"{modId}/{version}"] = SyncTestContent.Bytes(content);
    }

    public byte[] Blob(string link) => _blobs[link];


    /// <summary>What the head is, for the client that asks for no revision in particular.</summary>
    public int HeadRevision { get; set; } = 1;

    /// <summary>Every revision the client asked for, null being "whatever head is".</summary>
    public List<int?> RevisionsRequested { get; } = [];


    /// <summary>
    /// The mod list at one revision. The dependencies are the same whichever is asked for - sync
    /// reads a list and not a history - but the number is answered back exactly as the real endpoint
    /// does, so a client that hardcodes head cannot pass by accident.
    /// </summary>
    public Task<GetModDependenciesResponse> GetModDependenciesV1Async(Guid repoId, Guid profileId, int? revision = null, CancellationToken cancellationToken = default)
    {
        RevisionsRequested.Add(revision);

        return Task.FromResult(new GetModDependenciesResponse
        {
            Revision = revision ?? HeadRevision,
            IsHead = (revision ?? HeadRevision) == HeadRevision,
            Dependencies = [.. _dependencies]
        });
    }

    /// <summary>How many times the repo's mod list was asked for - the fetch a re-apply should never pay for.</summary>
    public int ModListFetches => Volatile.Read(ref _modListFetches);

    public Task<GetModsResponse> GetModsV1Async(Guid repoId, DateTime? updatedAfter = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _modListFetches);

        return Task.FromResult(new GetModsResponse { Mods = [.. _registered], NextCursor = null });
    }

    public Task<CreateModDownloadLinkResponse> CreateModDownloadLinkV1Async(CreateModDownloadLinkRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _downloadLinksMinted);

        return Task.FromResult(new CreateModDownloadLinkResponse { Link = $"{request.ModId}/{request.VersionId}" });
    }


    public Task DeleteModV1Async(Guid repoId, string modId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task DeleteModVersionV1Async(Guid repoId, string modId, string versionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<ModDto> RegisterModV1Async(Guid repoId, RegisterModRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<ModDependentsDto> GetModDependentsV1Async(Guid repoId, string modId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<ModDependentsDto> GetModVersionDependentsV1Async(Guid repoId, string modId, string versionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<GetModUsageResponse> GetModUsageV1Async(Guid repoId, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<GetModVersionsResponse> GetModVersionsV1Async(Guid repoId, string modId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<MoveModVersionResponse> MoveModVersionV1Async(Guid repoId, string modId, string versionId, MoveModVersionRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<ModDto> SetModVersionImagesV1Async(Guid repoId, string modId, string versionId, SetModVersionImagesRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<CreateModUploadLinkResponse> CreateModUploadLinkV1Async(CreateModUploadLinkRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<CreateSavegameDownloadLinkResponse> CreateSavegameDownloadLinkV1Async(CreateSavegameDownloadLinkRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<CreateSavegameUploadLinkResponse> CreateSavegameUploadLinkV1Async(CreateSavegameUploadLinkRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}


internal sealed class FakeModFileDownloader(FakeSyncServer server) : IModFileDownloader
{
    private int _downloads;

    public int Downloads => Volatile.Read(ref _downloads);

    /// <summary>Raised before each download, so a test can cancel exactly mid-fetch.</summary>
    public Action? BeforeDownload { get; set; }


    public Task<ModFileDownload> OpenAsync(string link, IProgress<long>? bytesReceived, CancellationToken cancellationToken)
    {
        BeforeDownload?.Invoke();
        Interlocked.Increment(ref _downloads);

        var bytes = server.CorruptDownload?.Invoke(link) ?? server.Blob(link);

        return Task.FromResult(new ModFileDownload(new MemoryStream(bytes), bytes.Length, new Nothing()));
    }


    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }
}


/// <summary>
/// A mod folder of files whose contents carry their own version, so the adapter reads the version off
/// the file the way a real one reads it out of the archive's metadata - and two builds can therefore
/// call themselves the same version.
/// </summary>
internal sealed class FakeModFolderAdapter(string modFolder, bool supportsHardlinks) : ILocalModAdapter
{
    public ModTarget Target { get; } = new(new TargetKey("mods"), null, modFolder);

    public ModTargets ModTargets => new(Target);

    public bool SupportsHardlinks { get; } = supportsHardlinks;


    /// <summary>Every file the adapter has opened, by name - which is what a scan costs.</summary>
    public ConcurrentBag<string> Opened { get; } = [];


    public Task<IEnumerable<LocalMod>> GetInstalledMods(ModTarget target, Func<string, bool> skip, CancellationToken cancellationToken)
        => Read(target.Path, skip);

    public Task<IEnumerable<LocalMod>> GetModsFromFolder(string path, CancellationToken cancellationToken)
        => Read(path, _ => false);

    private Task<IEnumerable<LocalMod>> Read(string path, Func<string, bool> skip)
    {
        var mods = new List<LocalMod>();

        foreach (var file in Directory.EnumerateFiles(path, "*.zip").Where(x => skip(x) is false))
        {
            Opened.Add(Path.GetFileName(file));

            var content = File.ReadAllText(file);

            if (SyncTestContent.TryReadVersion(content) is not string version)
            {
                // Not a readable mod - a readme, a half-finished download. Skipped, exactly as the
                // real adapter skips anything it cannot parse.
                continue;
            }

            var info = new FileInfo(file);

            mods.Add(new LocalMod(
                ModKey.From(Path.GetFileNameWithoutExtension(file)),
                ModVersionKey.From(version),
                Path.GetFileNameWithoutExtension(file),
                "",
                () => File.OpenRead(file))
            {
                FilePath = file,
                FileLength = info.Length
            });
        }

        return Task.FromResult<IEnumerable<LocalMod>>(mods);
    }

    public string GetModFilePath(ModTarget target, ModKey modId, ModVersionKey versionId, ModFileName? fileName)
        => Path.Combine(target.Path, fileName?.Value ?? $"{modId.Value}.zip");

    public ILocalModAdapter WithLocalSettings(string serializedLocalSettings) => this;
    public ILocalModAdapter WithLocalSettings(DynamicForm localSettings) => this;
}


internal sealed class FakeStoreProvider(ContentStore serving, params ContentStore[] others) : IContentStoreProvider
{
    public ContentStore GetStoreServing(string path) => serving;

    public IReadOnlyList<ContentStore> GetAllStores() => [serving, .. others];
}


internal sealed class FakeRecycleBin(bool available = true) : IRecycleBin
{
    public List<string> Recycled { get; } = [];

    public bool IsAvailableFor(string path) => available;

    public bool TryRecycle(string path)
    {
        if (available is false)
        {
            return false;
        }

        // The real bin keeps the bytes; the test only needs to know the file left the mod folder by
        // a route the user can undo.
        Recycled.Add(File.ReadAllText(path));
        File.Delete(path);

        return true;
    }
}


internal sealed class FakeModFolders(params GameModFolder[] folders) : IModFolders
{
    public IReadOnlyList<GameModFolder> GetAll() => folders;
}


/// <summary>
/// What the sync engine knows about savegames, over a list of holds a test writes directly.
/// </summary>
/// <remarks>
/// <para>
/// The rules themselves are not faked - <see cref="SavegameHoldRules"/> is pure and is the same code
/// the app runs, so a test that stubbed the answers would prove only that the stub was consulted.
/// What is replaced is the binding store behind them.
/// </para>
/// <para>
/// The observation records the revision the manifest said this folder was on at the moment it was
/// asked, which is the whole of what the ordering guarantees. Reading the manifest rather than
/// counting calls is the point: "observed before the manifest was rewritten" is not a fact about call
/// order that a test can see from outside - it is a fact about which revision the observer could
/// still have read, and that is the number play gets attributed to.
/// </para>
/// </remarks>
internal sealed class FakeHeldSavegames(SyncManifestStore manifests) : IHeldSavegames
{
    private readonly List<SavegameCheckoutBinding> _held = [];
    private readonly List<SavegameDrift> _drift = [];


    /// <summary>What the folder was on at each observation, oldest first. Null is "never synced".</summary>
    public List<int?> Observed { get; } = [];

    /// <summary>Which folder each observation was about, so a test can say it was this one's.</summary>
    public List<ModTargetRef> ObservedTargets { get; } = [];


    /// <summary>
    /// Records that this game is holding a savegame following one profile.
    /// </summary>
    /// <param name="targetRevision">A number makes it past, pinned there; null makes it current.</param>
    public void Hold(GameIdentity game, Guid profileId, int? targetRevision = null)
        => Add(profileId, targetRevision);

    /// <summary>One following no mod list, which claims nothing about the folder.</summary>
    public void HoldWithNoProfile(GameIdentity game) => Add(null, null);


    /// <remarks>
    /// Reads the manifest the same way the real one does - this folder's own, by the key the apply
    /// is about - so what is observed is the revision the folder being rewritten was still on.
    /// </remarks>
    public Task ObserveAsync(ModTargetRef target, CancellationToken ct)
    {
        Observed.Add(manifests.TryRead(target)?.ProfileRevision);
        ObservedTargets.Add(target);

        return Task.CompletedTask;
    }

    public int? GetRequiredRevision(GameIdentity game, Guid profileId)
        => SavegameHoldRules.RequiredRevision(_held, profileId);

    public SavegameApplyDecision DecideApply(GameIdentity game, Guid profileId, int? revision)
        => SavegameHoldRules.DecideApply(_held, profileId, revision);

    public SavegameCheckoutBinding? FindProfileHold(GameIdentity game)
        => SavegameHoldRules.FindProfileHold(_held);

    /// <summary>
    /// Whatever a test put there, which is nothing by default: what the notice <em>says</em> about a
    /// held slot is <see cref="SavegameDriftRules"/>'s and is exercised where that lives. What is
    /// worth reaching from here is which folder each answer is about, since that is what the monitor
    /// has to place it by.
    /// </summary>
    public Task<IReadOnlyList<SavegameDrift>> CheckDriftAsync(GameIdentity game, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SavegameDrift>>([.. _drift]);

    /// <summary>Records that a save held in one of this game's folders has drifted.</summary>
    public void Drifted(SavegameSlotRef slot)
        => _drift.Add(new SavegameDrift(Guid.NewGuid(), Guid.NewGuid(), slot, SavegameDriftKind.UncheckedInPlay));


    private void Add(Guid? profileId, int? targetRevision)
        => _held.Add(new SavegameCheckoutBinding(
            Guid.NewGuid(), Guid.NewGuid(), Keys.Slot("savegame1"), 1, "aaaa", DateTime.UtcNow)
        {
            ProfileId = profileId,
            ProfileRevision = targetRevision ?? 1,
            TargetRevision = targetRevision
        });
}


internal static class SyncTestContent
{
    private const string _separator = "|";


    /// <summary>A mod file's bytes: the version it declares, then whatever makes this build different.</summary>
    public static string File(string version, string build) => $"{version}{_separator}{build}";

    public static string? TryReadVersion(string content)
        => content.Split(_separator) is [var version, _] ? version : null;

    public static byte[] Bytes(string content) => Encoding.UTF8.GetBytes(content);

    public static string HashOf(string content) => ModContentHasher.Format(SHA256.HashData(Bytes(content)));
}
