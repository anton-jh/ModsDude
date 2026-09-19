using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// A savegame, its snapshots and its claims, as the API answers with them.
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
    /// The person, named. A snapshot records who made it as a <see cref="UserId"/> and there is no
    /// foreign key holding that user in place, so a name that cannot be resolved falls back to the
    /// id rather than dropping the row out of the history.
    /// </summary>
    public static UserDto Describe(UserId userId, DisplayName? displayName)
        => new(userId.Value, displayName?.Value ?? userId.Value, UserTag.For(userId));


    /// <summary>
    /// Every savegame in the repo, each carrying its head snapshot and its open claim.
    /// </summary>
    /// <param name="archived">
    /// Which list this is. The two are disjoint - the saves page shows what the repo is using, the
    /// Archive shows what it has put away - and everything below is identical either way.
    /// </param>
    public static async Task<List<SavegameDto>> GetListAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        CancellationToken cancellationToken,
        bool archived = false)
    {
        var rows = await dbContext.Savegames.GetRowsAsync(repoId, cancellationToken, archived);

        if (rows.Count == 0)
        {
            return [];
        }

        var heads = await dbContext.SavegameSnapshots.GetHeadSnapshotsAsync(
            repoId,
            rows.ToDictionary(x => x.Id, x => x.HeadSnapshot),
            cancellationToken);

        var checkouts = await dbContext.SavegameCheckouts.GetOpenCheckoutsAsync(repoId, cancellationToken);
        var totals = await dbContext.SavegameSnapshots.GetSnapshotTotalsAsync(repoId, cancellationToken);

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
                row.ProfileId?.Value,
                row.Created,
                headsBySavegame.TryGetValue(row.Id, out var head) ? ToDto(repoId, head, names) : null,
                checkoutsBySavegame.TryGetValue(row.Id, out var checkout) ? ToDto(checkout, names) : null,
                row.SupersededAt,
                row.ArchivedAt,
                totals.GetValueOrDefault(row.Id).Count,
                totals.GetValueOrDefault(row.Id).Bytes))
        ];
    }

    /// <summary>
    /// One savegame, in the same shape the list gives it. Takes the entity because every caller that
    /// wants one has just loaded it to change it.
    /// </summary>
    public static async Task<SavegameDto> DescribeAsync(
        ApplicationDbContext dbContext,
        Savegame savegame,
        CancellationToken cancellationToken)
    {
        var head = await dbContext.SavegameSnapshots.GetRowAsync(
            savegame.RepoId, savegame.Id, savegame.HeadSnapshot, cancellationToken);

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

        var totals = (await dbContext.SavegameSnapshots.GetSnapshotTotalsAsync(savegame.RepoId, cancellationToken, savegame.Id))
            .GetValueOrDefault(savegame.Id);

        var names = await GetNamesAsync(dbContext, userIds, cancellationToken);

        return new SavegameDto(
            savegame.Id.Value,
            savegame.RepoId.Value,
            savegame.Name.Value,
            savegame.ProfileId?.Value,
            savegame.Created,
            head is null ? null : ToDto(savegame.RepoId, head, names),
            checkout is null ? null : ToDto(checkout, names),
            savegame.SupersededAt,
            savegame.ArchivedAt,
            totals.Count,
            totals.Bytes);
    }

    public static async Task<List<SavegameSnapshotDto>> ToDtosAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        IReadOnlyList<SavegameSnapshotRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var names = await GetNamesAsync(dbContext, rows.Select(x => x.CreatedBy), cancellationToken);

        return [.. rows.Select(row => ToDto(repoId, row, names))];
    }

    public static async Task<SavegameSnapshotDto> ToDtoAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        SavegameSnapshotRow row,
        CancellationToken cancellationToken)
    {
        var dtos = await ToDtosAsync(dbContext, repoId, [row], cancellationToken);

        return dtos[0];
    }

    /// <summary>
    /// One snapshot as it stands in the database, for a caller that has just written it. Reading it
    /// back through <see cref="SavegameExtensions.GetRowAsync"/> would be a round trip to fetch the
    /// fields already in hand.
    /// </summary>
    public static async Task<SavegameSnapshotDto> ToDtoAsync(
        ApplicationDbContext dbContext,
        SavegameSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        return await ToDtoAsync(
            dbContext,
            snapshot.RepoId,
            new SavegameSnapshotRow(
                snapshot.SavegameId,
                snapshot.Number,
                snapshot.ProfileId,
                snapshot.ProfileRevision,
                snapshot.ContentHash,
                snapshot.SizeBytes,
                snapshot.Created,
                snapshot.CreatedBy,
                snapshot.Label,
                snapshot.Origin,
                snapshot.BaseSnapshot,
                snapshot.CheckoutId),
            cancellationToken);
    }

    public static async Task<List<SavegameCheckoutDto>> ToDtosAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<SavegameCheckout> checkouts,
        CancellationToken cancellationToken)
    {
        if (checkouts.Count == 0)
        {
            return [];
        }

        var names = await GetNamesAsync(dbContext, checkouts.Select(x => x.UserId), cancellationToken);

        return [.. checkouts.Select(checkout => ToDto(checkout, names))];
    }

    public static async Task<SavegameCheckoutDto> ToDtoAsync(
        ApplicationDbContext dbContext,
        SavegameCheckout checkout,
        CancellationToken cancellationToken)
    {
        var dtos = await ToDtosAsync(dbContext, [checkout], cancellationToken);

        return dtos[0];
    }


    private static Task<Dictionary<UserId, DisplayName>> GetNamesAsync(
        ApplicationDbContext dbContext,
        IEnumerable<UserId> userIds,
        CancellationToken cancellationToken)
    {
        return dbContext.Users.GetDisplayNamesAsync([.. userIds.Distinct()], cancellationToken);
    }

    private static SavegameSnapshotDto ToDto(RepoId repoId, SavegameSnapshotRow row, IReadOnlyDictionary<UserId, DisplayName> names)
    {
        return new SavegameSnapshotDto(
            repoId.Value,
            row.SavegameId.Value,
            row.Number.Value,
            row.ProfileId?.Value,
            row.ProfileRevision?.Value,
            row.ContentHash,
            row.SizeBytes,
            row.Created,
            Describe(row.CreatedBy, names.TryGetValue(row.CreatedBy, out var name) ? name : null),
            row.Label,
            row.Origin,
            row.BaseSnapshot?.Value,
            row.CheckoutId?.Value,
            [.. row.Details.Select(x => new SavegameDetailDto(x.Key, x.Label, x.Value))]);
    }

    private static SavegameCheckoutDto ToDto(SavegameCheckout checkout, IReadOnlyDictionary<UserId, DisplayName> names)
    {
        return SavegameCheckoutDto.FromModel(
            checkout,
            Describe(checkout.UserId, names.TryGetValue(checkout.UserId, out var name) ? name : null));
    }
}

/// <summary>
/// Turns what a client's adapter said into what the snapshot stores. The server does not look inside
/// - see <see cref="SavegameDetail"/> - so this only drops the empties and keeps the order sent.
/// </summary>
internal static class SavegameDetails
{
    /// <summary>
    /// Bounded because a request body is not a place to accept an unbounded list, and because
    /// anything past this is not something a person is reading off a row.
    /// </summary>
    private const int _maximum = 24;

    public static IEnumerable<SavegameDetail> From(IEnumerable<SavegameDetailDto>? details)
    {
        return (details ?? [])
            .Where(x => string.IsNullOrWhiteSpace(x.Key) is false && string.IsNullOrWhiteSpace(x.Value) is false)
            // The order the adapter sent them in is the order it wanted them read.
            .Select((x, index) => new SavegameDetail(x.Key.Trim(), x.Label.Trim(), x.Value.Trim(), index))
            .DistinctBy(x => x.Key)
            .Take(_maximum);
    }
}
