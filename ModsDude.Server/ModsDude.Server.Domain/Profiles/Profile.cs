using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ModsDude.Server.Domain.Profiles;
public class Profile(
    RepoId repoId,
    ProfileName name,
    DateTime created)
{
    private readonly HashSet<ModDependency> _modDependencies = [];


    public ProfileId Id { get; init; } = new(Guid.NewGuid());
    public RepoId RepoId { get; } = repoId;

    public IReadOnlySet<ModDependency> ModDependencies => _modDependencies;

    public ProfileName Name { get; set; } = name;
    public DateTime Created { get; } = created;


    public ModDependency AddDependency(ModVersion modVersion, bool lockVersion)
    {
        if (modVersion.Mod.RepoId != RepoId)
        {
            throw new InvalidOperationException($"Cannot add dependency to mod with id '{modVersion.Mod.Id}'. Mod belongs to another repo");
        }

        if (_modDependencies.Any(x => x.ModVersion.Mod == modVersion.Mod))
        {
            throw new InvalidOperationException($"Dependency to mod with id '{modVersion.Mod.Id}' already exists");
        }

        var newDependency = new ModDependency()
        {
            ModVersion = modVersion,
            LockVersion = lockVersion
        };

        _modDependencies.Add(newDependency);
        
        return newDependency;
    }

    public void DeleteDependency(ModDependency dependency)
    {
        if (!_modDependencies.Contains(dependency))
        {
            throw DependencyNotFoundThrowHelper(dependency.ModVersion.Mod.Id);
        }

        _modDependencies.Remove(dependency);
    }

    public void DeleteDependency(ModId modId)
    {
        var dependency = _modDependencies.FirstOrDefault(x => x.ModVersion.Mod.Id == modId)
            ?? throw DependencyNotFoundThrowHelper(modId);

        _modDependencies.Remove(dependency);
    }

    public bool HasDependencyOn(ModId modId)
        => _modDependencies.Any(x => x.ModVersion.Mod.Id == modId);
        


    private InvalidOperationException DependencyNotFoundThrowHelper(ModId modId)
        => new InvalidOperationException($"Cannot delete dependency on mod '{modId}' from profile '{Id}'. Dependency does not belong to profile");
}

public readonly record struct ProfileId(Guid Value);
public readonly record struct ProfileName(string Value);
