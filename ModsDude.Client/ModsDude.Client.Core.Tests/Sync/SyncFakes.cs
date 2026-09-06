using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Sync;
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

    public int DownloadLinksMinted { get; private set; }

    /// <summary>Set to hand back bytes that do not match the hash the repo declared.</summary>
    public Func<string, byte[]>? CorruptDownload { get; set; }


    /// <summary>Registers a version in the repo, uploads its bytes, and pins it in the profile.</summary>
    /// <param name="fileName">
    /// What the repo says the file is called. Defaults to the name the id alone produces, which is
    /// what a repo whose mods were imported from lower-cased folders holds.
    /// </param>
    public void Pin(string modId, string version, string content, bool locked = false, string? fileName = null)
    {
        Register(modId, version, content, locked, fileName);

        _dependencies.Add(new ModDependencyDto
        {
            ModId = modId,
            ModVersionId = version,
            FileName = fileName ?? $"{modId}.zip",
            ContentHash = SyncTestContent.HashOf(content),
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
    public void Register(string modId, string version, string content, bool locked = false, string? fileName = null)
    {
        var hash = SyncTestContent.HashOf(content);

        _registered.Add(new ModDto
        {
            ModId = modId,
            VersionId = version,
            SequenceNumber = _registered.Count,
            DisplayName = modId,
            Description = "",
            FileName = fileName ?? $"{modId}.zip",
            ContentHash = hash,
            Locked = locked,
            Attributes = [],
            Images = [],
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });

        _blobs[$"{modId}/{version}"] = SyncTestContent.Bytes(content);
    }

    public byte[] Blob(string link) => _blobs[link];


    /// <summary>The profile is always on revision 1 here: sync reads a mod list, not a history.</summary>
    public Task<GetModDependenciesResponse> GetModDependenciesV1Async(Guid repoId, Guid profileId, int? revision = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new GetModDependenciesResponse { Revision = 1, IsHead = true, Dependencies = [.. _dependencies] });

    public Task<GetModsResponse> GetModsV1Async(Guid repoId, DateTime? updatedAfter = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new GetModsResponse { Mods = [.. _registered], NextCursor = null });

    public Task<CreateModDownloadLinkResponse> CreateModDownloadLinkV1Async(CreateModDownloadLinkRequest request, CancellationToken cancellationToken = default)
    {
        DownloadLinksMinted++;

        return Task.FromResult(new CreateModDownloadLinkResponse { Link = $"{request.ModId}/{request.VersionId}" });
    }


    public Task DeleteModV1Async(Guid repoId, string modId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task DeleteModVersionV1Async(Guid repoId, string modId, string versionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<ModDto> RegisterModV1Async(Guid repoId, RegisterModRequest request, CancellationToken cancellationToken = default)
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
    public int Downloads { get; private set; }

    /// <summary>Raised before each download, so a test can cancel exactly mid-fetch.</summary>
    public Action? BeforeDownload { get; set; }


    public Task<ModFileDownload> OpenAsync(string link, CancellationToken cancellationToken)
    {
        BeforeDownload?.Invoke();
        Downloads++;

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
internal sealed class FakeModFolderAdapter(string modFolder, bool supportsHardlinks) : IInstanceModAdapter
{
    public string ModFolder { get; } = modFolder;

    public bool SupportsHardlinks { get; } = supportsHardlinks;


    public Task<IEnumerable<LocalMod>> GetInstalledMods(CancellationToken cancellationToken)
        => GetModsFromFolder(ModFolder, cancellationToken);

    public Task<IEnumerable<LocalMod>> GetModsFromFolder(string path, CancellationToken cancellationToken)
    {
        var mods = new List<LocalMod>();

        foreach (var file in Directory.EnumerateFiles(path, "*.zip"))
        {
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

    public string GetModFilePath(ModKey modId, ModVersionKey versionId, ModFileName? fileName)
        => Path.Combine(ModFolder, fileName?.Value ?? $"{modId.Value}.zip");

    public IInstanceModAdapter WithInstanceSettings(string serializedInstanceSettings) => this;
    public IInstanceModAdapter WithInstanceSettings(DynamicForm instanceSettings) => this;
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


internal sealed class FakeInstanceModFolders(params InstanceModFolder[] folders) : IInstanceModFolders
{
    public IReadOnlyList<InstanceModFolder> GetAll() => folders;
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
