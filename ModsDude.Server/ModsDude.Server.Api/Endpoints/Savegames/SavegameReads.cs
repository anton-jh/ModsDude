using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// A savegame, its versions and its claims, as the API answers with them.
/// </summary>
/// <remarks>
/// <para>
/// The queries themselves live in <see cref="SavegameExtensions"/>, with the rest of the savegame
/// vocabulary and where the persistence suite can run them against a real PostgreSQL. This is the
/// mapping either side of them: rows in, DTOs out, and the people named.
/// </para>
/// <para>
/// <b>Names are resolved in one further query, never joined and never per row.</b> A savegame list
/// is a handful of distinct people between all its heads and all its claims, and the join would have
/// to produce a nullable value object inside a projection - exactly the expression a provider
/// declines to translate. It is also what keeps the whole list to a fixed number of round trips:
/// savegames, their heads, their open claims, and the names, whatever the repo holds.
/// </para>
/// </remarks>
internal static class SavegameReads
{
    /// <summary>
    /// The person, named. A version records who made it as a <see cref="UserId"/> and there is no
    /// foreign key holding that user in place, so a name that cannot be resolved falls back to the
    /// id rather than dropping the row out of the history.
    /// </summary>
    public static UserDto Describe(UserId userId, DisplayName? displayName)
        => new(userId.Value, displayName?.Value ?? userId.Value, UserTag.For(userId));


    /// <summary>
    /// Every savegame in the repo, each carrying its head version and its open claim.
    /// </summary>
    public static async Task<List<SavegameDto>> GetListAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Savegames.GetRowsAsync(repoId, cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var heads = await dbContext.SavegameVersions.GetHeadVersionsAsync(
            repoId,
            rows.ToDictionary(x => x.Id, x => x.HeadVersion),
            cancellationToken);

        var checkouts = await dbContext.SavegameCheckouts.GetOpenCheckoutsAsync(repoId, cancellationToken);

        var names = await GetNamesAsync(
            dbContext,
            [.. heads.Select(x => x.CreatedBy), .. checkouts.Select(x => x.UserId)],
            cancellationToken);

        var headsBySavegame = heads.ToDictionary(x => x.SavegameId);
        var checkoutsBySavegame = checkouts.ToDictionary(x => x.SavegameId);

        return
        [
            .. rows.Select(row => new SavegameDto(
                row.Id.Value,
                repoId.Value,
                row.Name.Value,
                row.ProfileId.Value,
                row.Created,
                headsBySavegame.TryGetValue(row.Id, out var head) ? ToDto(repoId, head, names) : null,
                checkoutsBySavegame.TryGetValue(row.Id, out var checkout) ? ToDto(checkout, names, now) : null))
        ];
    }

    /// <summary>
    /// One savegame, in the same shape the list gives it. Takes the entity because every caller that
    /// wants one has just loaded it to change it.
    /// </summary>
    public static async Task<SavegameDto> DescribeAsync(
        ApplicationDbContext dbContext,
        Savegame savegame,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var head = await dbContext.SavegameVersions.GetRowAsync(
            savegame.RepoId, savegame.Id, savegame.HeadVersion, cancellationToken);

        var checkout = await dbContext.SavegameCheckouts.GetOpenCheckoutAsync(
            savegame.RepoId, savegame.Id, cancellationToken);

        var userIds = new List<UserId>();
        if (head is not null)
        {
            userIds.Add(head.CreatedBy);
        }
        if (checkout is not null)
        {
            userIds.Add(checkout.UserId);
        }

        var names = await GetNamesAsync(dbContext, userIds, cancellationToken);

        return new SavegameDto(
            savegame.Id.Value,
            savegame.RepoId.Value,
            savegame.Name.Value,
            savegame.ProfileId.Value,
            savegame.Created,
            head is null ? null : ToDto(savegame.RepoId, head, names),
            checkout is null ? null : ToDto(checkout, names, now));
    }

    public static async Task<List<SavegameVersionDto>> ToDtosAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        IReadOnlyList<SavegameVersionRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var names = await GetNamesAsync(dbContext, rows.Select(x => x.CreatedBy), cancellationToken);

        return [.. rows.Select(row => ToDto(repoId, row, names))];
    }

    public static async Task<SavegameVersionDto> ToDtoAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        SavegameVersionRow row,
        CancellationToken cancellationToken)
    {
        var dtos = await ToDtosAsync(dbContext, repoId, [row], cancellationToken);

        return dtos[0];
    }

    /// <summary>
    /// One version as it stands in the database, for a caller that has just written it. Reading it
    /// back through <see cref="SavegameExtensions.GetRowAsync"/> would be a round trip to fetch the
    /// fields already in hand.
    /// </summary>
    public static async Task<SavegameVersionDto> ToDtoAsync(
        ApplicationDbContext dbContext,
        SavegameVersion version,
        CancellationToken cancellationToken)
    {
        return await ToDtoAsync(
            dbContext,
            version.RepoId,
            new SavegameVersionRow(
                version.SavegameId,
                version.Number,
                version.ProfileId,
                version.ProfileRevision,
                version.ContentHash,
                version.SizeBytes,
                version.Created,
                version.CreatedBy,
                version.Label,
                version.Origin,
                version.BaseVersion,
                version.CheckoutId),
            cancellationToken);
    }

    public static async Task<List<SavegameCheckoutDto>> ToDtosAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<SavegameCheckout> checkouts,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (checkouts.Count == 0)
        {
            return [];
        }

        var names = await GetNamesAsync(dbContext, checkouts.Select(x => x.UserId), cancellationToken);

        return [.. checkouts.Select(checkout => ToDto(checkout, names, now))];
    }

    public static async Task<SavegameCheckoutDto> ToDtoAsync(
        ApplicationDbContext dbContext,
        SavegameCheckout checkout,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var dtos = await ToDtosAsync(dbContext, [checkout], now, cancellationToken);

        return dtos[0];
    }


    private static Task<Dictionary<UserId, DisplayName>> GetNamesAsync(
        ApplicationDbContext dbContext,
        IEnumerable<UserId> userIds,
        CancellationToken cancellationToken)
    {
        return dbContext.Users.GetDisplayNamesAsync([.. userIds.Distinct()], cancellationToken);
    }

    private static SavegameVersionDto ToDto(RepoId repoId, SavegameVersionRow row, IReadOnlyDictionary<UserId, DisplayName> names)
    {
        return new SavegameVersionDto(
            repoId.Value,
            row.SavegameId.Value,
            row.Number.Value,
            row.ProfileId.Value,
            row.ProfileRevision.Value,
            row.ContentHash,
            row.SizeBytes,
            row.Created,
            Describe(row.CreatedBy, names.TryGetValue(row.CreatedBy, out var name) ? name : null),
            row.Label,
            row.Origin,
            row.BaseVersion?.Value,
            row.CheckoutId?.Value);
    }

    private static SavegameCheckoutDto ToDto(SavegameCheckout checkout, IReadOnlyDictionary<UserId, DisplayName> names, DateTime now)
    {
        return SavegameCheckoutDto.FromModel(
            checkout,
            Describe(checkout.UserId, names.TryGetValue(checkout.UserId, out var name) ? name : null),
            now);
    }
}
