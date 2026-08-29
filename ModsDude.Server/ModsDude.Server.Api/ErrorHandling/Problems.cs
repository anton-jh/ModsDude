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
    }
}
