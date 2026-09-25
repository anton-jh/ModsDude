using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Activity;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;

/// <summary>One friend's game, with the names a list draws it with.</summary>
/// <param name="SavegameName">The savegame checked out, where there was one and it still exists.</param>
public sealed record VisibleGameActivity(
    GameActivity Activity,
    ProfileName ProfileName,
    User User,
    SavegameName? SavegameName);


public static class GameActivityExtensions
{
    public static Task<GameActivity?> GetAsync(this DbSet<GameActivity> set, UserId userId, GameKey game, CancellationToken cancellationToken)
    {
        return set.FirstOrDefaultAsync(x => x.UserId == userId && x.Game == game, cancellationToken);
    }

    /// <summary>
    /// Everybody else's games that <paramref name="viewer"/> may see, most recently touched first.
    /// </summary>
    /// <remarks>
    /// <b>Both of them have to be in the repo the row names.</b> The viewer, because a repo they are
    /// not in is none of their business; the other person, because somebody who has left a repo is not
    /// playing in it with anybody. An archived repo is out of everybody's lists, so it is out of this.
    /// </remarks>
    /// <param name="onlyRepo">One repo's rows, or every repo the viewer is in where null.</param>
    /// <param name="since">Rows last touched before this are left out.</param>
    public static Task<List<VisibleGameActivity>> GetVisibleToAsync(
        this ApplicationDbContext dbContext,
        UserId viewer,
        RepoId? onlyRepo,
        DateTime since,
        CancellationToken cancellationToken)
    {
        var query = dbContext.GameActivities
            .Where(x => x.UserId != viewer && x.TouchedAt >= since);

        if (onlyRepo is RepoId repoId)
        {
            query = query.Where(x => x.RepoId == repoId);
        }

        return query
            .Where(x => dbContext.RepoMemberships.Any(m => m.RepoId == x.RepoId && m.UserId == viewer))
            .Where(x => dbContext.RepoMemberships.Any(m => m.RepoId == x.RepoId && m.UserId == x.UserId))
            .Where(x => dbContext.Repos.Any(r => r.Id == x.RepoId && r.ArchivedAt == null))
            .Join(dbContext.Profiles,
                activity => new { activity.RepoId, activity.ProfileId },
                profile => new { profile.RepoId, ProfileId = profile.Id },
                (activity, profile) => new { Activity = activity, ProfileName = profile.Name })
            .Join(dbContext.Users,
                x => x.Activity.UserId,
                user => user.Id,
                (x, user) => new { x.Activity, x.ProfileName, User = user })
            .OrderByDescending(x => x.Activity.TouchedAt)
            .Select(x => new VisibleGameActivity(
                x.Activity,
                x.ProfileName,
                x.User,
                dbContext.Savegames
                    .Where(s => s.RepoId == x.Activity.RepoId && s.Id == x.Activity.SavegameId)
                    .Select(s => (SavegameName?)s.Name)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }
}
