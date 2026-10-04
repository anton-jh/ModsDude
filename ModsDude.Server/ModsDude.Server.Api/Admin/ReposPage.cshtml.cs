using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Invites;
using ModsDude.Server.Persistence.Retention;

namespace ModsDude.Server.Api.Admin;

public class ReposPageModel(
    ApplicationDbContext dbContext,
    IUnitOfWork unitOfWork,
    IAdminMemberships memberships,
    IRetentionSweeper retentionSweeper,
    ITimeService timeService,
    ILogger<ReposPageModel> logger)
    : PageModel
{
    private const string _repoGone = "That repo no longer exists.";
    private const string _repoChanged = "The repo changed. Check it again.";


    public StorageComparison? Storage { get; private set; }

    public IReadOnlyList<RepoRow> Repos { get; private set; } = [];

    /// <summary>
    /// Rendered into every create invite form, so a resubmit of one is recognised as a repeat. Request
    /// IDs are per repo, so the forms of different repos can share it.
    /// </summary>
    public Guid InviteRequestId { get; } = Guid.NewGuid();

    /// <summary>The repo whose row is shown open, after an action on it.</summary>
    public Guid? Open { get; private set; }


    public async Task OnGetAsync(Guid? open, CancellationToken cancellationToken)
    {
        Open = open;
        var now = timeService.Now();

        var repos = await dbContext.Repos
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .ToListAsync(cancellationToken);

        var users = await dbContext.Users
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var userOptions = users.Values
            .Where(x => !x.IsBlocked)
            .OrderBy(x => x.DisplayName.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id.Value, StringComparer.Ordinal)
            .Select(x => new UserOption(x.Id.Value, AdminFormat.User(x.Id, x.DisplayName)))
            .ToList();

        var membershipsByRepo = (await dbContext.RepoMemberships
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .ToLookup(x => x.RepoId);

        var invites = (await dbContext.RepoInvites
            .AsNoTracking()
            .Where(x => x.DismissedAt == null && !x.IsRevoked)
            .ToListAsync(cancellationToken))
            .Where(x => x.GetStatus(now) is InviteStatus.Active)
            .ToLookup(x => x.RepoId);

        var mods = await dbContext.ModVersions
            .AsNoTracking()
            .GroupBy(x => x.RepoId)
            .Select(x => new { RepoId = x.Key, Mods = x.Select(y => y.ModId).Distinct().Count(), Versions = x.Count() })
            .ToDictionaryAsync(x => x.RepoId, cancellationToken);

        var profiles = await dbContext.Profiles
            .AsNoTracking()
            .GroupBy(x => x.RepoId)
            .Select(x => new { RepoId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.RepoId, x => x.Count, cancellationToken);

        var savegames = await dbContext.Savegames
            .AsNoTracking()
            .GroupBy(x => x.RepoId)
            .Select(x => new { RepoId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.RepoId, x => x.Count, cancellationToken);

        var modBytes = await dbContext.ModVersions.GetRegisteredBytesPerRepoAsync(cancellationToken);
        var savegameBytes = await dbContext.SavegameSnapshots.GetRegisteredBytesPerRepoAsync(cancellationToken);
        var scheduledMods = await dbContext.ModVersions.GetScheduledForDeletionBytesPerRepoAsync(cancellationToken);
        var scheduledSavegames = await dbContext.SavegameSnapshots.GetScheduledForDeletionBytesPerRepoAsync(cancellationToken);

        var traffic = (await dbContext.FileTransfers.GetTotalsSinceAsync(now.AddDays(-AdminWindows.TrafficDays), cancellationToken))
            .ToLookup(x => x.RepoId);

        Storage = await StorageComparison.LoadAsync(dbContext, cancellationToken);

        Repos = [.. repos
            .OrderBy(x => x.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id.Value)
            .Select(repo =>
            {
                var members = membershipsByRepo[repo.Id]
                    .OrderByDescending(x => x.Level)
                    .ThenBy(x => users[x.UserId].DisplayName.Value, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.UserId.Value, StringComparer.Ordinal)
                    .Select(x => new MemberRow(
                        x.UserId.Value,
                        AdminFormat.User(x.UserId, users[x.UserId].DisplayName),
                        x.Level,
                        users[x.UserId].IsBlocked))
                    .ToList();

                var memberIds = members.Select(x => x.UserId).ToHashSet();

                return new RepoRow(
                    repo.Id.Value,
                    AdminFormat.Repo(repo.Id, repo.Name),
                    repo.Name.Value,
                    repo.AdapterData.Id.Value,
                    repo.Created,
                    repo.ArchivedAt,
                    repo.MembershipRevision,
                    members,
                    [.. userOptions.Where(x => !memberIds.Contains(x.Id))],
                    [.. invites[repo.Id]
                        .OrderByDescending(x => x.Created)
                        .ThenBy(x => x.Id.Value)
                        .Select(x => new InviteRow(
                            x.Id.Value,
                            InviteCodes.Format(x.Code),
                            x.GrantedLevel,
                            x.Created,
                            x.ExpiresAt,
                            x.Uses,
                            x.MaximumUses))],
                    mods.GetValueOrDefault(repo.Id)?.Mods ?? 0,
                    mods.GetValueOrDefault(repo.Id)?.Versions ?? 0,
                    profiles.GetValueOrDefault(repo.Id),
                    savegames.GetValueOrDefault(repo.Id),
                    modBytes.GetValueOrDefault(repo.Id),
                    savegameBytes.GetValueOrDefault(repo.Id),
                    Storage?.ForRepo(repo.Id),
                    scheduledMods.GetValueOrDefault(repo.Id) + scheduledSavegames.GetValueOrDefault(repo.Id),
                    TrafficRow.PerFile(traffic[repo.Id]),
                    TrafficRow.DownloadedBytesOf(traffic[repo.Id]),
                    TrafficRow.UploadedBytesOf(traffic[repo.Id]));
            })];
    }

    public async Task<IActionResult> OnPostRenameAsync(Guid repoId, string? name, CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return ShowError(repoId, "A name cannot be empty.");
        }

        if (await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken) is not { } repo)
        {
            return ShowError(repoId, _repoGone);
        }

        var previous = repo.Name;
        repo.Rename(new RepoName(trimmed));

        if (await CommitAsync(repoId, cancellationToken) is false)
        {
            return ShowError(repoId, _repoChanged);
        }

        logger.LogInformation("Admin {Operator} renamed repo {RepoId} from {Previous} to {Name}.",
            User.OperatorName(), repoId, previous.Value, trimmed);

        return ShowNotice(repoId, $"Renamed to {AdminFormat.Repo(repo.Id, repo.Name)}.");
    }

    public async Task<IActionResult> OnPostArchiveAsync(Guid repoId, CancellationToken cancellationToken)
    {
        if (await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken) is not { } repo)
        {
            return ShowError(repoId, _repoGone);
        }

        repo.Archive(timeService.Now());

        if (await CommitAsync(repoId, cancellationToken) is false)
        {
            return ShowError(repoId, _repoChanged);
        }

        logger.LogInformation("Admin {Operator} archived repo {RepoId}.", User.OperatorName(), repoId);

        return ShowNotice(repoId, $"{AdminFormat.Repo(repo.Id, repo.Name)} is archived.");
    }

    public async Task<IActionResult> OnPostRestoreAsync(Guid repoId, CancellationToken cancellationToken)
    {
        if (await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken) is not { } repo)
        {
            return ShowError(repoId, _repoGone);
        }

        repo.Restore();

        if (await CommitAsync(repoId, cancellationToken) is false)
        {
            return ShowError(repoId, _repoChanged);
        }

        logger.LogInformation("Admin {Operator} restored repo {RepoId}.", User.OperatorName(), repoId);

        return ShowNotice(repoId, $"{AdminFormat.Repo(repo.Id, repo.Name)} is restored.");
    }

    /// <summary>
    /// Only an archived repo, as in the app: archiving is the first of two deliberate acts, and the
    /// one every member can see.
    /// </summary>
    public async Task<IActionResult> OnPostDeleteAsync(Guid repoId, CancellationToken cancellationToken)
    {
        if (await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken) is not { } repo)
        {
            TempData.SetNotice("The repo is deleted.");

            return RedirectToPage();
        }

        if (!repo.IsArchived)
        {
            return ShowError(repoId, "Archive the repo before deleting it.");
        }

        var description = AdminFormat.Repo(repo.Id, repo.Name);

        try
        {
            await dbContext.DeleteWithContentsAsync(repo, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogInformation(exception, "Deleting repo {RepoId} from the admin page lost to a concurrent change.", repoId);
            dbContext.ChangeTracker.Clear();

            return ShowError(repoId, _repoChanged);
        }

        logger.LogInformation("Admin {Operator} deleted repo {RepoId} ({Name}).", User.OperatorName(), repoId, description);
        TempData.SetNotice($"{description} is deleted.");

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPruneAsync(Guid repoId, PrunableHistory history, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(history))
        {
            return ShowError(repoId, "Pick what to prune.");
        }

        var id = new RepoId(repoId);

        if (!await dbContext.Repos.AnyAsync(x => x.Id == id, cancellationToken))
        {
            return ShowError(repoId, _repoGone);
        }

        var result = await retentionSweeper.PruneNowAsync(id, history, timeService.Now(), cancellationToken);

        logger.LogInformation("Admin {Operator} pruned {History} in repo {RepoId}: {Deleted} deleted, {Failures} failures.",
            User.OperatorName(), history, repoId, result.Deleted, result.Failures);

        if (result.Failures > 0)
        {
            TempData.SetError($"Some {Describe(history)} could not be deleted. Run it again.");
        }

        return ShowNotice(repoId, $"Deleted {AdminFormat.Count(result.Deleted)} {Describe(history)}.");
    }

    public async Task<IActionResult> OnPostCreateInviteAsync(
        Guid repoId,
        Guid requestId,
        RepoMembershipLevel level,
        int? maximumUses,
        int? expiresInHours,
        CancellationToken cancellationToken)
    {
        var id = new RepoId(repoId);
        var request = new RepoInviteRequestId(requestId);

        if (await dbContext.RepoInvites.GetByRequestIdAsync(id, request, cancellationToken) is { } repeat)
        {
            return ShowNotice(repoId, InviteCreated(repeat));
        }

        if (await dbContext.Repos.GetAsync(id, cancellationToken) is not { } repo)
        {
            return ShowError(repoId, _repoGone);
        }

        if (repo.IsArchived)
        {
            return ShowError(repoId, "Restore the repo before inviting to it.");
        }

        if (!InvitableLevels.Contains(level))
        {
            return ShowError(repoId, "Pick a level below Admin.");
        }

        if (!ModelState.IsValid || maximumUses is <= 0)
        {
            return ShowError(repoId, "Max joins must be at least 1.");
        }

        if (expiresInHours is not null && !InviteExpiryOptions.Any(x => x.Hours == expiresInHours))
        {
            return ShowError(repoId, "Pick when the invite expires.");
        }

        var now = timeService.Now();
        var invite = await dbContext.IssueAsync(
            code => new RepoInvite(
                id,
                code,
                request,
                level,
                createdBy: null,
                now,
                expiresInHours is int hours ? now.AddHours(hours) : null,
                maximumUses),
            ct => dbContext.RepoInvites.GetByRequestIdAsync(id, request, ct),
            logger,
            cancellationToken);

        logger.LogInformation("Admin {Operator} created invite {InviteId} of repo {RepoId}.", User.OperatorName(), invite.Id.Value, repoId);

        return ShowNotice(repoId, InviteCreated(invite));
    }

    public async Task<IActionResult> OnPostRevokeInviteAsync(Guid repoId, Guid inviteId, CancellationToken cancellationToken)
    {
        var invite = await dbContext.RepoInvites.GetAsync(new RepoInviteId(inviteId), cancellationToken);
        if (invite is null || invite.RepoId != new RepoId(repoId))
        {
            return ShowError(repoId, "That invite no longer exists.");
        }

        var now = timeService.Now();

        if (invite.GetStatus(now) is InviteStatus.Active)
        {
            invite.Revoke(now);

            try
            {
                await unitOfWork.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                logger.LogInformation(exception, "Revoking invite {InviteId} from the admin page lost to a concurrent write.", inviteId);
                dbContext.ChangeTracker.Clear();

                return ShowError(repoId, "That invite changed. Check it again.");
            }

            logger.LogInformation("Admin {Operator} revoked invite {InviteId} of repo {RepoId}.", User.OperatorName(), inviteId, repoId);
        }

        return ShowNotice(repoId, "Invite revoked.");
    }

    public async Task<IActionResult> OnPostAddMemberAsync(Guid repoId, string userId, RepoMembershipLevel level, CancellationToken cancellationToken)
    {
        var error = Enum.IsDefined(level)
            ? await memberships.AddAsync(new RepoId(repoId), new UserId(userId), level, User.OperatorName(), cancellationToken)
            : "Pick a level.";

        return ShowOutcome(repoId, error, "Member added.");
    }

    public async Task<IActionResult> OnPostSetLevelAsync(Guid repoId, string userId, RepoMembershipLevel level, int revision, CancellationToken cancellationToken)
    {
        var error = Enum.IsDefined(level)
            ? await memberships.SetLevelAsync(new RepoId(repoId), new UserId(userId), level, revision, User.OperatorName(), cancellationToken)
            : "Pick a level.";

        return ShowOutcome(repoId, error, "Level changed.");
    }

    public async Task<IActionResult> OnPostRemoveMemberAsync(Guid repoId, string userId, int revision, CancellationToken cancellationToken)
    {
        var error = await memberships.RemoveAsync(new RepoId(repoId), new UserId(userId), revision, User.OperatorName(), cancellationToken);

        return ShowOutcome(repoId, error, "Member removed.");
    }


    /// <returns>False where another change to the repo was saved first.</returns>
    private async Task<bool> CommitAsync(Guid repoId, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.CommitAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogInformation(exception, "An admin change to repo {RepoId} lost to a concurrent change.", repoId);
            dbContext.ChangeTracker.Clear();

            return false;
        }
    }

    private static string InviteCreated(RepoInvite invite) => $"Invite {InviteCodes.Format(invite.Code)} created.";

    private RedirectToPageResult ShowOutcome(Guid repoId, string? error, string notice)
    {
        return error is null ? ShowNotice(repoId, notice) : ShowError(repoId, error);
    }

    private RedirectToPageResult ShowNotice(Guid repoId, string notice)
    {
        TempData.SetNotice(notice);

        return ShowRepo(repoId);
    }

    private RedirectToPageResult ShowError(Guid repoId, string error)
    {
        TempData.SetError(error);

        return ShowRepo(repoId);
    }

    private RedirectToPageResult ShowRepo(Guid repoId)
    {
        return RedirectToPage(null, null, new { open = repoId }, RowAnchor(repoId));
    }


    /// <summary>An invite can never grant Admin; see <see cref="RepoInvite"/>.</summary>
    public static IReadOnlyList<RepoMembershipLevel> InvitableLevels { get; } =
        [.. Enum.GetValues<RepoMembershipLevel>().Where(x => x < RepoMembershipLevel.Admin)];

    /// <summary>The same choices the client offers. <c>null</c> hours is no expiry.</summary>
    public static IReadOnlyList<InviteExpiryOption> InviteExpiryOptions { get; } =
    [
        new("Never", null),
        new("1 hour", 1),
        new("1 day", 24),
        new("7 days", 7 * 24),
        new("30 days", 30 * 24)
    ];

    public static string RowAnchor(Guid repoId) => $"repo-{repoId}";

    public static string Describe(PrunableHistory history) => history switch
    {
        PrunableHistory.SavegameSnapshots => "savegame snapshots",
        PrunableHistory.ProfileRevisions => "profile revisions",
        PrunableHistory.ModVersions => "mod versions",
        _ => throw new ArgumentOutOfRangeException(nameof(history), history, null)
    };


    /// <param name="MembershipRevision">Sent back with any change to an existing membership.</param>
    /// <param name="AvailableUsers">The users who could be added: not members, not blocked.</param>
    /// <param name="ModBytes">Every mod version the repo has registered.</param>
    /// <param name="SavegameBytes">Every distinct file its savegame snapshots refer to.</param>
    /// <param name="Storage">From the latest storage sample, or <c>null</c> before the first one.</param>
    public record RepoRow(
        Guid Id,
        string Name,
        string RawName,
        string Game,
        DateTime Created,
        DateTime? ArchivedAt,
        int MembershipRevision,
        IReadOnlyList<MemberRow> Members,
        IReadOnlyList<UserOption> AvailableUsers,
        IReadOnlyList<InviteRow> Invites,
        int ModCount,
        int VersionCount,
        int ProfileCount,
        int SavegameCount,
        long ModBytes,
        long SavegameBytes,
        RepoStorage? Storage,
        long ScheduledForDeletionBytes,
        IReadOnlyList<TrafficRow> Traffic,
        long DownloadedBytes,
        long UploadedBytes)
    {
        public bool HasActiveAdmin => Members.Any(x => x.Level == RepoMembershipLevel.Admin && !x.IsBlocked);
    }

    public record MemberRow(string UserId, string User, RepoMembershipLevel Level, bool IsBlocked);

    public record UserOption(string Id, string Name);

    public record InviteRow(Guid Id, string Code, RepoMembershipLevel Level, DateTime Created, DateTime? ExpiresAt, int Uses, int? MaximumUses);

    public record InviteExpiryOption(string Label, int? Hours);
}
