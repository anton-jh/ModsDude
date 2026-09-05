using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using System.Security.Cryptography;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// Just enough of the savegame endpoints, with blob storage as a dictionary keyed by content hash -
/// which is the one property of the real storage the client's behaviour turns on, since a check-in
/// whose bytes are already there is supposed to skip the upload entirely.
/// </summary>
/// <remarks>
/// The rules the client is tested against are the server's real ones, reproduced rather than
/// stubbed: a stale base is refused unless forced, a check-in whose hash equals the head's mints no
/// version, and the head answered back is the one a client would have been given. A fake that said
/// yes to everything would let a client that never sends <c>basedOn</c> pass.
/// </remarks>
internal sealed class FakeSavegameServer : ISavegamesClient, IFilesClient
{
    private readonly List<SavegameVersionDto> _versions = [];
    private readonly Dictionary<string, byte[]> _blobs = [];

    private SavegameDto _savegame = null!;


    public FakeSavegameServer()
    {
        _savegame = new SavegameDto
        {
            Id = SavegameId,
            RepoId = RepoId,
            Name = "Season 4",
            ProfileId = ProfileId,
            Created = DateTime.UtcNow
        };
    }


    public Guid RepoId { get; } = Guid.NewGuid();
    public Guid ProfileId { get; } = Guid.NewGuid();
    public Guid SavegameId { get; } = Guid.NewGuid();

    public int CheckoutsTaken { get; private set; }
    public int CheckoutsDiscarded { get; private set; }
    public int UploadLinksMinted { get; private set; }
    public int DownloadLinksMinted { get; private set; }

    /// <summary>Every check-in the client sent, so a test can read what it based itself on.</summary>
    public List<CheckInSavegameRequest> CheckIns { get; } = [];

    public List<PublishSavegameRequest> Publishes { get; } = [];

    /// <summary>The versions this savegame has, oldest first.</summary>
    public IReadOnlyList<SavegameVersionDto> Versions => _versions;

    public SavegameDto Savegame => _savegame;

    public SavegameVersionDto? Head => _savegame.Head;


    /// <summary>Puts a version and its bytes on the server - a publish that happened before the test.</summary>
    public SavegameVersionDto Seed(byte[] content, int profileRevision = 1)
    {
        var hash = HashOf(content);

        _blobs[hash] = content;

        return AddVersion(hash, content.Length, profileRevision, SavegameVersionOrigin.Created, null);
    }

    /// <summary>Somebody else took the save over and checked in while this machine was playing.</summary>
    public SavegameVersionDto CheckInFromAnotherMachine(byte[] content, int profileRevision = 1)
    {
        var hash = HashOf(content);

        _blobs[hash] = content;

        return AddVersion(hash, content.Length, profileRevision, SavegameVersionOrigin.CheckedIn, _savegame.Head?.Number);
    }

    public bool HasBlob(string contentHash) => _blobs.ContainsKey(contentHash);

    public byte[] Blob(string link) => _blobs[link];

    /// <summary>Called by the uploader fake: storage is content-addressed, so the link is the hash.</summary>
    public void PutBlob(string link, byte[] content) => _blobs[link] = content;


    public Task<CheckOutSavegameResponse> CheckOutSavegameV1Async(Guid repoId, Guid savegameId, CancellationToken cancellationToken = default)
    {
        CheckoutsTaken++;

        return Task.FromResult(new CheckOutSavegameResponse { Checkout = Checkout() });
    }

    public Task DiscardSavegameCheckoutV1Async(Guid repoId, Guid savegameId, CancellationToken cancellationToken = default)
    {
        CheckoutsDiscarded++;

        return Task.CompletedTask;
    }

    public Task<SavegameVersionDto> CheckInSavegameV1Async(Guid repoId, Guid savegameId, CheckInSavegameRequest request, CancellationToken cancellationToken = default)
    {
        CheckIns.Add(request);

        if (_blobs.ContainsKey(request.ContentHash) is false)
        {
            // The server refuses a version whose blob is absent, because that is a head nobody can
            // check out. Reproduced so that a client which skips the upload wrongly fails here.
            throw Problem(ProblemType.NotFound, $"No savegame blob '{request.ContentHash}'.");
        }

        var head = _savegame.Head;
        var isStale = head is not null && head.Number != request.BasedOn;

        if (isStale && request.Force is false)
        {
            throw Problem(ProblemType.SavegameVersionStale, $"Based on {request.BasedOn}, head is {head!.Number}.");
        }

        // A check-in that changes nothing mints nothing, and is answered with the head instead.
        if (head is not null && head.ContentHash == request.ContentHash)
        {
            return Task.FromResult(head);
        }

        return Task.FromResult(AddVersion(
            request.ContentHash,
            request.SizeBytes,
            request.ProfileRevision,
            isStale ? SavegameVersionOrigin.Forced : SavegameVersionOrigin.CheckedIn,
            request.BasedOn,
            request.Label));
    }

    public Task<SavegameDto> PublishSavegameV1Async(Guid repoId, PublishSavegameRequest request, CancellationToken cancellationToken = default)
    {
        Publishes.Add(request);

        if (_blobs.ContainsKey(request.ContentHash) is false)
        {
            throw Problem(ProblemType.NotFound, $"No savegame blob '{request.ContentHash}'.");
        }

        _savegame = _savegame with
        {
            Id = request.SavegameId,
            Name = request.Name,
            ProfileId = request.ProfileId
        };

        AddVersion(request.ContentHash, request.SizeBytes, request.ProfileRevision, SavegameVersionOrigin.Created, null, request.Label);

        return Task.FromResult(_savegame);
    }

    public Task<GetSavegameVersionsResponse> GetSavegameVersionsV1Async(Guid repoId, Guid savegameId, int? skip = null, int? limit = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new GetSavegameVersionsResponse
        {
            Versions = [.. _versions],
            HeadVersion = _savegame.Head?.Number ?? 0,
            HasMore = false
        });

    public Task<CreateSavegameDownloadLinkResponse> CreateSavegameDownloadLinkV1Async(CreateSavegameDownloadLinkRequest request, CancellationToken cancellationToken = default)
    {
        DownloadLinksMinted++;

        return Task.FromResult(new CreateSavegameDownloadLinkResponse { Link = request.ContentHash });
    }

    public Task<CreateSavegameUploadLinkResponse> CreateSavegameUploadLinkV1Async(CreateSavegameUploadLinkRequest request, CancellationToken cancellationToken = default)
    {
        UploadLinksMinted++;

        var stored = _blobs.ContainsKey(request.ContentHash);

        return Task.FromResult(new CreateSavegameUploadLinkResponse
        {
            // The real one answers with no link at all when the bytes are already there, so a client
            // that ignored AlreadyStored would fail loudly rather than upload for nothing.
            Link = stored ? null : request.ContentHash,
            AlreadyStored = stored,
            ContentHashMetadataKey = "contenthash"
        });
    }


    public Task<GetSavegameCheckoutsResponse> GetSavegameCheckoutsV1Async(Guid repoId, Guid savegameId, int? skip = null, int? limit = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task DeleteSavegameV1Async(Guid repoId, Guid savegameId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<SavegameDto> UpdateSavegameV1Async(Guid repoId, Guid savegameId, UpdateSavegameRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<ICollection<SavegameDto>> GetSavegamesV1Async(Guid repoId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<SavegameVersionDto> RestoreSavegameVersionV1Async(Guid repoId, Guid savegameId, int number, RestoreSavegameVersionRequest? request = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<CreateModDownloadLinkResponse> CreateModDownloadLinkV1Async(CreateModDownloadLinkRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<CreateModUploadLinkResponse> CreateModUploadLinkV1Async(CreateModUploadLinkRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();


    public static string HashOf(byte[] content) => ModContentHasher.Format(SHA256.HashData(content));

    private SavegameVersionDto AddVersion(
        string contentHash,
        long sizeBytes,
        int profileRevision,
        SavegameVersionOrigin origin,
        int? baseVersion,
        string? label = null)
    {
        var version = new SavegameVersionDto
        {
            RepoId = RepoId,
            SavegameId = _savegame.Id,
            Number = (_savegame.Head?.Number ?? 0) + 1,
            ProfileId = _savegame.ProfileId,
            ProfileRevision = profileRevision,
            ContentHash = contentHash,
            SizeBytes = sizeBytes,
            Created = DateTime.UtcNow,
            CreatedBy = new UserDto { Id = "someone", DisplayName = "Someone", Tag = "0001" },
            Label = label,
            Origin = origin,
            BaseVersion = baseVersion
        };

        _versions.Add(version);
        _savegame = _savegame with { Head = version };

        return version;
    }

    private SavegameCheckoutDto Checkout() => new()
    {
        Id = Guid.NewGuid(),
        RepoId = RepoId,
        SavegameId = _savegame.Id,
        User = new UserDto { Id = "me", DisplayName = "Me", Tag = "0002" },
        TakenAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(1),
        Status = SavegameCheckoutStatus.Held
    };

    private static ApiException<CustomProblemDetails> Problem(ProblemType type, string detail)
        => new("A server side error occurred.", 400, null, new Dictionary<string, IEnumerable<string>>(), new CustomProblemDetails
        {
            Type = type,
            Detail = detail
        }, null);
}


/// <summary>
/// The upload half of blob storage. Hashes what it is given exactly as the real block-blob uploader
/// does, so a test can assert that what was committed is what was packed.
/// </summary>
internal sealed class FakeSavegameUploader(FakeSavegameServer server) : IModFileUploader
{
    public int Uploads { get; private set; }


    public async Task<string> UploadAsync(ModFileUpload upload, CancellationToken cancellationToken)
    {
        Uploads++;

        using var content = upload.OpenContent();
        using var buffer = new MemoryStream();

        await content.CopyToAsync(buffer, cancellationToken);

        var bytes = buffer.ToArray();
        var hash = FakeSavegameServer.HashOf(bytes);

        server.PutBlob(upload.Link, bytes);

        return hash;
    }
}


internal sealed class FakeSavegameDownloader(FakeSavegameServer server) : IModFileDownloader
{
    public int Downloads { get; private set; }


    public Task<ModFileDownload> OpenAsync(string link, CancellationToken cancellationToken)
    {
        Downloads++;

        var bytes = server.Blob(link);

        return Task.FromResult(new ModFileDownload(new MemoryStream(bytes), bytes.Length, new Nothing()));
    }


    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }
}


/// <summary>
/// A fixed set of numbered slots under a real directory, which is what Farming Simulator is. Occupied
/// means the folder exists and has something in it - the same thing a real adapter decides by reading
/// the save.
/// </summary>
internal sealed class FakeSavegameAdapter(string root, params string[] slotIds) : IInstanceSavegameAdapter
{
    public bool CanCreateSlots => false;

    /// <summary>What the game calls each save, keyed by slot - the name a picker shows.</summary>
    public Dictionary<string, string> DisplayNames { get; } = [];


    public string GetSlotPath(SavegameSlotId slot) => Path.Combine(root, slot.Value);

    public Task<IReadOnlyList<SavegameSlot>> GetSlots(CancellationToken cancellationToken)
    {
        IReadOnlyList<SavegameSlot> slots =
        [
            .. slotIds.Select(id => new SavegameSlot(
                new SavegameSlotId(id),
                DisplayNames.GetValueOrDefault(id),
                IsOccupied(id),
                null,
                null))
        ];

        return Task.FromResult(slots);
    }

    public IInstanceSavegameAdapter WithInstanceSettings(string serializedInstanceSettings) => this;
    public IInstanceSavegameAdapter WithInstanceSettings(DynamicForm instanceSettings) => this;


    private bool IsOccupied(string slotId)
    {
        var path = Path.Combine(root, slotId);

        return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
    }
}


internal sealed class FakeInstanceSavegameAdapters(IInstanceSavegameAdapter? adapter) : IInstanceSavegameAdapters
{
    public IInstanceSavegameAdapter? TryGet(LocalInstance instance) => adapter;

    public IInstanceSavegameAdapter? TryGet(Guid instanceId) => adapter;
}


/// <summary>What this client has been told the heads are. Empty is "not asked", never "unchanged".</summary>
internal sealed class FakeSavegameHeadVersions : ISavegameHeadVersions
{
    private readonly Dictionary<Guid, int> _heads = [];


    public void Set(Guid savegameId, int headVersion) => _heads[savegameId] = headVersion;

    public int? GetHeadVersion(Guid repoId, Guid savegameId)
        => _heads.TryGetValue(savegameId, out var head) ? head : null;
}


/// <summary>
/// A Recycle Bin for whole folders, which is what a savegame slot is. Records what went in so a test
/// can assert the local copy left by a route the user can undo rather than by deletion.
/// </summary>
internal sealed class FakeSlotRecycleBin(bool available = true) : IRecycleBin
{
    public List<string> Recycled { get; } = [];


    public bool IsAvailableFor(string path) => available;

    public bool TryRecycle(string path)
    {
        if (available is false)
        {
            return false;
        }

        Recycled.Add(path);
        Directory.Delete(path, recursive: true);

        return true;
    }
}


/// <summary>
/// The persisted instances, in memory - <c>state.json</c> lives at a fixed path under LocalAppData,
/// and a test running against the real store would rewrite the developer's own instance list.
/// </summary>
internal sealed class FakeInstanceState : IPersistedInstanceState
{
    private readonly Dictionary<Guid, PersistedLocalInstance> _instances = [];


    public int Saves { get; private set; }


    public void Add(PersistedLocalInstance instance) => _instances[instance.Id] = instance;

    public PersistedLocalInstance? Find(Guid instanceId)
        => _instances.TryGetValue(instanceId, out var instance) ? instance : null;

    public void Save() => Saves++;
}
