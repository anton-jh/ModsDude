using ModsDude.Client.Core.Concurrency;

namespace ModsDude.Client.Core.Repos;

/// <summary>
/// Everything this machine could be doing for one repo, as a single claim: what leaving or archiving
/// the repo has to wait for, since it takes the repo away from under whatever works on it.
/// </summary>
public interface IRepoWork
{
    /// <summary>Claims all of it, or none of it where something already holds a part. Never waits.</summary>
    IResourceLease? TryClaim(Guid repoId, string holder);

    /// <summary>What is working for the repo, named for the person waiting. Null when nothing is.</summary>
    string? DescribeBusy(Guid repoId);
}
