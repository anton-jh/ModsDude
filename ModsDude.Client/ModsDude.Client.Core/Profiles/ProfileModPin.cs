using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// One mod, pinned by a profile at one version. Keyed by <see cref="ModId"/> alone, because the
/// domain allows a profile exactly one dependency per mod - a profile is a pinned list, not a set of
/// constraints to be solved.
/// </summary>
public sealed record ProfileModPin(ModKey ModId, ModVersionKey VersionId, ProfileModLock Lock);
