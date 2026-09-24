namespace ModsDude.Server.ModHub;

public class ModHubOptions
{
    public const string SectionName = "ModHub";


    /// <summary>Whether the crawler runs. The lookup route answers either way, from whatever is stored.</summary>
    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://www.farming-simulator.com/";

    /// <summary>ModHub's codes for the games to crawl and answer for, as they appear in its URLs.</summary>
    /// <remarks>
    /// <b>Empty by default, and set in appsettings.</b> The configuration binder appends to a list
    /// rather than replacing it, so a default here would be doubled by the same value in config - and
    /// every game crawled twice a tick.
    /// </remarks>
    public List<string> Games { get; set; } = [];

    /// <summary>
    /// Sent with every request, so ModHub's operators can see who is reading their pages. A contact
    /// belongs in here once there is one to give.
    /// </summary>
    public string UserAgent { get; set; } = "ModsDude";

    /// <summary>
    /// The least time between two requests. The site was tested at far higher rates without any sign of
    /// throttling; this is politeness, not necessity.
    /// </summary>
    public TimeSpan RequestDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// When the crawl runs, in UTC: each run checks the top of the listing for new and updated mods, and
    /// sweeps or refreshes as needed. A run still going when the next is due makes that one a no-op.
    /// </summary>
    public string PollCron { get; set; } = "0 * * * *";

    /// <summary>
    /// How often every listing page is read to find mods a poll missed. The first sweep of a game is
    /// its backfill.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How far down a poll reads looking for where the previous one left off before giving up and
    /// taking everything it read as changed.
    /// </summary>
    public int PollPageLimit { get; set; } = 5;

    /// <summary>
    /// How many of the longest-unread mods each poll reads again. This is what catches what the listing
    /// order cannot show and what removes mods ModHub no longer has: 40 an hour goes through six
    /// thousand mods in about a week.
    /// </summary>
    public int RefreshPerPoll { get; set; } = 40;
}
