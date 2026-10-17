using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Profiles;

/// <summary>
/// One exact version of a mod this profile will not be offered as an update. Newer versions still are.
/// </summary>
/// <remarks>
/// <para>
/// <b>Part of the profile's ignore list, not of a revision.</b> Like <see cref="ProfileIgnoredMod"/>, it
/// only decides what the editor offers, and is read and written together with the ignored mods.
/// </para>
/// <para>
/// <b>Pinned or not.</b> A dismissal says nothing about what the profile pins, so writing a revision
/// leaves it alone, and a mod that is taken out and added back keeps it.
/// </para>
/// <para>
/// <b>No foreign key onto the mod or the version.</b> The versions most worth dismissing are often
/// only in somebody's folder or online. Deleting the mod or that registered version releases it.
/// </para>
/// </remarks>
public class ProfileDismissedVersion(RepoId repoId, ProfileId profileId, ModId modId, ModVersionId versionId)
{
    public RepoId RepoId { get; } = repoId;
    public ProfileId ProfileId { get; } = profileId;
    public ModId ModId { get; } = modId;
    public ModVersionId VersionId { get; } = versionId;

    public DismissedVersion Key => new(ModId, VersionId);
}

public readonly record struct DismissedVersion(ModId ModId, ModVersionId VersionId);
