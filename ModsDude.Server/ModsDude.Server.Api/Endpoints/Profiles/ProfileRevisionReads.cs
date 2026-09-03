using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// Reading a profile's history, as a projection.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here materializes a <see cref="ProfileRevision"/>. Its dependencies are an owned
/// collection, which EF loads with the entity whether or not anything asked - so a page of fifty
/// revisions of a two-thousand-mod profile would read a hundred thousand rows to render fifty lines
/// of "12 added, 3 changed".
/// </para>
/// <para>
/// The authors are resolved in a second query rather than joined. A page of revisions has a handful
/// of distinct authors between them, and the join would have to produce a nullable value object
/// inside a projection, which is exactly the kind of expression a provider declines to translate.
/// </para>
/// </remarks>
internal static class ProfileRevisionReads
{
    /// <summary>
    /// Newest first, because the interesting end of a history is the recent one, and windowed by
    /// offset.
    /// </summary>
    /// <remarks>
    /// An offset rather than a keyset: <see cref="RevisionNumber"/> is a value object, and a
    /// provider cannot translate a comparison on one - the same constraint the mod usage listing
    /// works around. New revisions arrive at the front, so a page read while somebody is saving can
    /// repeat a row. Acceptable for a list nothing acts on in bulk.
    /// </remarks>
    public static async Task<List<ProfileRevisionDto>> GetHistoryAsync(
        ApplicationDbContext dbContext,
        RepoId repoId, ProfileId profileId,
        int skip, int take,
        CancellationToken cancellationToken)
    {
        var rows = await Project(dbContext, repoId, profileId)
            .OrderByDescending(x => x.Number)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return await ToDtosAsync(dbContext, repoId, profileId, rows, cancellationToken);
    }

    /// <summary>One revision's entry, or <c>null</c> where the profile has no such revision.</summary>
    public static async Task<ProfileRevisionDto?> GetAsync(
        ApplicationDbContext dbContext,
        RepoId repoId, ProfileId profileId, RevisionNumber number,
        CancellationToken cancellationToken)
    {
        var row = await Project(dbContext, repoId, profileId)
            .FirstOrDefaultAsync(x => x.Number == number, cancellationToken);

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


    private static IQueryable<RevisionRow> Project(ApplicationDbContext dbContext, RepoId repoId, ProfileId profileId)
    {
        return dbContext.ProfileRevisions
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId)
            .Select(x => new RevisionRow(
                x.Number,
                x.Created,
                x.CreatedBy,
                x.Label,
                x.Origin,
                x.SourceProfileId,
                x.SourceRevision,
                x.ModCount,
                x.Changes.Added,
                x.Changes.Changed,
                x.Changes.Removed));
    }

    private static async Task<List<ProfileRevisionDto>> ToDtosAsync(
        ApplicationDbContext dbContext,
        RepoId repoId, ProfileId profileId,
        IReadOnlyList<RevisionRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var authorIds = rows.Select(x => x.CreatedBy).Distinct().ToList();

        var names = await dbContext.Users
            .Where(x => authorIds.Contains(x.Id))
            .Select(x => new { x.Id, x.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);

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


    private record RevisionRow(
        RevisionNumber Number,
        DateTime Created,
        UserId CreatedBy,
        string? Label,
        ProfileRevisionOrigin Origin,
        ProfileId? SourceProfileId,
        RevisionNumber? SourceRevision,
        int ModCount,
        int Added,
        int Changed,
        int Removed);
}
