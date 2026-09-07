using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;

/// <summary>
/// Everything that reads a savegame, its claims, or the blob addresses its versions refer to.
/// </summary>
/// <remarks>
/// The reads that answer a question project rather than materialize, for the reason
/// <see cref="ProfileRevisionExtensions"/> gives: a savegame's history is unbounded and its rows are
/// wanted a field at a time. Entities are loaded only where something is going to change one - a
/// savegame whose head is about to move, a claim that is about to end.
/// </remarks>
public static class SavegameExtensions
{
    public static object[] GetKey(this Savegame savegame)
    {
        return [savegame.RepoId, savegame.Id];
    }

    public static object[] GetKey(RepoId repoId, SavegameId savegameId)
    {
        return [repoId, savegameId];
    }

    /// <summary>
    /// The savegame row itself - name, which profile it follows, and which version is its head. Its
    /// versions are not here and cannot be reached from here; see <see cref="Savegame"/> for why the
    /// navigation does not exist.
    /// </summary>
    public static ValueTask<Savegame?> GetAsync(this DbSet<Savegame> dbSet, RepoId repoId, SavegameId savegameId, CancellationToken cancellationToken)
    {
        return dbSet.FindAsync(GetKey(repoId, savegameId), cancellationToken);
    }

    /// <summary>
    /// Whether a live savegame already answers to this name. Archived ones are ignored - they gave
    /// up their names when they were archived.
    /// </summary>
    public static Task<bool> CheckNameIsTaken(this DbSet<Savegame> dbSet, RepoId repoId, SavegameName name, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.RepoId == repoId && x.ArchivedAt == null && x.Name == name, cancellationToken);
    }

    /// <inheritdoc cref="CheckNameIsTaken(DbSet{Savegame}, RepoId, SavegameName, CancellationToken)"/>
    public static Task<bool> CheckNameIsTaken(this DbSet<Savegame> dbSet, RepoId repoId, SavegameId except, SavegameName name, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.RepoId == repoId && x.ArchivedAt == null && x.Id != except && x.Name == name, cancellationToken);
    }

    /// <summary>
    /// Every savegame in the repo, as a list renders one - and none of what hangs off it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The head version and the open claim are two further reads over the whole repo -
    /// <see cref="GetHeadVersionsAsync"/> and <see cref="GetOpenCheckoutsAsync"/> - rather than a
    /// join here or a follow-up per row. A repo's savegame list then costs a fixed number of
    /// queries whether it holds three saves or fifty, which is the only property that matters: the
    /// per-row shape is the one that quietly turns a page into a hundred round trips.
    /// </para>
    /// <para>
    /// Ordered by name, because that is what the list is read by and the database is the only place
    /// the order can be settled once for every caller.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The names of a handful of savegames at once, for a refusal that has to name what is holding
    /// something rather than counting it.
    /// </summary>
    public static async Task<Dictionary<SavegameId, SavegameName>> GetNamesAsync(
        this DbSet<Savegame> dbSet,
        RepoId repoId, IReadOnlyCollection<SavegameId> savegameIds,
        CancellationToken cancellationToken)
    {
        if (savegameIds.Count == 0)
        {
            return [];
        }

        var rows = await dbSet
            .Where(x => x.RepoId == repoId && savegameIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Id, x => x.Name);
    }

    /// <param name="archived">
    /// Which list this is. The two are disjoint and never merged: the saves page shows what the repo
    /// is using and the archive shows what it has put away, and a flag on a row that mixed them
    /// would make every caller responsible for filtering something it did not ask for.
    /// </param>
    public static Task<List<SavegameRow>> GetRowsAsync(
        this DbSet<Savegame> dbSet,
        RepoId repoId,
        CancellationToken cancellationToken,
        bool archived = false)
    {
        return dbSet
            .Where(x => x.RepoId == repoId && (x.ArchivedAt != null) == archived)
            // Archived ones lead with when they were put away, because several may share a name and
            // that is the only thing telling them apart.
            .OrderBy(x => archived ? x.ArchivedAt : null)
            .ThenBy(x => x.Name)
            .Select(x => new SavegameRow(x.Id, x.Name, x.ProfileId, x.Created, x.HeadVersion, x.ArchivedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The savegame's open claim, or <c>null</c> where nobody holds it. Tracked, because every caller
    /// that asks is about to end it, renew it, or take it over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is at most one, and that is a database fact rather than an assumption this query has to
    /// defend: a filtered unique index permits one row per savegame with no <c>EndedAt</c> - see
    /// <see cref="EntityTypeConfigurations.SavegameCheckoutEntityTypeConfiguration"/>.
    /// </para>
    /// <para>
    /// The predicate tests the column rather than <see cref="SavegameCheckout.IsOpen"/>, which is
    /// computed and has nothing for a provider to translate. It is also what makes the query match
    /// the index's filter, so the open row is found without reading the history behind it.
    /// </para>
    /// <para>
    /// Open is not the same as held. A claim taken on Friday is still the open row on Monday; whether
    /// it reads as held or as stale is <see cref="SavegameCheckout.GetStatus"/>'s answer, and it needs
    /// a clock this query does not have.
    /// </para>
    /// </remarks>
    public static Task<SavegameCheckout?> GetOpenCheckoutAsync(
        this DbSet<SavegameCheckout> dbSet,
        RepoId repoId, SavegameId savegameId,
        CancellationToken cancellationToken)
    {
        return dbSet
            .FirstOrDefaultAsync(x => x.RepoId == repoId && x.SavegameId == savegameId && x.EndedAt == null, cancellationToken);
    }

    /// <summary>
    /// Every open claim in the repo, one per savegame that has one. What the savegame list joins to
    /// its rows, in one read rather than one per row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Materialized rather than projected, and the one read here that is. A claim is six columns
    /// with nothing hanging off it, and <see cref="SavegameCheckout.GetStatus"/> - the thing every
    /// caller actually wants - is a method on the entity. Projecting would mean re-deriving that
    /// rule in the mapping, where it could drift away from the domain's.
    /// </para>
    /// <para>
    /// Untracked, unlike <see cref="GetOpenCheckoutAsync"/>: nothing that reads a list is about to
    /// change one, and tracking a repo's worth of claims to render them would put them all in the
    /// change tracker to be scanned on the next commit.
    /// </para>
    /// </remarks>
    public static Task<List<SavegameCheckout>> GetOpenCheckoutsAsync(
        this DbSet<SavegameCheckout> dbSet,
        RepoId repoId,
        CancellationToken cancellationToken)
    {
        return dbSet
            .AsNoTracking()
            .Where(x => x.RepoId == repoId && x.EndedAt == null)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// One savegame's claims, newest first, windowed by offset - the other half of the timeline a
    /// savegame's detail pane renders.
    /// </summary>
    /// <remarks>
    /// Ordered by <see cref="SavegameCheckout.TakenAt"/> rather than by when a claim ended, because
    /// the open row has no end and would sort either first or last depending on the provider's
    /// opinion of nulls. Taking is the event a reader is looking for anyway.
    /// </remarks>
    public static Task<List<SavegameCheckout>> GetHistoryAsync(
        this DbSet<SavegameCheckout> dbSet,
        RepoId repoId, SavegameId savegameId,
        int skip, int take,
        CancellationToken cancellationToken)
    {
        return dbSet
            .AsNoTracking()
            .Where(x => x.RepoId == repoId && x.SavegameId == savegameId)
            .OrderByDescending(x => x.TakenAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public static Task<int> CountCheckoutsAsync(
        this DbSet<SavegameCheckout> dbSet,
        RepoId repoId, SavegameId savegameId,
        CancellationToken cancellationToken)
    {
        return dbSet.CountAsync(x => x.RepoId == repoId && x.SavegameId == savegameId, cancellationToken);
    }

    /// <summary>
    /// One savegame's history without its blobs - newest first, windowed by offset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ordered and windowed before it is projected</b>, for the reason
    /// <see cref="ProfileRevisionExtensions.GetHistoryAsync"/> gives: a provider cannot see through
    /// a constructor-bound record, so ordering by a member of the projection has nowhere to go.
    /// </para>
    /// <para>
    /// An offset rather than a keyset, and the same reason again -
    /// <see cref="SavegameVersionNumber"/> is a value object, so ordering by it translates and
    /// comparing two of them does not. New versions arrive at the front, so a page read while
    /// somebody is checking in can repeat a row.
    /// </para>
    /// </remarks>
    public static Task<List<SavegameVersionRow>> GetHistoryAsync(
        this DbSet<SavegameVersion> dbSet,
        RepoId repoId, SavegameId savegameId,
        int skip, int take,
        CancellationToken cancellationToken)
    {
        return dbSet
            // Owned entities cannot be tracked without the owner they hang off, and Details is
            // projected out of a row that deliberately never materializes a version.
            .AsNoTracking()
            .Where(x => x.RepoId == repoId && x.SavegameId == savegameId)
            .OrderByDescending(x => x.Number)
            .Skip(skip)
            .Take(take)
            .Select(x => new SavegameVersionRow(
                x.SavegameId,
                x.Number,
                x.ProfileId,
                x.ProfileRevision,
                x.ContentHash,
                x.SizeBytes,
                x.Created,
                x.CreatedBy,
                x.Label,
                x.Origin,
                x.BaseVersion,
                x.CheckoutId)
            {
                Details = x.Details.OrderBy(y => y.Position).ToList()
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// How many versions the savegame still has. Read rather than inferred from the head, because
    /// version numbers are <b>not contiguous</b> - pruning leaves the gap where an old version was,
    /// so a savegame whose head is 40 may hold ten rows.
    /// </summary>
    public static Task<int> CountVersionsAsync(
        this DbSet<SavegameVersion> dbSet,
        RepoId repoId, SavegameId savegameId,
        CancellationToken cancellationToken)
    {
        return dbSet.CountAsync(x => x.RepoId == repoId && x.SavegameId == savegameId, cancellationToken);
    }

    /// <summary>One version's entry, or <c>null</c> where the savegame has no such version.</summary>
    public static Task<SavegameVersionRow?> GetRowAsync(
        this DbSet<SavegameVersion> dbSet,
        RepoId repoId, SavegameId savegameId, SavegameVersionNumber number,
        CancellationToken cancellationToken)
    {
        return dbSet
            .AsNoTracking()
            .Where(x => x.RepoId == repoId && x.SavegameId == savegameId && x.Number == number)
            .Select(x => new SavegameVersionRow(
                x.SavegameId,
                x.Number,
                x.ProfileId,
                x.ProfileRevision,
                x.ContentHash,
                x.SizeBytes,
                x.Created,
                x.CreatedBy,
                x.Label,
                x.Origin,
                x.BaseVersion,
                x.CheckoutId)
            {
                Details = x.Details.OrderBy(y => y.Position).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The head version of each of several savegames at once, for a list that carries its heads
    /// inline.
    /// </summary>
    /// <param name="heads">
    /// What each savegame says its head is - <see cref="SavegameRow.HeadVersion"/>, read in the same
    /// request. The head is asked for by number rather than found by maximum because the savegame
    /// row is the authority on which version is current, and a maximum would only agree with it by
    /// coincidence of how pruning happens to work.
    /// </param>
    /// <remarks>
    /// <b>The predicate is the cross product of the two sets and the pairing is done afterwards.</b>
    /// A provider cannot translate a membership test on a tuple of two value objects, so asking for
    /// exactly these <c>(savegame, number)</c> pairs in SQL would mean an OR-chain the length of the
    /// repo. Fetching every version whose savegame is in the list and whose number is one of the
    /// head numbers over-reads by a bounded amount - retention keeps a savegame to a handful of
    /// versions - and the exact pairing is a dictionary lookup once the rows are here.
    /// </remarks>
    public static async Task<List<SavegameVersionRow>> GetHeadVersionsAsync(
        this DbSet<SavegameVersion> dbSet,
        RepoId repoId,
        IReadOnlyDictionary<SavegameId, SavegameVersionNumber> heads,
        CancellationToken cancellationToken)
    {
        if (heads.Count == 0)
        {
            return [];
        }

        var savegameIds = heads.Keys.ToList();
        var numbers = heads.Values.Distinct().ToList();

        var rows = await dbSet
            .AsNoTracking()
            .Where(x => x.RepoId == repoId && savegameIds.Contains(x.SavegameId) && numbers.Contains(x.Number))
            .Select(x => new SavegameVersionRow(
                x.SavegameId,
                x.Number,
                x.ProfileId,
                x.ProfileRevision,
                x.ContentHash,
                x.SizeBytes,
                x.Created,
                x.CreatedBy,
                x.Label,
                x.Origin,
                x.BaseVersion,
                x.CheckoutId)
            {
                Details = x.Details.OrderBy(y => y.Position).ToList()
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Where(x => heads[x.SavegameId] == x.Number)];
    }

    /// <summary>
    /// Every blob address any savegame version still refers to, across every repo. What the
    /// reclamation sweep reads to decide that a stored blob is garbage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Projected and deduplicated in the database rather than materialized. Versions are addressed by
    /// content, so a restore copies an older version forward under the same hash and several versions
    /// legitimately share one address - what the sweep needs is the set of addresses, not one entry
    /// per version. Loading the entities to build it would read every savegame ever checked in.
    /// </para>
    /// <para>
    /// The record struct is built after the round trip because a provider cannot see through its
    /// constructor, exactly as the mod sweep does it.
    /// </para>
    /// <para>
    /// Must be read <b>after</b> the blobs are listed, not before. A blob written before the listing
    /// and registered during it is then seen as registered; the other order misses it and deletes
    /// live data, however long the grace period is - see <see cref="BlobReclamation"/>.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlySet<SavegameBlobAddress>> GetRegisteredBlobAddressesAsync(
        this DbSet<SavegameVersion> dbSet,
        CancellationToken cancellationToken)
    {
        var rows = await dbSet
            .AsNoTracking()
            .Select(x => new { x.RepoId, x.SavegameId, x.ContentHash })
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new SavegameBlobAddress(x.RepoId, x.SavegameId, x.ContentHash))
            .ToHashSet();
    }

    /// <summary>
    /// Every version of one savegame, reduced to what the retention policy actually looks at: the
    /// number, and whether somebody named it.
    /// </summary>
    /// <remarks>
    /// A whole history rather than a window, because the policy's rules are about the set - the most
    /// recent N of the unlabelled ones - and cannot be evaluated from a page of it. Two integers and
    /// a boolean per version is cheap enough to read all of even at the point where pruning starts
    /// to matter.
    /// </remarks>
    public static async Task<IReadOnlyList<SavegameVersionRetention>> GetRetentionRowsAsync(
        this DbSet<SavegameVersion> dbSet,
        RepoId repoId, SavegameId savegameId,
        CancellationToken cancellationToken)
    {
        var rows = await dbSet
            .AsNoTracking()
            .Where(x => x.RepoId == repoId && x.SavegameId == savegameId)
            .Select(x => new { x.Number, x.Label })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => new SavegameVersionRetention(x.Number, x.Label != null))];
    }

    /// <summary>
    /// Drops the named versions of one savegame.
    /// </summary>
    /// <remarks>
    /// <b>Rows only. The blobs are left to the reclamation sweep</b>, and that is the safety property
    /// rather than an omission: versions are addressed by content, so a restore and a night that
    /// changed nothing both leave two versions naming one blob. Deleting bytes here would mean asking
    /// whether any other version still refers to them, in a transaction, every time - and getting it
    /// wrong destroys the save a different version still points at. The sweep already asks exactly
    /// that question of the whole store, so pruning stops at the rows and the bytes fall out on the
    /// next pass.
    /// </remarks>
    public static Task<int> DeleteVersionsAsync(
        this DbSet<SavegameVersion> dbSet,
        RepoId repoId, SavegameId savegameId,
        IReadOnlyCollection<SavegameVersionNumber> numbers,
        CancellationToken cancellationToken)
    {
        if (numbers.Count == 0)
        {
            return Task.FromResult(0);
        }

        return dbSet
            .Where(x => x.RepoId == repoId && x.SavegameId == savegameId && numbers.Contains(x.Number))
            .ExecuteDeleteAsync(cancellationToken);
    }
}


/// <summary>
/// One savegame as a list renders it, and nothing that hangs off it. Its head version and its open
/// claim are read separately - see <see cref="SavegameExtensions.GetRowsAsync"/>.
/// </summary>
public record SavegameRow(
    SavegameId Id,
    SavegameName Name,
    ProfileId ProfileId,
    DateTime Created,
    SavegameVersionNumber HeadVersion,
    DateTime? ArchivedAt);


/// <summary>
/// One version as a history renders it. Everything the row itself holds, and none of the bytes it
/// addresses - <see cref="SavegameVersionRow.ContentHash"/> is what a client turns into a download
/// link when somebody actually wants them.
/// </summary>
public record SavegameVersionRow(
    SavegameId SavegameId,
    SavegameVersionNumber Number,
    ProfileId ProfileId,
    RevisionNumber ProfileRevision,
    string ContentHash,
    long SizeBytes,
    DateTime Created,
    UserId CreatedBy,
    string? Label,
    SavegameVersionOrigin Origin,
    SavegameVersionNumber? BaseVersion,
    SavegameCheckoutId? CheckoutId)
{
    /// <summary>
    /// What the adapter said about this version, in its own order. Projected rather than left to
    /// the owned collection's own loading, because nothing here materializes a version.
    /// </summary>
    public IReadOnlyList<SavegameDetail> Details { get; init; } = [];
}
