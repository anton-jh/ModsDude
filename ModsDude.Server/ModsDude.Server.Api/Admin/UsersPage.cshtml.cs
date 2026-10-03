using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Admin;

public class UsersPageModel(
    ApplicationDbContext dbContext,
    ITimeService timeService)
    : PageModel
{
    public IReadOnlyList<UserRow> Users { get; private set; } = [];


    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var users = await dbContext.Users
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var repos = await dbContext.Repos
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var memberships = (await dbContext.RepoMemberships
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .ToLookup(x => x.UserId);

        var trafficTotals = await dbContext.FileTransfers.GetTotalsSinceAsync(timeService.Now().AddDays(-AdminWindows.TrafficDays), cancellationToken);
        var traffic = trafficTotals.ToLookup(x => x.UserId);
        var trafficPerRepo = trafficTotals.ToLookup(x => (x.UserId, x.RepoId));

        Users = [.. users
            .OrderBy(x => x.DisplayName.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
            .Select(user =>
            {
                var userMemberships = memberships[user.Id]
                    .OrderBy(x => repos[x.RepoId].Name.Value, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.RepoId.Value)
                    .Select(x => new MembershipRow(
                        AdminFormat.Repo(x.RepoId, repos[x.RepoId].Name),
                        x.Level,
                        TrafficRow.DownloadedBytesOf(trafficPerRepo[(user.Id, x.RepoId)]),
                        TrafficRow.UploadedBytesOf(trafficPerRepo[(user.Id, x.RepoId)])))
                    .ToList();

                return new UserRow(
                    AdminFormat.User(user.Id, user.DisplayName),
                    user.Created,
                    user.LastSeen,
                    user.IsTrusted,
                    userMemberships,
                    TrafficRow.DownloadedBytesOf(traffic[user.Id]),
                    TrafficRow.UploadedBytesOf(traffic[user.Id]));
            })];
    }


    /// <param name="DownloadedBytes">Across every repo, including ones the user has since left.</param>
    public record UserRow(
        string Name,
        DateTime Created,
        DateTime LastSeen,
        bool IsTrusted,
        IReadOnlyList<MembershipRow> Memberships,
        long DownloadedBytes,
        long UploadedBytes);

    public record MembershipRow(string Repo, RepoMembershipLevel Level, long DownloadedBytes, long UploadedBytes);
}
