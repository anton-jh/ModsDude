using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace ModsDude.Server.Api.ErrorHandling;

public static class Problems
{
    private const string _typeBaseUri = "https://server.modsdude.com/api/problems/";


    public static CustomProblemDetails NameTaken(string name) => new()
    {
        Type = ProblemType.NameTaken,
        Title = "Name taken",
        Detail = $"A resource with the name '{name}' already exists.",
    };

    public static CustomProblemDetails NotFound => new()
    {
        Type = ProblemType.NotFound,
        Title = "Not found",
        Detail = $"The requested resource does not exist."
    };

    public static CustomProblemDetails ModDependencyExists(Profile profile, ModId modId) => new()
    {
        Type = ProblemType.ModDependencyExists,
        Title = "Profile already has a dependency on mod",
        Detail = $"The profile '{profile.Id.Value}' already has a dependency on mod '{modId.Value}'."
    };

    public static CustomProblemDetails InsufficientRepoAccess(RepoMembershipLevel minimumLevel)
    {
        var levelText =
            minimumLevel == RepoMembershipLevel.Guest ? "Guest" :
            minimumLevel == RepoMembershipLevel.Member ? "Member" :
            minimumLevel == RepoMembershipLevel.Admin ? "Admin" :
            throw new UnreachableException();

        return new()
        {
            Type = ProblemType.InsufficientRepoAccess,
            Title = "Insufficient Repo access",
            Detail = $"The operation requires a Repo membership level of '{levelText}' or greater."
        };
    }

    public static CustomProblemDetails NotAuthorized => new()
    {
        Type = ProblemType.NotAuthorized,
        Title = "Not authorized",
        Detail = $"You are not authorized to perform this operation."
    };

    public static CustomProblemDetails CannotDemoteOnlyAdmin => new()
    {
        Type = ProblemType.CannotDemoteOnlyAdmin,
        Title = "Cannot demote last admin",
        Detail = "You cannot demote the only admin of the repo."
    };

    public static CustomProblemDetails CannotKickOnlyAdmin => new()
    {
        Type = ProblemType.CannotKickOnlyAdmin,
        Title = "Cannot kick last admin",
        Detail = "You cannot kick the only admin of the repo."
    };

    public static CustomProblemDetails ModVersionAlreadyExists(RepoId repoId, ModId modId, ModVersionId modVersionId) => new()
    {
        Type = ProblemType.AlreadyExists,
        Title = "The mod version already exists",
        Detail = $"Repo '{repoId.Value}' already contains a mod version '{modVersionId.Value}' in mod '{modId.Value}'."
    };

    public static CustomProblemDetails ModFileDoesNotExist(RepoId repoId, ModId modId, ModVersionId modVersionId) => new()
    {
        Type = ProblemType.FileNotFound,
        Title = "Cannot find file for mod version",
        Detail = $"Cannot find file for repo '{repoId.Value}', mod '{modId.Value}' and version '{modVersionId.Value}'."
    };

    public static CustomProblemDetails ImageDoesNotExist(string hash) => new()
    {
        Type = ProblemType.FileNotFound,
        Title = "Cannot find image",
        Detail = $"Nothing is stored at image address '{hash}'."
    };

    /// <summary>
    /// The version is registered, so the client has nothing left to do for it — a success for the
    /// import flow, and the case a teammate registering the same version concurrently produces.
    /// </summary>
    public static CustomProblemDetails ModVersionAlreadyRegistered(RepoId repoId, ModId modId, ModVersionId modVersionId) => new()
    {
        Type = ProblemType.AlreadyRegistered,
        Title = "The mod version is already registered",
        Detail = $"Repo '{repoId.Value}' already contains a mod version '{modVersionId.Value}' in mod '{modId.Value}'."
    };

    /// <summary>
    /// An unregistered blob is already at the address, so no upload link can be minted — but the
    /// registration the blob is missing can still be made. <paramref name="contentHash"/> is what
    /// makes that safe: matching it means this is the client's own orphan from a failed import and
    /// registering describes the bytes that are really there, differing means an id/version
    /// collision between two different builds, which must be reported rather than registered over.
    /// It is <c>null</c> only when the blob predates the metadata being recorded, in which case the
    /// client has established nothing and must not register.
    /// </summary>
    public static CustomProblemDetails ModFileAlreadyPresent(RepoId repoId, ModId modId, ModVersionId modVersionId, string? contentHash) => new()
    {
        Type = ProblemType.FileAlreadyPresent,
        Title = "A file for the mod version is already stored",
        Detail = $"A file for repo '{repoId.Value}', mod '{modId.Value}' and version '{modVersionId.Value}' is already stored, but no version is registered against it.",
        ContentHash = contentHash
    };

    public static CustomProblemDetails ModVersionInUse(RepoId repoId, ModId modId, ModVersionId modVersionId) => new()
    {
        Type = ProblemType.ModInUse,
        Title = "The mod version is used by a profile",
        Detail = $"Version '{modVersionId.Value}' of mod '{modId.Value}' cannot be deleted from repo '{repoId.Value}' while a profile depends on it."
    };

    public static CustomProblemDetails ModInUse(RepoId repoId, ModId modId) => new()
    {
        Type = ProblemType.ModInUse,
        Title = "The mod is used by a profile",
        Detail = $"Mod '{modId.Value}' cannot be deleted from repo '{repoId.Value}' while a profile depends on one of its versions."
    };

    public static CustomProblemDetails CannotDeleteOnlyModVersion(RepoId repoId, ModId modId, ModVersionId modVersionId) => new()
    {
        Type = ProblemType.CannotDeleteOnlyModVersion,
        Title = "Cannot delete the only version of a mod",
        Detail = $"Version '{modVersionId.Value}' is the only version of mod '{modId.Value}' in repo '{repoId.Value}'. Delete the mod instead."
    };

    public static CustomProblemDetails ImageHashMismatch(string expectedHash, string actualHash) => new()
    {
        Type = ProblemType.HashMismatch,
        Title = "The uploaded bytes do not hash to the address",
        Detail = $"The upload addressed '{expectedHash}' but the bytes hash to '{actualHash}'."
    };

    public static CustomProblemDetails InvalidImageHash(string hash) => new()
    {
        Type = ProblemType.InvalidHash,
        Title = "Not a valid image address",
        Detail = $"'{hash}' is not a lowercase hex SHA-256."
    };

    public static CustomProblemDetails InvalidImageSet(RepoId repoId, ModId modId, ModVersionId modVersionId) => new()
    {
        Type = ProblemType.InvalidImageSet,
        Title = "The image references do not describe a coherent gallery",
        Detail = $"The images offered for version '{modVersionId.Value}' of mod '{modId.Value}' in repo '{repoId.Value}' contain more than one icon, or two images of a kind at the same position."
    };

    public static CustomProblemDetails BatchTooLarge(int size, int maximum) => new()
    {
        Type = ProblemType.BatchTooLarge,
        Title = "Too many items in one request",
        Detail = $"The request carries {size} items; at most {maximum} are accepted at a time."
    };

    public static CustomProblemDetails InvalidCursor(string cursor) => new()
    {
        Type = ProblemType.InvalidCursor,
        Title = "Not a valid pagination cursor",
        Detail = $"'{cursor}' is not a cursor this endpoint issued. Restart the listing without one."
    };

    public static CustomProblemDetails UserAlreadyMember(RepoId repoId, UserId userId) => new()
    {
        Type = ProblemType.UserAlreadyMember,
        Title = "User is already a member of this repo",
        Detail = $"User '{userId.Value}' is already a member of '{repoId.Value}'."
    };

    public static CustomProblemDetails VersionPlacementConflict(RepoId repoId, ModId modId) => new()
    {
        Type = ProblemType.VersionPlacementConflict,
        Title = "Version placement no longer matches the ordering",
        Detail = $"The requested placement for a version of mod '{modId.Value}' in repo '{repoId.Value}' no longer matches the version order. Refetch the mod's versions, recompute the placement and retry."
    };

    public static CustomProblemDetails RepoNotEmpty(RepoId repoId) => new()
    {
        Type = ProblemType.RepoNotEmpty,
        Title = "Repo is not empty",
        Detail = $"Repo '{repoId.Value}' still has registered mods. Remove them before deleting the repo."
    };


    /// <summary>
    /// Every member carries the same URI twice, and both are load bearing.
    /// <see cref="EnumMemberAttribute"/> is what NJsonSchema writes into the OpenAPI document, and
    /// therefore what the generated client expects; System.Text.Json ignores it and only honours
    /// <see cref="JsonStringEnumMemberNameAttribute"/>. With only the first, the wire value is the
    /// bare member name and no client can match it against the schema it was generated from.
    /// </summary>
    public enum ProblemType
    {
        [EnumMember(Value = _typeBaseUri + "name-taken")]
        [JsonStringEnumMemberName(_typeBaseUri + "name-taken")]
        NameTaken,

        [EnumMember(Value = _typeBaseUri + "not-found")]
        [JsonStringEnumMemberName(_typeBaseUri + "not-found")]
        NotFound,

        [EnumMember(Value = _typeBaseUri + "mod-dependency-exists")]
        [JsonStringEnumMemberName(_typeBaseUri + "mod-dependency-exists")]
        ModDependencyExists,

        [EnumMember(Value = _typeBaseUri + "insufficient-repo-access")]
        [JsonStringEnumMemberName(_typeBaseUri + "insufficient-repo-access")]
        InsufficientRepoAccess,

        [EnumMember(Value = _typeBaseUri + "not-authorized")]
        [JsonStringEnumMemberName(_typeBaseUri + "not-authorized")]
        NotAuthorized,

        [EnumMember(Value = _typeBaseUri + "cannot-kick-only-admin")]
        [JsonStringEnumMemberName(_typeBaseUri + "cannot-kick-only-admin")]
        CannotKickOnlyAdmin,

        [EnumMember(Value = _typeBaseUri + "cannot-demote-only-admin")]
        [JsonStringEnumMemberName(_typeBaseUri + "cannot-demote-only-admin")]
        CannotDemoteOnlyAdmin,

        [EnumMember(Value = _typeBaseUri + "already-exists")]
        [JsonStringEnumMemberName(_typeBaseUri + "already-exists")]
        AlreadyExists,

        [EnumMember(Value = _typeBaseUri + "file-not-found")]
        [JsonStringEnumMemberName(_typeBaseUri + "file-not-found")]
        FileNotFound,

        [EnumMember(Value = _typeBaseUri + "user-already-member")]
        [JsonStringEnumMemberName(_typeBaseUri + "user-already-member")]
        UserAlreadyMember,

        [EnumMember(Value = _typeBaseUri + "repo-not-empty")]
        [JsonStringEnumMemberName(_typeBaseUri + "repo-not-empty")]
        RepoNotEmpty,

        [EnumMember(Value = _typeBaseUri + "version-placement-conflict")]
        [JsonStringEnumMemberName(_typeBaseUri + "version-placement-conflict")]
        VersionPlacementConflict,

        [EnumMember(Value = _typeBaseUri + "file-already-present")]
        [JsonStringEnumMemberName(_typeBaseUri + "file-already-present")]
        FileAlreadyPresent,

        [EnumMember(Value = _typeBaseUri + "already-registered")]
        [JsonStringEnumMemberName(_typeBaseUri + "already-registered")]
        AlreadyRegistered,

        [EnumMember(Value = _typeBaseUri + "mod-in-use")]
        [JsonStringEnumMemberName(_typeBaseUri + "mod-in-use")]
        ModInUse,

        [EnumMember(Value = _typeBaseUri + "cannot-delete-only-mod-version")]
        [JsonStringEnumMemberName(_typeBaseUri + "cannot-delete-only-mod-version")]
        CannotDeleteOnlyModVersion,

        [EnumMember(Value = _typeBaseUri + "hash-mismatch")]
        [JsonStringEnumMemberName(_typeBaseUri + "hash-mismatch")]
        HashMismatch,

        [EnumMember(Value = _typeBaseUri + "invalid-hash")]
        [JsonStringEnumMemberName(_typeBaseUri + "invalid-hash")]
        InvalidHash,

        [EnumMember(Value = _typeBaseUri + "invalid-image-set")]
        [JsonStringEnumMemberName(_typeBaseUri + "invalid-image-set")]
        InvalidImageSet,

        [EnumMember(Value = _typeBaseUri + "batch-too-large")]
        [JsonStringEnumMemberName(_typeBaseUri + "batch-too-large")]
        BatchTooLarge,

        [EnumMember(Value = _typeBaseUri + "invalid-cursor")]
        [JsonStringEnumMemberName(_typeBaseUri + "invalid-cursor")]
        InvalidCursor,
    }
}
