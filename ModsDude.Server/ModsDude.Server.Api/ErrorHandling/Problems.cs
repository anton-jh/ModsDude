using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
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

    public static CustomProblemDetails ModPinnedTwice(ModId modId) => new()
    {
        Type = ProblemType.ModDependencyExists,
        Title = "A mod is pinned twice",
        Detail = $"The mod list pins mod '{modId.Value}' more than once. A profile pins each mod at exactly one version."
    };

    /// <summary>
    /// The save was based on a revision that is no longer the head - somebody else saved the profile
    /// while this one was being edited. <paramref name="head"/> is carried so the client can say what
    /// it is now rather than only that it is not what was sent.
    /// </summary>
    public static CustomProblemDetails ProfileRevisionStale(ProfileId profileId, RevisionNumber basedOn, RevisionNumber head) => new()
    {
        Type = ProblemType.ProfileRevisionStale,
        Title = "The profile changed while you were editing it",
        Detail = $"This save is based on revision {basedOn.Value} of profile '{profileId.Value}', which is now at revision {head.Value}. Reload the mod list and save again."
    };

    /// <summary>
    /// The check-in was built on a version that is no longer the head - somebody took the save over
    /// and checked in while this one was being played. <paramref name="head"/> is carried so the
    /// client can say what it is now, and so the user can decide to force past it: forcing is
    /// allowed, and records the fork rather than hiding it.
    /// </summary>
    public static CustomProblemDetails SavegameVersionStale(SavegameId savegameId, SavegameVersionNumber basedOn, SavegameVersionNumber head) => new()
    {
        Type = ProblemType.SavegameVersionStale,
        Title = "The savegame changed while you were playing it",
        Detail = $"This check-in is based on version {basedOn.Value} of savegame '{savegameId.Value}', which is now at version {head.Value}. Somebody else checked in while you were away."
    };

    /// <summary>
    /// Two people took the same savegame in the same instant, and the one-open-claim index let
    /// exactly one through. Taking a save from somebody is allowed; taking it from two people at
    /// once is not a state the log can represent.
    /// </summary>
    public static CustomProblemDetails SavegameCheckoutConflict(SavegameId savegameId) => new()
    {
        Type = ProblemType.SavegameCheckoutConflict,
        Title = "Somebody took the savegame at the same moment",
        Detail = $"Savegame '{savegameId.Value}' was claimed by somebody else while this request was being made. Reload and decide again."
    };

    public static CustomProblemDetails SavegameNotCheckedOut(SavegameId savegameId) => new()
    {
        Type = ProblemType.SavegameNotCheckedOut,
        Title = "Nobody has this savegame checked out",
        Detail = $"Savegame '{savegameId.Value}' has no open checkout, so there is none to renew or give back."
    };

    /// <summary>
    /// The content hash is a blob path segment, so an unparseable one has to be refused at the
    /// boundary rather than carried inward. There is no global exception handler, and the storage
    /// layer validates the same value on its way to building a blob name — without this the caller
    /// gets a 500 for what is plainly a bad request. Reuses <see cref="ProblemType.InvalidHash"/>
    /// rather than minting a savegame-specific type: it is the same fact about the same kind of
    /// value, and every problem type is a wire contract the generated client has to learn.
    /// </summary>
    public static CustomProblemDetails InvalidSavegameContentHash(string contentHash) => new()
    {
        Type = ProblemType.InvalidHash,
        Title = "Not a valid savegame address",
        Detail = $"'{contentHash}' is not a lowercase hex SHA-256."
    };

    public static CustomProblemDetails SavegameFileDoesNotExist(RepoId repoId, SavegameId savegameId, string contentHash) => new()
    {
        Type = ProblemType.FileNotFound,
        Title = "Cannot find file for savegame version",
        Detail = $"Nothing is stored for repo '{repoId.Value}', savegame '{savegameId.Value}' at content hash '{contentHash}'."
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

    /// <summary>
    /// Returned at 401, and only ever for a caller the server cannot identify — a token carrying no
    /// usable <c>sub</c>, or one whose subject has no user row. Everything reached through
    /// <see cref="AuthorizationResultExtensions.MapToForbidden(Application.Authorization.AuthorizationResult?)"/>
    /// is a 403 instead: the endpoint group requires authentication, so a request that reaches a
    /// handler at all has already proved who it is and can only be refused for what it may do.
    /// </summary>
    public static CustomProblemDetails NotAuthenticated => new()
    {
        Type = ProblemType.NotAuthenticated,
        Title = "Not authenticated",
        Detail = "The request carries no identity this server can act on."
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
        Detail = $"The images offered for version '{modVersionId.Value}' of mod '{modId.Value}' in repo '{repoId.Value}' contain more than one icon of a rendition, or two images of a kind at the same rendition and position."
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

    public static CustomProblemDetails InviteNotFound => new()
    {
        Type = ProblemType.InviteNotFound,
        Title = "No such invite",
        Detail = "No invite has that code."
    };

    public static CustomProblemDetails InviteNotUsable(InviteStatus status)
    {
        var reason = status switch
        {
            InviteStatus.Expired => "This invite has expired.",
            InviteStatus.Exhausted => "This invite has been used as many times as it was meant for.",
            InviteStatus.Revoked => "This invite has been revoked.",
            _ => throw new UnreachableException("An active invite is usable")
        };

        return new()
        {
            Type = ProblemType.InviteNotUsable,
            Title = "Invite no longer works",
            Detail = reason + " Ask for a new one."
        };
    }

    public static CustomProblemDetails InviteRedemptionConflict => new()
    {
        Type = ProblemType.InviteRedemptionConflict,
        Title = "The invite changed while you were joining",
        Detail = "Somebody else used or revoked this invite at the same moment. Try again."
    };

    public static CustomProblemDetails InviteCannotGrantAdmin => new()
    {
        Type = ProblemType.InviteCannotGrantAdmin,
        Title = "An invite cannot grant Admin",
        Detail = "A code can travel further than it was meant to. Invite at Member or Guest, then promote the person once they are in."
    };

    public static CustomProblemDetails InvalidInviteLimits(string detail) => new()
    {
        Type = ProblemType.InvalidInviteLimits,
        Title = "Invite limits do not make sense",
        Detail = detail
    };

    /// <summary>
    /// A savegame follows the profile, or a version of one was played on a revision of it. Either
    /// makes the profile undeletable - the same bargain as a pinned mod version one aggregate down,
    /// and reported here rather than left to surface as a foreign key violation.
    /// </summary>
    public static CustomProblemDetails ProfileInUseBySavegame(RepoId repoId, ProfileId profileId) => new()
    {
        Type = ProblemType.ProfileInUseBySavegame,
        Title = "The profile is used by a savegame",
        Detail = $"Profile '{profileId.Value}' cannot be deleted from repo '{repoId.Value}' while a savegame follows it or was played on one of its revisions."
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

        [EnumMember(Value = _typeBaseUri + "not-authenticated")]
        [JsonStringEnumMemberName(_typeBaseUri + "not-authenticated")]
        NotAuthenticated,

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

        [EnumMember(Value = _typeBaseUri + "invite-not-found")]
        [JsonStringEnumMemberName(_typeBaseUri + "invite-not-found")]
        InviteNotFound,

        [EnumMember(Value = _typeBaseUri + "invite-not-usable")]
        [JsonStringEnumMemberName(_typeBaseUri + "invite-not-usable")]
        InviteNotUsable,

        [EnumMember(Value = _typeBaseUri + "invite-redemption-conflict")]
        [JsonStringEnumMemberName(_typeBaseUri + "invite-redemption-conflict")]
        InviteRedemptionConflict,

        [EnumMember(Value = _typeBaseUri + "invalid-invite-limits")]
        [JsonStringEnumMemberName(_typeBaseUri + "invalid-invite-limits")]
        InvalidInviteLimits,

        [EnumMember(Value = _typeBaseUri + "invite-cannot-grant-admin")]
        [JsonStringEnumMemberName(_typeBaseUri + "invite-cannot-grant-admin")]
        InviteCannotGrantAdmin,

        [EnumMember(Value = _typeBaseUri + "profile-revision-stale")]
        [JsonStringEnumMemberName(_typeBaseUri + "profile-revision-stale")]
        ProfileRevisionStale,

        [EnumMember(Value = _typeBaseUri + "savegame-version-stale")]
        [JsonStringEnumMemberName(_typeBaseUri + "savegame-version-stale")]
        SavegameVersionStale,

        [EnumMember(Value = _typeBaseUri + "savegame-checkout-conflict")]
        [JsonStringEnumMemberName(_typeBaseUri + "savegame-checkout-conflict")]
        SavegameCheckoutConflict,

        [EnumMember(Value = _typeBaseUri + "savegame-not-checked-out")]
        [JsonStringEnumMemberName(_typeBaseUri + "savegame-not-checked-out")]
        SavegameNotCheckedOut,

        [EnumMember(Value = _typeBaseUri + "profile-in-use-by-savegame")]
        [JsonStringEnumMemberName(_typeBaseUri + "profile-in-use-by-savegame")]
        ProfileInUseBySavegame,
    }
}
