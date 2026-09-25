using ModsDude.Server.Domain.Activity;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// Which profile somebody's game is on, with the names a list needs to say so without a second read.
/// </summary>
/// <param name="Game">The game identity the client reported, exactly as it sent it.</param>
/// <param name="PinnedRevision">
/// The revision their game is held to, or null - nearly always - where it follows the profile's head.
/// </param>
/// <param name="Kind">
/// What happened at <paramref name="ChangedAt"/>: an activation or a savegame checked out. Never a
/// re-apply, which only moves <paramref name="TouchedAt"/>.
/// </param>
/// <param name="SavegameName">The savegame checked out, where it still exists.</param>
public record GameActivityDto(
    UserDto User,
    string Game,
    Guid RepoId,
    Guid ProfileId,
    string ProfileName,
    int? PinnedRevision,
    GameActivityKind Kind,
    Guid? SavegameId,
    string? SavegameName,
    DateTime ChangedAt,
    DateTime TouchedAt);
