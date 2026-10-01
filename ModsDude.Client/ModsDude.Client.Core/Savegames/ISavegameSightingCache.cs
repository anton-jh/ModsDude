using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

public interface ISavegameSightingCache : ISavegameSightings
{
    /// <summary>
    /// Records what a freshly read savegame list says. Called by whatever just read one; a savegame
    /// with no head yet - published in the same breath and not yet answered for - is skipped rather
    /// than recorded as zero.
    /// </summary>
    /// <param name="currentUserId">
    /// Who is signed in, which is what decides whether a claim is yours. Null where the caller could
    /// not find out, and then the claims are left as they were rather than all read as somebody else's.
    /// </param>
    void Record(Guid repoId, IEnumerable<SavegameDto> savegames, string? currentUserId);
}
