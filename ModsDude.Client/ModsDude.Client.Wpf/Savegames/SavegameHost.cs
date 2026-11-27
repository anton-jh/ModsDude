using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// The game a repo's savegames act on, with what it is holding and which revision of which profile
/// its mod folder was last made to match.
/// </summary>
/// <param name="UnreachableHolds">
/// The savegames held in a folder the settings no longer name - see
/// <see cref="ISavegameHolds.GetUnreachableHolds"/>.
/// </param>
/// <param name="GoneHolds">The savegames held here whose repo is gone, so they can never be checked in.</param>
public sealed record SavegameHost(
    Game Game,
    IReadOnlyList<SavegameCheckoutBinding> Held,
    IReadOnlySet<Guid> UnreachableHolds,
    IReadOnlySet<Guid> GoneHolds,
    Guid? AppliedProfileId,
    int? AppliedRevision);
