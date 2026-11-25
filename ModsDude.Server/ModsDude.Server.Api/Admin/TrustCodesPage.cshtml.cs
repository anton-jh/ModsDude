using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Invites;

namespace ModsDude.Server.Api.Admin;

public class TrustCodesPageModel(
    ApplicationDbContext dbContext,
    IUnitOfWork unitOfWork,
    ITimeService timeService,
    ILogger<TrustCodesPageModel> logger)
    : PageModel
{
    private const int _listedCodes = 50;


    /// <summary>Rendered into the issue form, so a resubmit of the same form is recognised as a repeat.</summary>
    public Guid IssueRequestId { get; } = Guid.NewGuid();

    public TrustCodeRow? Issued { get; private set; }

    public IReadOnlyList<TrustCodeRow> TrustCodes { get; private set; } = [];


    public async Task OnGetAsync(Guid? issued, CancellationToken cancellationToken)
    {
        var now = timeService.Now();
        var trustCodes = await dbContext.TrustCodes.GetLatestAsync(_listedCodes, cancellationToken);

        var redeemers = trustCodes
            .Select(x => x.RedeemedBy)
            .OfType<UserId>()
            .Distinct()
            .ToList();
        var nameplates = await dbContext.Users.GetNameplatesAsync(redeemers, cancellationToken);

        TrustCodes = [.. trustCodes.Select(x => TrustCodeRow.Of(x, now, nameplates))];

        if (issued is Guid issuedId)
        {
            Issued = TrustCodes.FirstOrDefault(x => x.Id == issuedId);
        }
    }

    public async Task<IActionResult> OnPostIssueAsync(Guid requestId, int expiresInHours, CancellationToken cancellationToken)
    {
        var request = new TrustCodeRequestId(requestId);

        if (await dbContext.TrustCodes.GetByRequestIdAsync(request, cancellationToken) is TrustCode repeat)
        {
            return ShowIssued(repeat);
        }

        if (!AdminExpiryOptions.IsOffered(expiresInHours))
        {
            return ShowError("Pick when the trust code expires.");
        }

        var now = timeService.Now();
        var trustCode = await dbContext.IssueAsync(
            code => new TrustCode(code, request, now, now.AddHours(expiresInHours)),
            ct => dbContext.TrustCodes.GetByRequestIdAsync(request, ct),
            logger,
            cancellationToken);

        logger.LogInformation("Admin {Operator} issued trust code {TrustCodeId}.", User.OperatorName(), trustCode.Id.Value);

        return ShowIssued(trustCode);
    }

    public async Task<IActionResult> OnPostChangeExpiryAsync(Guid id, int expiresInHours, CancellationToken cancellationToken)
    {
        if (!AdminExpiryOptions.IsOffered(expiresInHours))
        {
            return ShowError("Pick when the trust code expires.");
        }

        var trustCode = await dbContext.TrustCodes.GetAsync(new TrustCodeId(id), cancellationToken);

        if (trustCode is null)
        {
            return ShowError("That trust code does not exist.");
        }

        if (trustCode.RedeemedAt is not null)
        {
            return ShowError("That trust code has already been redeemed.");
        }

        if (trustCode.RevokedAt is not null)
        {
            return ShowError("That trust code has been revoked.");
        }

        var now = timeService.Now();
        var expiresAt = now.AddHours(expiresInHours);
        trustCode.ChangeExpiry(expiresAt, now);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogInformation(exception, "Changing the expiry of trust code {TrustCodeId} lost to a concurrent write.", id);
            dbContext.ChangeTracker.Clear();

            return ShowError("That trust code changed. Check it again.");
        }

        logger.LogInformation(
            "Admin {Operator} changed the expiry of trust code {TrustCodeId} to {ExpiresAt:O}.",
            User.OperatorName(),
            id,
            expiresAt);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        var trustCode = await dbContext.TrustCodes.GetAsync(new TrustCodeId(id), cancellationToken);

        if (trustCode is null)
        {
            return ShowError("That trust code does not exist.");
        }

        if (trustCode.RedeemedAt is not null)
        {
            return ShowError("That trust code has already been redeemed.");
        }

        trustCode.Revoke(timeService.Now());

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogInformation(exception, "Revoking trust code {TrustCodeId} lost to a concurrent write.", id);
            dbContext.ChangeTracker.Clear();

            return ShowError("That trust code changed. Check it again.");
        }

        logger.LogInformation("Admin {Operator} revoked trust code {TrustCodeId}.", User.OperatorName(), id);

        return RedirectToPage();
    }


    private RedirectToPageResult ShowIssued(TrustCode trustCode)
    {
        // Redirected rather than rendered, so refreshing the page does not post the form again.
        return RedirectToPage(new { issued = trustCode.Id.Value });
    }

    private RedirectToPageResult ShowError(string error)
    {
        TempData.SetError(error);

        return RedirectToPage();
    }


    public record TrustCodeRow(
        Guid Id,
        string Code,
        TrustCodeStatus Status,
        DateTime Created,
        DateTime ExpiresAt,
        string? RedeemedBy,
        DateTime? RedeemedAt)
    {
        public bool CanChangeExpiry => Status is TrustCodeStatus.Active or TrustCodeStatus.Expired;

        public static TrustCodeRow Of(TrustCode trustCode, DateTime now, IReadOnlyDictionary<UserId, UserNameplate> nameplates)
        {
            return new(
                trustCode.Id.Value,
                InviteCodes.Format(trustCode.Code),
                trustCode.GetStatus(now),
                trustCode.Created,
                trustCode.ExpiresAt,
                trustCode.RedeemedBy is UserId redeemer ? DescribeUser(redeemer, nameplates) : null,
                trustCode.RedeemedAt);
        }

        private static string DescribeUser(UserId userId, IReadOnlyDictionary<UserId, UserNameplate> nameplates)
        {
            return nameplates.TryGetValue(userId, out var nameplate)
                ? AdminFormat.User(userId, nameplate.DisplayName)
                : userId.Value;
        }
    }
}
