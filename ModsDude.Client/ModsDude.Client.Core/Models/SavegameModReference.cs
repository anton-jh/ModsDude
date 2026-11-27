namespace ModsDude.Client.Core.Models;

/// <summary>A mod a savegame lists as played with, and the version it recorded where it recorded one.</summary>
public sealed record SavegameModReference(ModKey ModId, ModVersionKey? Version);
