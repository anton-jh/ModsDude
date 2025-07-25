﻿using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Repos;
public class Repo
{
    private readonly HashSet<RepoMembership> _memberships = [];


    // ef
    private Repo() { }

    public Repo(RepoName name, DateTime created, UserId firstAdmin)
    {
        Name = name;
        Created = created;
        UpdateMembershipLevel(firstAdmin, RepoMembershipLevel.Admin);
    }


    public RepoId Id { get; init; } = new(Guid.NewGuid());

    public RepoName Name { get; set; }
    public required AdapterData AdapterData { get; set; }
    public DateTime Created { get; }


    public void AddMember(UserId userId, RepoMembershipLevel level)
    {
        if (_memberships.FirstOrDefault(x => x.UserId == userId) is RepoMembership existing)
        {
            throw new InvalidOperationException($"Cannot add member '{userId.Value}' to '{Id.Value}'. User is already a member.");
        }
        else
        {
            var membership = new RepoMembership(
                userId, Id, level);
            _memberships.Add(membership);
        }
    }
    
    public void UpdateMembershipLevel(UserId userId, RepoMembershipLevel level)
    {
        if (_memberships.FirstOrDefault(x => x.UserId == userId) is RepoMembership existing)
        {
            existing.Level = level;
        }
        else
        {
            var membership = new RepoMembership(
                userId, Id, level);
            _memberships.Add(membership);
        }
    }

    public void KickMember(UserId userId)
    {
        var membership = _memberships.FirstOrDefault(x => x.UserId == userId)
            ?? throw new InvalidOperationException($"User '{userId}' is not a member of repo '{Id}'");

        if (IsOnlyAdmin(userId))
        {
            throw new InvalidOperationException("Cannot kick the only Admin of a repo");
        }

        _memberships.Remove(membership);
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
public readonly record struct RepoName(string Value);

public record AdapterData(AdapterIdentifier Id, AdapterConfiguration Configuration);
public readonly record struct AdapterIdentifier(string Value);
public readonly record struct AdapterConfiguration(string Value);
