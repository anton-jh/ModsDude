namespace ModsDude.Server.Domain.ModHub;

/// <summary>
/// Where the crawler is with one game, persisted so a restart resumes rather than starts over - the
/// first sweep of a game is thousands of pages at one request a second.
/// </summary>
public class ModHubCrawlState
{
    public required string Game { get; init; }

    /// <summary>
    /// The next listing page a sweep in progress reads, or null when none is running. A sweep reads
    /// every page of the "latest" listing and fetches the mods it does not know; the first one is the
    /// backfill.
    /// </summary>
    public int? SweepNextPage { get; set; }

    /// <summary>
    /// When a sweep last reached the end of the listing. Null until the first one has, which is what
    /// makes the data incomplete as far as a client is concerned.
    /// </summary>
    public DateTimeOffset? SweepCompletedAt { get; set; }

    /// <summary>
    /// The first listing page as the last poll (or the start of the last sweep) read it, in order. The
    /// next poll reads down until it finds this again; see <see cref="ModHubListingChanges"/>.
    /// </summary>
    public List<int> HeadModIds { get; set; } = [];

    public DateTimeOffset? PolledAt { get; set; }


    /// <summary>
    /// As of when a client may take the data to be current: ModHub's changes up to this moment are in
    /// it. Null until the first sweep has finished <b>and</b> been followed by a poll - the sweep only
    /// fetches mods it has never seen, so an update to one it read early in a two-hour backfill is
    /// only picked up by the poll after it.
    /// </summary>
    public DateTimeOffset? CurrentAsOf => SweepCompletedAt is null ? null : PolledAt;
}
