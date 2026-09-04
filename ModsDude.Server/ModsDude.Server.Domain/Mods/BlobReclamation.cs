using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Domain.Mods;

/// <summary>
/// A blob as storage reports it: the name it is stored under and when it was last written.
/// </summary>
public readonly record struct StoredBlob(string Name, DateTimeOffset LastModified);

/// <summary>
/// The triple a mod file is stored against. Kept as a value rather than three parameters so a set of
/// registered addresses can be looked up in one step.
/// </summary>
public readonly record struct ModBlobAddress(RepoId RepoId, ModId ModId, ModVersionId VersionId);

/// <summary>
/// The pair a savegame blob is stored against. A version's bytes are addressed by <b>content</b>
/// rather than by version number, so two people checking in at the same moment write two different
/// blobs instead of racing for one name, and a restore is a metadata operation rather than a copy.
/// The consequence for the sweep: several versions can refer to one blob, so what is registered is
/// the set of addresses, not one per version.
/// </summary>
public readonly record struct SavegameBlobAddress(RepoId RepoId, SavegameId SavegameId, string ContentHash);

/// <param name="Reclaimable">Blobs nothing refers to, old enough to be certain of it.</param>
/// <param name="Retained">
/// Blobs nothing refers to that are too recent to judge. Reported rather than silently dropped,
/// because a count that never falls is the signal that the grace period is set wrong.
/// </param>
/// <param name="Unrecognised">
/// Names the sweep could not parse into an address. Never deleted — a name the sweep does not
/// understand is a name it cannot prove is garbage — and reported so that a change to the storage
/// layout shows up as a number rather than as silence.
/// </param>
public record ReclamationPlan(
    IReadOnlyList<StoredBlob> Reclaimable,
    IReadOnlyList<StoredBlob> Retained,
    IReadOnlyList<string> Unrecognised);

/// <summary>
/// Decides which stored blobs are garbage. Pure, and separate from storage on purpose: the decision
/// is the part that can destroy data, and it is the part that can be tested without a storage account.
/// </summary>
/// <remarks>
/// <para>
/// <b>The hazard is the import that is still running.</b> A client mints an upload link, writes the
/// blob, and only then registers the version — so between those two steps a perfectly live blob is
/// referenced by nothing at all, and a sweep that deletes it destroys an operation in progress and
/// leaves behind a registration whose file can never be restored (being registered is exactly what
/// stops a fresh upload link being minted for it).
/// </para>
/// <para>
/// Two things guard it. A blob is only considered once it has been untouched for longer than
/// <c>cutoff</c> allows, which is set far beyond the 30-minute lifetime of an upload SAS plus the
/// registration call that follows it. And the caller must read the registered set <b>after</b>
/// listing the blobs: a blob written before the listing and registered during it is then seen as
/// registered, whereas the reverse order would miss it. Getting that order wrong deletes live data
/// no matter how long the grace period is.
/// </para>
/// </remarks>
public static class BlobReclamation
{
    /// <param name="cutoff">
    /// Blobs last modified at or before this instant may be judged; anything newer is retained
    /// whatever the registrations say.
    /// </param>
    public static ReclamationPlan PlanModSweep(
        IEnumerable<StoredBlob> stored,
        IReadOnlySet<ModBlobAddress> registered,
        DateTimeOffset cutoff)
    {
        return Plan(stored, cutoff, name => TryParseModBlobName(name, out var address) ? registered.Contains(address) : null);
    }

    /// <param name="cutoff">
    /// See <see cref="PlanModSweep"/>. The hazard is identical for savegames and so is the guard: a
    /// client mints an upload link, writes the packed save, and only then checks it in, so between
    /// those two steps a perfectly live blob is referred to by nothing at all.
    /// </param>
    public static ReclamationPlan PlanSavegameSweep(
        IEnumerable<StoredBlob> stored,
        IReadOnlySet<SavegameBlobAddress> registered,
        DateTimeOffset cutoff)
    {
        return Plan(stored, cutoff, name => TryParseSavegameBlobName(name, out var address) ? registered.Contains(address) : null);
    }

    /// <param name="cutoff">See <see cref="PlanModSweep"/>.</param>
    public static ReclamationPlan PlanImageSweep(
        IEnumerable<StoredBlob> stored,
        IReadOnlySet<string> referenced,
        DateTimeOffset cutoff)
    {
        return Plan(stored, cutoff, name => TryParseImageBlobName(name, out var hash) ? referenced.Contains(hash) : null);
    }

    /// <summary>
    /// The layout <c>ModStorageService</c> writes: <c>{repoId}/{modId}/{versionId}</c>. Rejects
    /// anything that is not exactly three non-empty segments with a parseable repo id, so a stray
    /// file or a future layout is reported rather than deleted.
    /// </summary>
    public static bool TryParseModBlobName(string name, out ModBlobAddress address)
    {
        address = default;

        var segments = name.Split('/');

        if (segments.Length != 3
            || segments.Any(string.IsNullOrEmpty)
            || !Guid.TryParse(segments[0], out var repoId))
        {
            return false;
        }

        address = new ModBlobAddress(new RepoId(repoId), new ModId(segments[1]), new ModVersionId(segments[2]));

        return true;
    }

    /// <summary>
    /// The layout <c>SavegameStorageService</c> writes: <c>{repoId}/{savegameId}/{contentHash}</c>.
    /// Both ids have to parse and the last segment has to be a hash, so a stray file or a future
    /// layout is reported rather than deleted.
    /// </summary>
    public static bool TryParseSavegameBlobName(string name, out SavegameBlobAddress address)
    {
        address = default;

        var segments = name.Split('/');

        if (segments.Length != 3
            || !Guid.TryParse(segments[0], out var repoId)
            || !Guid.TryParse(segments[1], out var savegameId)
            || !ModImageHash.IsValid(segments[2]))
        {
            return false;
        }

        address = new SavegameBlobAddress(new RepoId(repoId), new SavegameId(savegameId), segments[2]);

        return true;
    }

    /// <summary>
    /// The layout <c>ModImageStorageService</c> writes: <c>{hash[..2]}/{hash}</c>. The prefix has to
    /// agree with the hash, or the name was not written by this system.
    /// </summary>
    public static bool TryParseImageBlobName(string name, out string hash)
    {
        hash = string.Empty;

        var segments = name.Split('/');

        if (segments.Length != 2
            || !ModImageHash.IsValid(segments[1])
            || segments[0] != segments[1][..2])
        {
            return false;
        }

        hash = segments[1];

        return true;
    }


    /// <param name="isReferenced">
    /// Whether the model refers to the blob, or <c>null</c> when the name does not parse.
    /// </param>
    private static ReclamationPlan Plan(
        IEnumerable<StoredBlob> stored,
        DateTimeOffset cutoff,
        Func<string, bool?> isReferenced)
    {
        var reclaimable = new List<StoredBlob>();
        var retained = new List<StoredBlob>();
        var unrecognised = new List<string>();

        foreach (var blob in stored)
        {
            switch (isReferenced(blob.Name))
            {
                case null:
                    unrecognised.Add(blob.Name);
                    break;

                case true:
                    break;

                case false when blob.LastModified <= cutoff:
                    reclaimable.Add(blob);
                    break;

                case false:
                    retained.Add(blob);
                    break;
            }
        }

        return new ReclamationPlan(reclaimable, retained, unrecognised);
    }
}
