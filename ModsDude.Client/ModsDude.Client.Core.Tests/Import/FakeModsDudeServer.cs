using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Tests.Import;

/// <summary>
/// Just enough of the two endpoints import uses to hold the rules it depends on: the upload link's
/// two refusals, the never-register-before-upload check, and the placement assertion. Everything is
/// recorded in one ordered journal, because most of what is worth asserting about an import is the
/// order things happened in.
/// </summary>
internal sealed class FakeModsDudeServer : IFilesClient, IModsClient
{
    public const string ContentHashMetadataKey = "sha256";

    private readonly Lock _lock = new();
    private readonly List<ServerCall> _journal = [];
    private readonly Dictionary<ModVersionIdentity, string?> _stored = [];
    private readonly Dictionary<ModKey, List<ModDto>> _registered = [];


    public Guid RepoId { get; } = Guid.NewGuid();

    /// <summary>Runs before a registration is applied, which is where a teammate's change lands.</summary>
    public Func<ModVersionIdentity, Task>? BeforeRegister { get; set; }

    public IReadOnlyList<ServerCall> Journal
    {
        get
        {
            lock (_lock)
            {
                return [.. _journal];
            }
        }
    }


    public IReadOnlyList<ModVersionKey> VersionsOf(ModKey modId)
    {
        lock (_lock)
        {
            return _registered.TryGetValue(modId, out var versions)
                ? [.. versions.OrderBy(x => x.SequenceNumber).Select(x => ModVersionKey.From(x.VersionId))]
                : [];
        }
    }

    public string? RecordedHash(ModVersionIdentity identity)
    {
        lock (_lock)
        {
            return _stored.GetValueOrDefault(identity);
        }
    }

    public IReadOnlyList<ModVersionIdentity> CallsOf(ServerCallKind kind)
        => [.. Journal.Where(x => x.Kind == kind).Select(x => x.Identity)];

    /// <summary>A blob a failed import left behind: stored, and registered against by nothing.</summary>
    public void PlaceOrphan(ModVersionIdentity identity, string? recordedHash)
    {
        lock (_lock)
        {
            _stored[identity] = recordedHash;
        }
    }

    public void Seed(ModKey modId, params string[] versions)
    {
        foreach (var version in versions)
        {
            Apply(new ModVersionIdentity(modId, ModVersionKey.From(version)), "seeded", null, null, assertPlacement: false);
        }
    }

    /// <summary>What another member registering mid-import looks like from here.</summary>
    public void RegisterElsewhere(ModVersionIdentity identity, ModVersionKey? after, ModVersionKey? before)
    {
        _stored[identity] = "elsewhere";
        Apply(identity, "elsewhere", after, before, assertPlacement: false);
    }

    public string MakeLink(ModVersionIdentity identity) => $"fake://{identity.ModId}/{identity.VersionId}";

    /// <summary>Called by the fake uploader, which is the only thing that can make a blob appear.</summary>
    public void CompleteUpload(string link, string contentHash)
    {
        var parts = link["fake://".Length..].Split('/');
        var identity = new ModVersionIdentity(ModKey.From(parts[0]), ModVersionKey.From(parts[1]));

        lock (_lock)
        {
            _stored[identity] = contentHash;
            _journal.Add(new ServerCall(ServerCallKind.Upload, identity));
        }
    }


    public Task<CreateModUploadLinkResponse> CreateModUploadLinkV1Async(CreateModUploadLinkRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var identity = new ModVersionIdentity(ModKey.From(request.ModId), ModVersionKey.From(request.VersionId));

        lock (_lock)
        {
            _journal.Add(new ServerCall(ServerCallKind.Link, identity));

            if (IsRegistered(identity))
            {
                throw Problem(ProblemType.AlreadyRegistered, null);
            }

            if (_stored.TryGetValue(identity, out var recorded))
            {
                // The orphan case. Carrying the recorded hash is what lets the client tell its own
                // failed upload from a different build wearing the same id and version.
                throw Problem(ProblemType.FileAlreadyPresent, recorded);
            }
        }

        return Task.FromResult(new CreateModUploadLinkResponse()
        {
            Link = MakeLink(identity),
            ContentHashMetadataKey = ContentHashMetadataKey
        });
    }

    public async Task<ModDto> RegisterModV1Async(Guid repoId, RegisterModRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var identity = new ModVersionIdentity(ModKey.From(request.ModId), ModVersionKey.From(request.VersionId));

        if (BeforeRegister is not null)
        {
            await BeforeRegister(identity);
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _journal.Add(new ServerCall(ServerCallKind.Register, identity));

            // Metadata is never written for a file nobody has.
            if (_stored.ContainsKey(identity) is false)
            {
                throw Problem(ProblemType.FileNotFound, null);
            }

            if (IsRegistered(identity))
            {
                throw Problem(ProblemType.AlreadyExists, null);
            }
        }

        var after = request.Placement.After is null ? (ModVersionKey?)null : ModVersionKey.From(request.Placement.After);
        var before = request.Placement.Before is null ? (ModVersionKey?)null : ModVersionKey.From(request.Placement.Before);

        return Apply(identity, request.ContentHash, after, before, assertPlacement: true);
    }

    public Task<GetModsResponse> GetModsV1Async(Guid repoId, DateTime? updatedAfter = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _journal.Add(new ServerCall(ServerCallKind.GetMods, default));

            return Task.FromResult(new GetModsResponse()
            {
                Mods = [.. _registered.Values.SelectMany(x => x)],
                NextCursor = null
            });
        }
    }


    public Task<CreateModDownloadLinkResponse> CreateModDownloadLinkV1Async(CreateModDownloadLinkRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task DeleteModV1Async(Guid repoId, string modId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task DeleteModVersionV1Async(Guid repoId, string modId, string versionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<ModDto> SetModVersionImagesV1Async(Guid repoId, string modId, string versionId, SetModVersionImagesRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<GetModUsageResponse> GetModUsageV1Async(Guid repoId, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();


    private ModDto Apply(ModVersionIdentity identity, string contentHash, ModVersionKey? after, ModVersionKey? before, bool assertPlacement)
    {
        lock (_lock)
        {
            if (_registered.TryGetValue(identity.ModId, out var versions) is false)
            {
                _registered[identity.ModId] = versions = [];
            }

            var order = versions.OrderBy(x => x.SequenceNumber).Select(x => ModVersionKey.From(x.VersionId)).ToList();

            var afterPosition = after is null ? -1 : order.IndexOf(after.Value);
            var beforePosition = before is null ? order.Count : order.IndexOf(before.Value);

            if (assertPlacement)
            {
                // Both neighbours, exactly as ModVersionSequencer does it: naming only one stops
                // collisions but still allows a silently wrong order.
                if ((after is not null && afterPosition < 0)
                    || (before is not null && beforePosition < 0)
                    || beforePosition != afterPosition + 1)
                {
                    throw Problem(ProblemType.VersionPlacementConflict, null);
                }
            }
            else
            {
                beforePosition = Math.Max(0, Math.Min(beforePosition, order.Count));
            }

            var dto = new ModDto()
            {
                ModId = identity.ModId.Value,
                VersionId = identity.VersionId.Value,
                DisplayName = identity.ModId.Value,
                Description = string.Empty,
                ContentHash = contentHash,
                Locked = false,
                Attributes = [],
                Images = [],
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow
            };

            order.Insert(beforePosition, identity.VersionId);
            versions.Add(dto);

            foreach (var version in versions)
            {
                version.SequenceNumber = order.IndexOf(ModVersionKey.From(version.VersionId));
            }

            return dto;
        }
    }

    private bool IsRegistered(ModVersionIdentity identity)
        => _registered.TryGetValue(identity.ModId, out var versions)
        && versions.Any(x => ModVersionKey.From(x.VersionId) == identity.VersionId);

    private static ApiException<CustomProblemDetails> Problem(ProblemType type, string? contentHash)
        => new(type.ToString(), 400, null, new Dictionary<string, IEnumerable<string>>(),
            new CustomProblemDetails() { Type = type, ContentHash = contentHash }, null);
}


internal enum ServerCallKind
{
    Link,
    Upload,
    Register,
    GetMods
}

internal readonly record struct ServerCall(ServerCallKind Kind, ModVersionIdentity Identity);
