using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using System.Diagnostics;

namespace ModsDude.Server.Application.Authorization;

public static class FluentAuthorizationBuilderExtensions
{
    public static FluentAuthorizationBuilder AccessRepoAtLevel(this FluentAuthorizationBuilder builder,
        RepoId repoId, RepoMembershipLevel minimumMembershipLevel)
    {
        if (builder.Result is not null)
        {
            return builder;
        }
        
        var membership = builder.User.RepoMemberships.FirstOrDefault(x => x.RepoId == repoId);

        if (membership is null || membership.Level < minimumMembershipLevel)
        {
            builder.Result = new AuthorizationResult.InsufficientRepoAccess(membership?.Level, minimumMembershipLevel);
        }

        return builder;
    }

    public static FluentAuthorizationBuilder GrantAccessToRepo(this FluentAuthorizationBuilder builder,
        RepoId repoId, RepoMembershipLevel level)
    {
        if (builder.Result is not null)
        {
            return builder;
        }

        var membership = builder.User.RepoMemberships.FirstOrDefault(x => x.RepoId == repoId);

        if (membership is null || membership.Level < level)
        {
            builder.Result = new AuthorizationResult.InsufficientRepoAccess(membership?.Level, level);
        }

        return builder;
    }

    public static FluentAuthorizationBuilder ChangeOthersMembership(this FluentAuthorizationBuilder builder,
        RepoMembership subjectMembership)
    {
        if (builder.Result is not null)
        {
            return builder;
        }

        var membership = builder.User.RepoMemberships.FirstOrDefault(x => x.RepoId == subjectMembership.RepoId);

        var neededLevel = subjectMembership.Level switch
        {
            RepoMembershipLevel.Guest => RepoMembershipLevel.Member,
            RepoMembershipLevel.Member => RepoMembershipLevel.Admin,
            RepoMembershipLevel.Admin => RepoMembershipLevel.Admin,
            _ => throw new UnreachableException("Invalid enum value")
        };

        if (membership is null || membership.Level < neededLevel)
        {
            builder.Result = new AuthorizationResult.InsufficientRepoAccess(membership?.Level, neededLevel);
        }

        return builder;
    }
}   
