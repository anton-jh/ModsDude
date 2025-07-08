using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Users;
public class User(UserId id, Username username, DateTime created)
{
    private readonly HashSet<RepoMembership> _repoMemberships = [];


    public UserId Id { get; private set; } = id;

    public Username Username { get; set; } = username;
    public DateTime Created { get; init; } = created;
    public DateTime LastSeen { get; set; } = created;
    public DateTime ProfileLastUpdated { get; set; } = created;
    public bool IsTrusted { get; private set; } = false;

    public IEnumerable<RepoMembership> RepoMemberships => _repoMemberships;
}

public readonly record struct UserId(string Value);
public readonly record struct Username(string Value);
