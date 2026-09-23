namespace ModsDude.Server.Domain.ModHub;

/// <summary>
/// Works out which mods at the top of ModHub's "latest" listing are new or updated since the last
/// poll, from their order alone - the listing shows no versions.
/// </summary>
/// <remarks>
/// <para>
/// "Latest" is ordered by last activity: a new mod enters at the top, and an updated one is lifted
/// out of wherever it was and put back at the top. So the listing now is <em>whatever changed</em>,
/// followed by the previous head in its previous order with the changed ones taken out. The first
/// place the listing resumes the previous head's order is the boundary, and everything above it is
/// what to fetch.
/// </para>
/// <para>
/// <b>It cannot see everything, and does not try to.</b> A mod updated again while it is already
/// first leaves the order exactly as it was. The crawler re-reads the stalest mods a few at a time
/// for that reason, so this only has to be right about the ordinary case, and it errs towards
/// fetching too much: a boundary it finds late costs a few extra page reads, never a missed update
/// it could have seen.
/// </para>
/// </remarks>
public static class ModHubListingChanges
{
    /// <summary>How many consecutive mods in the previous order it takes to trust a boundary.</summary>
    private const int _confirmations = 3;


    /// <summary>
    /// How many of the leading ids in <paramref name="listing"/> have changed since
    /// <paramref name="previousHead"/> was read, or null when the boundary is not within what has been
    /// read yet - read another page and ask again.
    /// </summary>
    /// <param name="listing">The listing from the top, as far as it has been read.</param>
    /// <param name="previousHead">The top of the listing as the previous poll read it.</param>
    public static int? FindBoundary(IReadOnlyList<int> listing, IReadOnlyList<int> previousHead)
    {
        if (previousHead.Count == 0)
        {
            return null;
        }

        var positions = new Dictionary<int, int>();

        for (var i = 0; i < previousHead.Count; i++)
        {
            positions.TryAdd(previousHead[i], i);
        }

        for (var start = 0; start < listing.Count; start++)
        {
            if (positions.TryGetValue(listing[start], out var position) is false)
            {
                continue;
            }

            // Near the end of the previous head there are fewer of its mods left to confirm with, and
            // what follows them on the listing was never in it.
            var needed = Math.Min(_confirmations, previousHead.Count - position);

            if (start + needed > listing.Count)
            {
                return null;
            }

            if (ResumesPreviousOrder(listing, start, needed, positions))
            {
                return start;
            }
        }

        return null;
    }


    /// <summary>
    /// Whether <paramref name="count"/> ids from <paramref name="start"/> all come from the previous
    /// head and in its order. Increasing rather than adjacent positions, because a mod ModHub has since
    /// removed leaves a gap in the previous order that the listing simply closes.
    /// </summary>
    private static bool ResumesPreviousOrder(IReadOnlyList<int> listing, int start, int count, Dictionary<int, int> positions)
    {
        var last = -1;

        for (var i = start; i < start + count; i++)
        {
            if (positions.TryGetValue(listing[i], out var position) is false || position <= last)
            {
                return false;
            }

            last = position;
        }

        return true;
    }
}
