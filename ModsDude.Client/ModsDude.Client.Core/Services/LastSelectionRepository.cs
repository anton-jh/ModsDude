using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Services;

/// <summary>
/// Where the user last was, so the app comes back to it instead of to the top of the list.
/// </summary>
/// <remarks>
/// Kept as most-recently-used lists rather than a single id each. One repo is restored at startup,
/// but a profile is restored per repo, and a list keyed by nothing but recency gives that for free:
/// profile ids are unique across repos, so the first remembered id that the repo actually offers is
/// the one the user was on there.
/// </remarks>
public class LastSelectionRepository(
    StateStore store)
{
    /// <summary>
    /// Enough to cover the repos and profiles anybody keeps in rotation. The list is rewritten on
    /// every navigation, so it is bounded on principle rather than because the size matters.
    /// </summary>
    private const int _maxRemembered = 32;


    public Guid? GetLastRepo(IEnumerable<Guid> offered)
    {
        return FindFirstOffered(store.Get().LastSelectedRepos, offered);
    }

    public Guid? GetLastProfile(IEnumerable<Guid> offered)
    {
        return FindFirstOffered(store.Get().LastSelectedProfiles, offered);
    }

    public void RecordRepo(Guid repoId)
    {
        Record(store.Get().LastSelectedRepos, repoId);
    }

    public void RecordProfile(Guid profileId)
    {
        Record(store.Get().LastSelectedProfiles, profileId);
    }


    private static Guid? FindFirstOffered(List<Guid> remembered, IEnumerable<Guid> offered)
    {
        var candidates = offered.ToHashSet();

        foreach (var id in remembered)
        {
            if (candidates.Contains(id))
            {
                return id;
            }
        }

        return null;
    }

    private void Record(List<Guid> remembered, Guid id)
    {
        if (remembered.FirstOrDefault() == id)
        {
            return;
        }

        remembered.Remove(id);
        remembered.Insert(0, id);

        if (remembered.Count > _maxRemembered)
        {
            remembered.RemoveRange(_maxRemembered, remembered.Count - _maxRemembered);
        }

        store.Save();
    }
}
