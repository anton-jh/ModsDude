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
/// snapshot, and the head answered back is the one a client would have been given. A fake that said
/// yes to everything would let a client that never sends <c>basedOn</c> pass.
/// </remarks>
internal sealed class FakeSavegameServer : ISavegamesClient, IFilesClient
{
    private readonly List<SavegameSnapshotDto> _snapshots = [];
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

    /// <summary>The snapshots this savegame has, oldest first.</summary>
    public IReadOnlyList<SavegameSnapshotDto> Snapshots => _snapshots;

    public SavegameDto Savegame => _savegame;

    public SavegameSnapshotDto? Head => _savegame.Head;


    /// <summary>
    /// Makes this savegame follow no mod list, which publish offers as a choice and which every
    /// adapter with savegames but no mods gets by default. Its snapshots then record no revision.
    /// </summary>
    public void FollowNoProfile() => _savegame = _savegame with { ProfileId = null };

    /// <summary>
    /// Makes this savegame a past one: somebody published a new savegame to its profile, and that one is
    /// current now. Nothing about this savegame's own history changes - a past savegame is not
    /// read-only, and the single restriction is that its profile revision does not move.
    /// </summary>
    public void Supersede() => _savegame = _savegame with { SupersededAt = DateTime.UtcNow };

    /// <summary>Puts a snapshot and its bytes on the server - a publish that happened before the test.</summary>
    public SavegameSnapshotDto Seed(byte[] content, int? profileRevision = 1)
    {
        var hash = HashOf(content);

        _blobs[hash] = content;

        return AddSnapshot(hash, content.Length, profileRevision, SavegameSnapshotOrigin.Created, null);
    }

    /// <summary>Somebody else took the save over and checked in while this machine was playing.</summary>
    public SavegameSnapshotDto CheckInFromAnotherMachine(byte[] content, int profileRevision = 1)
    {
        var hash = HashOf(content);

        _blobs[hash] = content;

        return AddSnapshot(hash, content.Length, profileRevision, SavegameSnapshotOrigin.CheckedIn, _savegame.Head?.Number);
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

    public Task<SavegameSnapshotDto> CheckInSavegameV1Async(Guid repoId, Guid savegameId, CheckInSavegameRequest request, CancellationToken cancellationToken = default)
    {
        CheckIns.Add(request);

        if (_blobs.ContainsKey(request.ContentHash) is false)
        {
            // The server refuses a snapshot whose blob is absent, because that is a head nobody can
            // check out. Reproduced so that a client which skips the upload wrongly fails here.
            throw Problem(ProblemType.NotFound, $"No savegame blob '{request.ContentHash}'.");
        }

        // The pairing, refused in either direction rather than resolved in one: a savegame following
        // no mod list has no revision to send, and one that follows a mod list has to name which.
        if ((_savegame.ProfileId is null) != (request.ProfileRevision is null))
        {
            throw Problem(
                ProblemType.SavegameProfileNotPaired,
                $"Savegame '{_savegame.Id}' {(_savegame.ProfileId is null ? "follows no profile but a revision was sent" : "follows a profile but no revision was sent")}.");
        }

        var head = _savegame.Head;
        var isStale = head is not null && head.Number != request.BasedOn;

        if (isStale && request.Force is false)
        {
            throw Problem(ProblemType.SavegameSnapshotStale, $"Based on {request.BasedOn}, head is {head!.Number}.");
        }

        // A check-in that changes nothing mints nothing, and is answered with the head instead.
        if (head is not null && head.ContentHash == request.ContentHash)
        {
            return Task.FromResult(head);
        }

        return Task.FromResult(AddSnapshot(
            request.ContentHash,
            request.SizeBytes,
            request.ProfileRevision,
            isStale ? SavegameSnapshotOrigin.Forced : SavegameSnapshotOrigin.CheckedIn,
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

        AddSnapshot(request.ContentHash, request.SizeBytes, request.ProfileRevision, SavegameSnapshotOrigin.Created, null, request.Label);

        return Task.FromResult(_savegame);
    }

    public Task<GetSavegameSnapshotsResponse> GetSavegameSnapshotsV1Async(Guid repoId, Guid savegameId, int? skip = null, int? limit = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new GetSavegameSnapshotsResponse
        {
            Snapshots = [.. _snapshots],
            HeadSnapshot = _savegame.Head?.Number ?? 0,
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
    public Task ArchiveSavegameV1Async(Guid repoId, Guid savegameId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task RestoreSavegameV1Async(Guid repoId, Guid savegameId, RestoreRequest? request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<System.Collections.Generic.ICollection<SavegameDto>> GetArchivedSavegamesV1Async(Guid repoId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task DeleteSavegameSnapshotV1Async(Guid repoId, Guid savegameId, int number, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task DeleteSavegameV1Async(Guid repoId, Guid savegameId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<SavegameDto> UpdateSavegameV1Async(Guid repoId, Guid savegameId, UpdateSavegameRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    /// <summary>
    /// The swap, as the real endpoint performs it: this savegame takes the slot and whatever held it
    /// leaves. Only the seeded savegame exists here, so nothing is displaced and the answer says so.
    /// </summary>
    public Task<MakeSavegameCurrentResponse> MakeSavegameCurrentV1Async(Guid repoId, Guid savegameId, CancellationToken cancellationToken = default)
    {
        MadeCurrent++;

        _savegame = _savegame with { SupersededAt = null };

        return Task.FromResult(new MakeSavegameCurrentResponse { Savegame = _savegame, Superseded = null });
    }

    /// <summary>How many times a savegame was put back in its profile's slot.</summary>
    public int MadeCurrent { get; private set; }
    public Task<ICollection<SavegameDto>> GetSavegamesV1Async(Guid repoId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<SavegameSnapshotDto> RestoreSavegameSnapshotV1Async(Guid repoId, Guid savegameId, int number, RestoreSavegameSnapshotRequest? request = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<CreateModDownloadLinkResponse> CreateModDownloadLinkV1Async(CreateModDownloadLinkRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<CreateModUploadLinkResponse> CreateModUploadLinkV1Async(CreateModUploadLinkRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();


    public static string HashOf(byte[] content) => ModContentHasher.Format(SHA256.HashData(content));

    private SavegameSnapshotDto AddSnapshot(
        string contentHash,
        long sizeBytes,
        // Nullable like the wire shape it stands in for: a savegame that follows no mod list records
        // no revision, and the pair is null together with the profile above it.
        int? profileRevision,
        SavegameSnapshotOrigin origin,
        int? baseSnapshot,
        string? label = null)
    {
        var snapshot = new SavegameSnapshotDto
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
            BaseSnapshot = baseSnapshot
        };

        _snapshots.Add(snapshot);
        _savegame = _savegame with { Head = snapshot };

        return snapshot;
    }

    private SavegameCheckoutDto Checkout() => new()
    {
        Id = Guid.NewGuid(),
        RepoId = RepoId,
        SavegameId = _savegame.Id,
        User = new UserDto { Id = "me", DisplayName = "Me", Tag = "0002" },
        TakenAt = DateTime.UtcNow,
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

        // The real uploader reports as blocks go; one report at the end is the same contract.
        upload.BytesTransferred?.Report(bytes.Length);

        return hash;
    }
}


internal sealed class FakeSavegameDownloader(FakeSavegameServer server) : IModFileDownloader
{
    public int Downloads { get; private set; }


    public Task<ModFileDownload> OpenAsync(string link, IProgress<long>? bytesReceived, CancellationToken cancellationToken)
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
internal sealed class FakeSavegameAdapter(string root, params string[] slotIds) : ILocalSavegameAdapter
{
    public bool CanCreateSlots => false;

    /// <summary>What the game calls each save, keyed by slot - the name a picker shows.</summary>
    public Dictionary<string, string> DisplayNames { get; } = [];

    /// <summary>
    /// The folders this adapter answers with, keyed. One by default, named the way Farming
    /// Simulator's is, so an ordinary test reads exactly as a one-folder game does; a test about
    /// several folders adds them with <see cref="AddTarget"/>.
    /// </summary>
    public Dictionary<TargetKey, string> Folders { get; } = new() { [Keys.Target().Key] = root };


    public SavegameTargets SavegameTargets => new(Folders.Select(x => new SavegameTarget(x.Key, x.Key.Value, x.Value)));


    /// <summary>A second folder under the same adapter, which is what BeamNG's settings produce.</summary>
    public void AddTarget(TargetKey key, string folder) => Folders[key] = folder;

    /// <summary>
    /// Takes a folder away, the way emptying a settings field does. What is held behind it is not
    /// this adapter's business, which is the point of being able to do it from a test.
    /// </summary>
    public void RemoveTarget(TargetKey key) => Folders.Remove(key);

    public string GetSlotPath(SavegameTarget target, SavegameSlotId slot) => Path.Combine(target.Path, slot.Value);

    public Task<IReadOnlyList<SavegameSlot>> GetSlots(SavegameTarget target, CancellationToken cancellationToken)
    {
        IReadOnlyList<SavegameSlot> slots =
        [
            .. slotIds.Select(id => new SavegameSlot(
                new SavegameSlotId(id),
                DisplayNames.GetValueOrDefault(id),
                IsOccupied(target, id),
                []))
        ];

        return Task.FromResult(slots);
    }

    public ILocalSavegameAdapter WithLocalSettings(string serializedLocalSettings) => this;
    public ILocalSavegameAdapter WithLocalSettings(DynamicForm localSettings) => this;


    private static bool IsOccupied(SavegameTarget target, string slotId)
    {
        var path = Path.Combine(target.Path, slotId);

        return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
    }
}


internal sealed class FakeSavegameAdapters(ILocalSavegameAdapter? adapter) : ILocalSavegameAdapters
{
    public ILocalSavegameAdapter? TryGet(Game game) => adapter;

    public ILocalSavegameAdapter? TryGet(GameIdentity identity) => adapter;
}


/// <summary>What this client has been told the heads are. Empty is "not asked", never "unchanged".</summary>
internal sealed class FakeSavegameHeadSnapshots : ISavegameHeadSnapshots
{
    private readonly Dictionary<Guid, int> _heads = [];


    public void Set(Guid savegameId, int headSnapshot) => _heads[savegameId] = headSnapshot;

    public int? GetHeadSnapshot(Guid repoId, Guid savegameId)
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
/// The persisted games, in memory - <c>state.json</c> lives at a fixed path under LocalAppData,
/// and a test running against the real store would rewrite the developer's own game list.
/// </summary>
internal sealed class FakeGameState : IPersistedGameState
{
    private readonly Dictionary<GameIdentity, PersistedGame> _games = [];


    public int Saves { get; private set; }


    public void Add(GameIdentity identity, PersistedGame game) => _games[identity] = game;

    public PersistedGame? Find(GameIdentity identity)
        => _games.TryGetValue(identity, out var game) ? game : null;

    public void Save() => Saves++;
}
