using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Tests.Services;

internal sealed class FakeKnownRepos : IKnownRepos
{
    public HashSet<Guid> Gone { get; } = [];

    public bool IsGone(Guid repoId) => Gone.Contains(repoId);
}
