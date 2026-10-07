using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Admin;

public class UsersPageModel(
    ApplicationDbContext dbContext,
    IUnitOfWork unitOfWork,
    IAdminMemberships memberships,
    ITimeService timeService,
    ILogger<UsersPageModel> logger)
    : PageModel
{
    private const string _userGone = "That user no longer exists.";


    public IReadOnlyList<UserRow> Users { get; private set; } = [];

    /// <summary>The user whose row is shown open, after an action on it.</summary>
    public string? Open { get; private set; }


    public async Task OnGetAsync(string? open, CancellationToken cancellationToken)
    {
        Open = open;

        var users = await dbContext.Users
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var repos = await dbContext.Repos
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var repoOptions = repos.Values
            .OrderBy(x => x.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id.Value)
            .Select(x => new RepoOption(x.Id.Value, AdminFormat.Repo(x.Id, x.Name)))
            .ToList();

        var membershipsByUser = (await dbContext.RepoMemberships
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
                var userMemberships = membershipsByUser[user.Id]
                    .OrderBy(x => repos[x.RepoId].Name.Value, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.RepoId.Value)
                    .Select(x => new MembershipRow(
                        x.RepoId.Value,
                        AdminFormat.Repo(x.RepoId, repos[x.RepoId].Name),
                        x.Level,
                        repos[x.RepoId].MembersVersion,
                        TrafficRow.DownloadedBytesOf(trafficPerRepo[(user.Id, x.RepoId)]),
                        TrafficRow.UploadedBytesOf(trafficPerRepo[(user.Id, x.RepoId)])))
                    .ToList();

                var memberOf = userMemberships.Select(x => x.RepoId).ToHashSet();

                return new UserRow(
                    user.Id.Value,
                    AdminFormat.User(user.Id, user.DisplayName),
                    user.DisplayName.Value,
                    user.AvatarHash is not null,
                    user.Created,
                    user.LastSeen,
                    user.IsTrusted,
                    user.BlockedAt,
                    userMemberships,
                    [.. repoOptions.Where(x => !memberOf.Contains(x.Id))],
                    TrafficRow.DownloadedBytesOf(traffic[user.Id]),
                    TrafficRow.UploadedBytesOf(traffic[user.Id]));
            })];
    }

    public async Task<IActionResult> OnPostBlockAsync(string userId, CancellationToken cancellationToken)
    {
        if (await dbContext.Users.GetAsync(new UserId(userId), cancellationToken) is not { } user)
        {
            return ShowError(userId, _userGone);
        }

        user.Block(timeService.Now());
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Admin {Operator} blocked user {UserId}.", User.OperatorName(), userId);

        return ShowNotice(userId, $"{Describe(user)} is blocked.");
    }

    public async Task<IActionResult> OnPostUnblockAsync(string userId, CancellationToken cancellationToken)
    {
        if (await dbContext.Users.GetAsync(new UserId(userId), cancellationToken) is not { } user)
        {
            return ShowError(userId, _userGone);
        }

        user.Unblock();
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Admin {Operator} unblocked user {UserId}.", User.OperatorName(), userId);

        return ShowNotice(userId, $"{Describe(user)} is unblocked.");
    }

    public async Task<IActionResult> OnPostGrantTrustAsync(string userId, CancellationToken cancellationToken)
    {
        if (await dbContext.Users.GetAsync(new UserId(userId), cancellationToken) is not { } user)
        {
            return ShowError(userId, _userGone);
        }

        user.GrantTrust();
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Admin {Operator} granted trust to user {UserId}.", User.OperatorName(), userId);

        return ShowNotice(userId, $"{Describe(user)} is trusted.");
    }

    public async Task<IActionResult> OnPostRevokeTrustAsync(string userId, CancellationToken cancellationToken)
    {
        if (await dbContext.Users.GetAsync(new UserId(userId), cancellationToken) is not { } user)
        {
            return ShowError(userId, _userGone);
        }

        user.RevokeTrust();
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Admin {Operator} revoked trust from user {UserId}.", User.OperatorName(), userId);

        return ShowNotice(userId, $"{Describe(user)} is no longer trusted.");
    }

    public async Task<IActionResult> OnPostRenameAsync(string userId, string? name, CancellationToken cancellationToken)
    {
        if (!DisplayName.TryParse(name, out var displayName, out var error))
        {
            return ShowError(userId, error);
        }

        if (await dbContext.Users.GetAsync(new UserId(userId), cancellationToken) is not { } user)
        {
            return ShowError(userId, _userGone);
        }

        var previous = user.DisplayName;
        user.Rename(displayName, timeService.Now());
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Admin {Operator} renamed user {UserId} from {Previous} to {Name}.",
            User.OperatorName(), userId, previous.Value, displayName.Value);

        return ShowNotice(userId, $"Renamed to {Describe(user)}.");
    }

    public async Task<IActionResult> OnPostRemoveAvatarAsync(string userId, CancellationToken cancellationToken)
    {
        if (await dbContext.Users.GetAsync(new UserId(userId), cancellationToken) is not { } user)
        {
            return ShowError(userId, _userGone);
        }

        user.SetAvatar(null, timeService.Now());
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Admin {Operator} removed the avatar of user {UserId}.", User.OperatorName(), userId);

        return ShowNotice(userId, $"Removed the avatar of {Describe(user)}.");
    }

    public async Task<IActionResult> OnPostAddMembershipAsync(string userId, Guid repoId, RepoMembershipLevel level, CancellationToken cancellationToken)
    {
        var error = Enum.IsDefined(level)
            ? await memberships.AddAsync(new RepoId(repoId), new UserId(userId), level, User.OperatorName(), cancellationToken)
            : "Pick a level.";

        return ShowOutcome(userId, error, "Membership added.");
    }

    public async Task<IActionResult> OnPostSetLevelAsync(string userId, Guid repoId, RepoMembershipLevel level, int revision, CancellationToken cancellationToken)
    {
        var error = Enum.IsDefined(level)
            ? await memberships.SetLevelAsync(new RepoId(repoId), new UserId(userId), level, revision, User.OperatorName(), cancellationToken)
            : "Pick a level.";

        return ShowOutcome(userId, error, "Level changed.");
    }

    public async Task<IActionResult> OnPostRemoveMembershipAsync(string userId, Guid repoId, int revision, CancellationToken cancellationToken)
    {
        var error = await memberships.RemoveAsync(new RepoId(repoId), new UserId(userId), revision, User.OperatorName(), cancellationToken);

        return ShowOutcome(userId, error, "Membership removed.");
    }


    private RedirectToPageResult ShowOutcome(string userId, string? error, string notice)
    {
        return error is null ? ShowNotice(userId, notice) : ShowError(userId, error);
    }

    private RedirectToPageResult ShowNotice(string userId, string notice)
    {
        TempData.SetNotice(notice);

        return ShowUser(userId);
    }

    private RedirectToPageResult ShowError(string userId, string error)
    {
        TempData.SetError(error);

        return ShowUser(userId);
    }

    private RedirectToPageResult ShowUser(string userId)
    {
        return RedirectToPage(null, null, new { open = userId }, RowAnchor(userId));
    }

    private static string Describe(User user) => AdminFormat.User(user.Id, user.DisplayName);


    public static string RowAnchor(string userId) => $"user-{userId}";


    /// <param name="DownloadedBytes">Across every repo, including ones the user has since left.</param>
    /// <param name="AvailableRepos">The repos the user could be added to.</param>
    public record UserRow(
        string Id,
        string Name,
        string DisplayName,
        bool HasAvatar,
        DateTime Created,
        DateTime LastSeen,
        bool IsTrusted,
        DateTime? BlockedAt,
        IReadOnlyList<MembershipRow> Memberships,
        IReadOnlyList<RepoOption> AvailableRepos,
        long DownloadedBytes,
        long UploadedBytes);

    /// <param name="Revision">The repo's membership revision, sent back with any change to it.</param>
    public record MembershipRow(Guid RepoId, string Repo, RepoMembershipLevel Level, int Revision, long DownloadedBytes, long UploadedBytes);

    public record RepoOption(Guid Id, string Name);
}
