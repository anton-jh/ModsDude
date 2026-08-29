using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

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


    public ModDependency AddDependency(ModVersion modVersion, bool locked)
    {
        if (modVersion.RepoId != RepoId)
        {
            throw new InvalidOperationException($"Cannot add dependency to mod with id '{modVersion.ModId}'. Mod belongs to another repo");
        }

        if (_modDependencies.Any(x => x.ModVersion.ModId == modVersion.ModId))
        {
            throw new InvalidOperationException($"Dependency to mod with id '{modVersion.ModId}' already exists");
        }

        var newDependency = new ModDependency()
        {
            ModVersion = modVersion,
            Locked = locked
        };

        _modDependencies.Add(newDependency);

        return newDependency;
    }

    public void DeleteDependency(ModDependency dependency)
    {
        if (!_modDependencies.Contains(dependency))
        {
            throw DependencyNotFoundThrowHelper(dependency.ModVersion.ModId);
        }

        _modDependencies.Remove(dependency);
    }

    public void DeleteDependency(ModId modId)
    {
        var dependency = _modDependencies.FirstOrDefault(x => x.ModVersion.ModId == modId)
            ?? throw DependencyNotFoundThrowHelper(modId);

        _modDependencies.Remove(dependency);
    }

    public bool HasDependencyOn(ModId modId)
        => _modDependencies.Any(x => x.ModVersion.ModId == modId);



    private InvalidOperationException DependencyNotFoundThrowHelper(ModId modId)
        => new InvalidOperationException($"Cannot delete dependency on mod '{modId}' from profile '{Id}'. Dependency does not belong to profile");
}

public readonly record struct ProfileId(Guid Value);
public readonly record struct ProfileName(string Value);
