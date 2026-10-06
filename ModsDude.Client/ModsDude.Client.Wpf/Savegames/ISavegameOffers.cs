using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegameOffers
{
    /// <summary>
    /// The game this repo's savegames act on, with what it holds and what its mod folder was last
    /// synced to. Null where nothing is connected. Read once per list, not once per row.
    /// </summary>
    SavegameHost? ReadHost(Repo repo);

    /// <summary>
    /// Tells a row what its buttons can do, and where the local copy of the save is.
    /// </summary>
    /// <param name="heldNames">What the savegames the game holds are called.</param>
    void Offer(Repo repo, SavegameListItemViewModel row, SavegameHost? host, IReadOnlyDictionary<Guid, HeldSavegameName> heldNames);
}
