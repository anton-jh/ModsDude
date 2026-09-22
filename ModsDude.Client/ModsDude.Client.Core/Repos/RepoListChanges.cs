using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Repos;

/// <summary>
/// The parts of a repo in the sidebar that the server can change behind this machine's back, frozen
/// at one moment so two moments can be compared.
/// </summary>
/// <param name="AdapterConfiguration">
/// The base settings as the server last sent them. Compared as text: the server stores and returns
/// the string it was given, so text that differs is settings somebody saved.
/// </param>
public sealed record RepoListEntry(Guid Id, string Name, RepoMembershipLevel MembershipLevel, string AdapterConfiguration);


public static class RepoListChanges
{
    /// <summary>
    /// What would change about <paramref name="local"/> if it were refreshed to
    /// <paramref name="remote"/>, or null where nothing would.
    /// </summary>
    /// <remarks>
    /// Exactly the fields <see cref="Services.RepoRepository.RefreshRepos"/> folds in - a difference
    /// the refresh would not apply is one the user would press the button for and see nothing come
    /// of, and the dot would still be there on the next check.
    /// </remarks>
    public static RemoteChanges? Between(IReadOnlyList<RepoListEntry> local, IEnumerable<RepoMembershipDto> remote)
    {
        var lines = new List<string>();
        var localById = local.ToDictionary(x => x.Id);
        var remoteIds = new HashSet<Guid>();

        foreach (var dto in remote)
        {
            remoteIds.Add(dto.Repo.Id);

            if (localById.TryGetValue(dto.Repo.Id, out var existing) is false)
            {
                lines.Add($"'{dto.Repo.Name}' was added.");

                continue;
            }

            if (existing.Name != dto.Repo.Name)
            {
                lines.Add($"'{existing.Name}' was renamed to '{dto.Repo.Name}'.");
            }

            if (existing.MembershipLevel != dto.MembershipLevel)
            {
                lines.Add($"You are now {Article(dto.MembershipLevel)} {dto.MembershipLevel} in '{dto.Repo.Name}'.");
            }

            if (existing.AdapterConfiguration != dto.Repo.AdapterConfiguration)
            {
                lines.Add($"'{dto.Repo.Name}' had its game settings changed.");
            }
        }

        // Archived, deleted, or this account taken out of it - the list cannot tell which, and all
        // three mean the same thing to the sidebar.
        foreach (var gone in local.Where(x => remoteIds.Contains(x.Id) is false))
        {
            lines.Add($"'{gone.Name}' was removed.");
        }

        return lines.Count == 0 ? null : new RemoteChanges(lines);
    }


    private static string Article(RepoMembershipLevel level)
    {
        return level is RepoMembershipLevel.Admin ? "an" : "a";
    }
}
