using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Repos;
public class Repo : IArchivable
{
    private readonly HashSet<RepoMembership> _memberships = [];


    // ef
    private Repo() { }

    public Repo(RepoName name, DateTime created, User firstAdmin)
    {
        Name = name;
        Created = created;
        AddMember(firstAdmin, RepoMembershipLevel.Admin);
    }


    public RepoId Id { get; init; } = new(Guid.NewGuid());

    public RepoName Name { get; private set; }
    public required AdapterData AdapterData { get; set; }
    public DateTime Created { get; }

    /// <inheritdoc cref="IArchivable.ArchivedAt"/>
    public DateTime? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    /// <summary>
    /// Counts changes to the repo itself - its name, its game settings, archiving it. A concurrency token,
    /// and what a client sends back to say which repo its change was made against.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Counts membership changes. A concurrency token, so two requests that each passed the only-Admin
    /// check against the same memberships cannot both be saved.
    /// </summary>
    public int MembersVersion { get; private set; }


    public void Rename(RepoName name)
    {
        if (Name == name)
        {
            return;
        }

        Name = name;
        Version++;
    }

    public void Configure(AdapterConfiguration configuration)
    {
        if (AdapterData.Configuration == configuration)
        {
            return;
        }

        AdapterData = AdapterData with { Configuration = configuration };
        Version++;
    }

    /// <summary>
    /// Puts the repo away for everybody - it is repo state, not membership state, so there is no
    /// per-person version of this. Idempotent, and it does not restamp: an archive holding several
    /// repos of the same name is read by when each of them was put away.
    /// </summary>
    public void Archive(DateTime now)
    {
        if (ArchivedAt is not null)
        {
            return;
        }

        ArchivedAt = now;
        Version++;
    }

    /// <summary>
    /// Brings it back under the name it went away with. Nothing can be in the way: repo names are
    /// not unique, so an archived repo never gave its name up and never has to ask for another.
    /// </summary>
    public void Restore()
    {
        if (ArchivedAt is null)
        {
            return;
        }

        ArchivedAt = null;
        Version++;
    }


    public void AddMember(User user, RepoMembershipLevel level)
    {
        if (user.IsBlocked)
        {
            throw new DomainValidationException($"Cannot add blocked user '{user.Id.Value}' to repo '{Id.Value}'.");
        }

        if (HasMember(user.Id))
        {
            throw new DomainValidationException($"User '{user.Id.Value}' is already a member of repo '{Id.Value}'.");
        }

        _memberships.Add(new RepoMembership(user.Id, Id, level));
        MembersVersion++;
    }

    /// <summary>
    /// Demoting the only Admin is refused for the same reason kicking them is: it leaves a repo
    /// nobody can administer, and no remaining member can undo it. Setting the level a member already
    /// has changes nothing, so a repeated request does not count as a change.
    /// </summary>
    public void UpdateMembershipLevel(UserId userId, RepoMembershipLevel level)
    {
        var membership = GetMembership(userId)
            ?? throw new DomainValidationException($"User '{userId.Value}' is not a member of repo '{Id.Value}'.");

        if (membership.Level == level)
        {
            return;
        }

        if (level < RepoMembershipLevel.Admin && IsOnlyAdmin(userId))
        {
            throw new DomainValidationException($"Cannot demote the only Admin of repo '{Id.Value}'.");
        }

        membership.Level = level;
        MembersVersion++;
    }

    public void KickMember(UserId userId)
    {
        var membership = GetMembership(userId)
            ?? throw new DomainValidationException($"User '{userId.Value}' is not a member of repo '{Id.Value}'.");

        if (IsOnlyAdmin(userId))
        {
            throw new DomainValidationException($"Cannot kick the only Admin of repo '{Id.Value}'.");
        }

        _memberships.Remove(membership);
        MembersVersion++;
    }

    public bool HasMember(UserId userId)
    {
        return _memberships.Any(x => x.UserId == userId);
    }

    public RepoMembership? GetMembership(UserId userId)
    {
        return _memberships.FirstOrDefault(x => x.UserId == userId);
    }

    public bool IsOnlyAdmin(UserId userId)
    {
        var numberOfAdmins = _memberships.Count(x => x.Level == RepoMembershipLevel.Admin);
        var membership = _memberships.FirstOrDefault(x => x.UserId == userId);

        return membership?.Level == RepoMembershipLevel.Admin
            && numberOfAdmins == 1;
    }
}

public readonly record struct RepoId(Guid Value);

/// <summary>
/// What a repo is called. Nothing makes it unique and nothing here tries to: two groups who both
/// called theirs Vanilla both have a Vanilla, and a list showing both of them disambiguates at the
/// point of display with <see cref="RepoTag"/> rather than by making somebody rename theirs.
/// </summary>
public readonly record struct RepoName(string Value);

public record AdapterData(AdapterIdentifier Id, AdapterConfiguration Configuration);
public readonly record struct AdapterIdentifier(string Value);
public readonly record struct AdapterConfiguration(string Value);
