using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Invites;

/// <summary>Invites as the repo's list shows them, with whether each inviter still backs theirs.</summary>
internal static class InviteReads
{
    public static async Task<List<RepoInviteDto>> ToDtosAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        IReadOnlyList<RepoInvite> invites,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var levels = await dbContext.RepoMemberships.GetLevelsAsync(repoId, cancellationToken);

        var inviters = invites
            .Select(x => x.CreatedBy)
            .OfType<UserId>()
            .Distinct()
            .ToList();

        var names = await dbContext.Users.GetNameplatesAsync(inviters, cancellationToken);

        return [.. invites.Select(x => x.CreatedBy is UserId inviter
            ? RepoInviteDto.FromModel(
                x,
                now,
                levels.TryGetValue(inviter, out var level) ? level : null,
                UserDto.Of(inviter, names.TryGetValue(inviter, out var name) ? name : null))
            : RepoInviteDto.FromModel(x, now, inviterLevel: null, createdBy: null))];
    }
}
