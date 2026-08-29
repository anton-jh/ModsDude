using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Domain.Profiles;

public class ModDependency
{
    public required ModVersion ModVersion { get; set; }
    public required bool Locked { get; set; }

    /// <summary>
    /// A mod is held in a profile's mod list when either the profile pins it or the adapter marked
    /// the mod itself version-sensitive. Keeping the disjunction here puts the rule in one place.
    /// </summary>
    public bool IsEffectivelyLocked => Locked || ModVersion.Locked;


    /// <summary>
    /// <paramref name="siblingVersions"/> is every version sharing this dependency's
    /// <c>(RepoId, ModId)</c>. It is passed in because a version has no parent to reach them
    /// through.
    /// </summary>
    public bool CanBeUpgraded(IReadOnlyCollection<ModVersion> siblingVersions)
    {
        return siblingVersions.Any(x => x.SequenceNumber > ModVersion.SequenceNumber);
    }

    /// <inheritdoc cref="CanBeUpgraded"/>
    public void Upgrade(IReadOnlyCollection<ModVersion> siblingVersions)
    {
        var latest = siblingVersions.MaxBy(x => x.SequenceNumber)
            ?? throw new InvalidOperationException($"Cannot upgrade dependency on mod '{ModVersion.ModId}'. No versions were supplied");

        ChangeVersion(latest);
    }

    public void ChangeVersion(ModVersion newVersion)
    {
        if (newVersion.RepoId != ModVersion.RepoId || newVersion.ModId != ModVersion.ModId)
        {
            throw new InvalidOperationException($"Cannot change dependency on mod '{ModVersion.ModId}' to version '{newVersion.Id}' of mod '{newVersion.ModId}'. The version belongs to another mod");
        }

        ModVersion = newVersion;
    }
}
