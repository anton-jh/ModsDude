using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Domain.Profiles;

public class ModDependency
{
    public required ModVersion ModVersion { get; set; }
    public required bool LockVersion { get; set; }


    public bool CanBeUpgraded()
    {
        return ModVersion.Mod.Versions
            .Any(x => x.SequenceNumber > ModVersion.SequenceNumber);
    }

    public void Upgrade()
    {
        ModVersion = ModVersion.Mod.GetLatestVersion();
    }

    public void ChangeVersion(ModVersionId modVersionId)
    {
        var newVersion = ModVersion.Mod.Versions.FirstOrDefault(x => x.Id == modVersionId)
            ?? throw new InvalidOperationException($"Cannot change dependency on mod '{ModVersion.Mod.Id}' to version '{modVersionId}'. No such version exists");

        ModVersion = newVersion;
    }
}
