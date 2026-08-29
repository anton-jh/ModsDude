namespace ModsDude.Client.Core.Models;

/// <summary>
/// The profile an instance's mod folder is meant to match. A source of truth, not a cache: a folder
/// cannot tell you which profile it was meant to be, so losing this loses the intent for good.
/// </summary>
public readonly record struct ActiveProfile(Guid RepoId, Guid ProfileId);
