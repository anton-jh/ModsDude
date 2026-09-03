using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// A profile's history, as the API answers with it.
/// </summary>
/// <remarks>
/// <para>
/// The query itself lives in <see cref="ProfileRevisionExtensions"/>, with the rest of the
/// revision vocabulary and where the persistence suite can run it against a real PostgreSQL. This
/// is the mapping either side of it: rows in, DTOs out, and the authors named.
/// </para>
/// <para>
/// The authors are resolved in a second query rather than joined. A page of revisions has a handful
/// of distinct authors between them, and the join would have to produce a nullable value object
/// inside a projection, which is exactly the kind of expression a provider declines to translate.
/// </para>
/// </remarks>
internal static class ProfileRevisionReads
{
    public static async Task<List<ProfileRevisionDto>> GetHistoryAsync(
        ApplicationDbContext dbContext,
        RepoId repoId, ProfileId profileId,
        int skip, int take,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.ProfileRevisions.GetHistoryAsync(repoId, profileId, skip, take, cancellationToken);

        return await ToDtosAsync(dbContext, repoId, profileId, rows, cancellationToken);
    }

    /// <summary>One revision's entry, or <c>null</c> where the profile has no such revision.</summary>
    public static async Task<ProfileRevisionDto?> GetAsync(
        ApplicationDbContext dbContext,
        RepoId repoId, ProfileId profileId, RevisionNumber number,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.ProfileRevisions.GetRowAsync(repoId, profileId, number, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var dtos = await ToDtosAsync(dbContext, repoId, profileId, [row], cancellationToken);

        return dtos[0];
    }

    /// <summary>
    /// The author, named. A revision records who made it as a <see cref="UserId"/> and there is no
    /// foreign key holding that user in place, so a name that cannot be resolved falls back to the
    /// id rather than dropping the revision out of the history.
    /// </summary>
    public static UserDto Describe(UserId userId, DisplayName? displayName)
        => new(userId.Value, displayName?.Value ?? userId.Value, UserTag.For(userId));


    private static async Task<List<ProfileRevisionDto>> ToDtosAsync(
        ApplicationDbContext dbContext,
        RepoId repoId, ProfileId profileId,
        IReadOnlyList<ProfileRevisionRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var names = await dbContext.Users.GetDisplayNamesAsync(
            [.. rows.Select(x => x.CreatedBy).Distinct()],
            cancellationToken);

        return
        [
            .. rows.Select(row => new ProfileRevisionDto(
                repoId.Value,
                profileId.Value,
                row.Number.Value,
                row.Created,
                Describe(row.CreatedBy, names.TryGetValue(row.CreatedBy, out var name) ? name : null),
                row.Label,
                row.Origin,
                row.SourceProfileId?.Value,
                row.SourceRevision?.Value,
                row.ModCount,
                new ProfileRevisionChangesDto(row.Added, row.Changed, row.Removed)))
        ];
    }
}
