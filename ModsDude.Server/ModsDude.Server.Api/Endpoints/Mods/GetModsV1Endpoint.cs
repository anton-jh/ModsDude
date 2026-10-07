using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.Changes;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Api.Endpoints.Mods;

/// <summary>
/// The repo's mod versions as a feed of changes: everything on a first read, and after that only what
/// was registered, changed or deleted since the last one.
/// </summary>
public class GetModsV1Endpoint : IEndpoint
{
    private const int _defaultLimit = 200;
    private const int _maximumLimit = 500;


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/mods", GetAll)
            .WithTags("Mods")
            .RequireRepoLevel(RepoMembershipLevel.Guest);
    }

    /// <param name="after">
    /// The <c>Sequence</c> of an earlier answer, or nothing for every version. Exclusive.
    /// </param>
    public static async Task<GetModsResponse> GetAll(
        Guid repoId,
        long? after,
        int? limit,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var changes = await ModChanges.ReadAsync(
            dbContext,
            new RepoId(repoId),
            Math.Max(after ?? 0, 0),
            Math.Clamp(limit ?? _defaultLimit, 1, _maximumLimit),
            cancellationToken);

        return new GetModsResponse(
            [.. changes.Versions.Select(ModDto.FromModel)],
            [.. changes.Deleted.Select(x => new ModVersionRefDto(x.ModId.Value, x.VersionId.Value))],
            changes.Sequence,
            changes.HasMore);
    }


    /// <param name="Mods">The versions that are new or changed, as they are now.</param>
    /// <param name="Deleted">
    /// The versions deleted. Applied before <paramref name="Mods"/>: a version deleted and registered
    /// again is listed in both, and exists.
    /// </param>
    /// <param name="Sequence">Where this answer ends; the <c>after</c> of the next read.</param>
    /// <param name="HasMore">Whether there is more up to now; read again from <paramref name="Sequence"/> straight away.</param>
    public record GetModsResponse(
        IReadOnlyList<ModDto> Mods,
        IReadOnlyList<ModVersionRefDto> Deleted,
        long Sequence,
        bool HasMore);

    public record ModVersionRefDto(string ModId, string VersionId);
}
