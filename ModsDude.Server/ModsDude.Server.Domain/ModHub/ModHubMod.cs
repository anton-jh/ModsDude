namespace ModsDude.Server.Domain.ModHub;

/// <summary>
/// One mod as ModHub currently lists it, kept so a client can ask "is there a newer version of this
/// file on ModHub" without asking ModHub. Written only by the crawler; nothing a user does touches it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not tied to any repo.</b> ModHub is public and global, so this is one table for everyone, and
/// nothing about a repo's mods refers to it by key - a client matches on the file name.
/// </para>
/// <para>
/// <b>A mod ModHub no longer has is deleted, not marked.</b> Nothing a client does with a removed mod
/// needs the row, and keeping it would only leave a dead link to be offered.
/// </para>
/// </remarks>
public class ModHubMod
{
    /// <summary>ModHub's own code for the game, as it appears in its URLs: <c>fs2025</c>.</summary>
    public required string Game { get; init; }

    /// <summary>ModHub's id, the <c>mod_id</c> of its page.</summary>
    public required int ModHubId { get; init; }

    /// <summary>The archive's name exactly as ModHub gives it, extension included.</summary>
    public required string FileName { get; set; }

    /// <summary>
    /// <see cref="FileName"/> without its extension and lowercased: what a lookup matches on. Stored
    /// rather than computed so it can be indexed.
    /// </summary>
    public required string FileNameKey { get; set; }

    public required string Title { get; set; }
    public string? Author { get; set; }

    /// <summary>ModHub's version string, as it shows it. Not parsed here - ordering is the client's.</summary>
    public required string Version { get; set; }

    public DateOnly? Released { get; set; }

    /// <summary>ModHub's rounded size, such as <c>78 KB</c>. Display only.</summary>
    public string? Size { get; set; }

    /// <summary>
    /// The archive on ModHub's CDN, as the page links it. Kept verbatim rather than built from the id,
    /// because the CDN host differs from mod to mod.
    /// </summary>
    public string? DownloadUrl { get; set; }

    /// <summary>When this mod's page was last read. The stalest are re-read a few at a time.</summary>
    public required DateTimeOffset FetchedAt { get; set; }


    public static string ToFileNameKey(string fileName)
    {
        var name = fileName.Trim();

        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.ToLowerInvariant();
    }

    public static ModHubMod Create(string game, int modHubId, ModHubModDetails details, DateTimeOffset now)
    {
        var mod = new ModHubMod
        {
            Game = game,
            ModHubId = modHubId,
            FileName = details.FileName,
            FileNameKey = ToFileNameKey(details.FileName),
            Title = details.Title,
            Version = details.Version,
            FetchedAt = now
        };

        mod.Apply(details, now);

        return mod;
    }

    public void Apply(ModHubModDetails details, DateTimeOffset now)
    {
        FileName = details.FileName;
        FileNameKey = ToFileNameKey(details.FileName);
        Title = details.Title;
        Author = details.Author;
        Version = details.Version;
        Released = details.Released;
        Size = details.Size;
        DownloadUrl = details.DownloadUrl;
        FetchedAt = now;
    }
}

/// <summary>What one ModHub mod page says about the mod.</summary>
public record ModHubModDetails(
    string FileName,
    string Title,
    string? Author,
    string Version,
    DateOnly? Released,
    string? Size,
    string? DownloadUrl);
