using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Admin;

public class TrustCodesPageModel(
    ApplicationDbContext dbContext,
    IUnitOfWork unitOfWork,
    ITimeService timeService,
    ILogger<TrustCodesPageModel> logger)
    : PageModel
{
    private const int _listedCodes = 50;

    /// <summary>The unique index makes a repeat impossible rather than unlikely; this is for the unlikely.</summary>
    private const int _maximumCodeAttempts = 3;


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

    public async Task<IActionResult> OnPostIssueAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = new TrustCodeRequestId(requestId);

        if (await dbContext.TrustCodes.GetByRequestIdAsync(request, cancellationToken) is TrustCode repeat)
        {
            return ShowIssued(repeat);
        }

        for (var attempt = 1; ; attempt++)
        {
            var trustCode = new TrustCode(InviteCodes.Generate(), request, timeService.Now());
            dbContext.TrustCodes.Add(trustCode);

            try
            {
                await unitOfWork.CommitAsync(cancellationToken);
                logger.LogInformation("Admin {Operator} issued trust code {TrustCodeId}.", User.OperatorName(), trustCode.Id.Value);

                return ShowIssued(trustCode);
            }
            catch (DbUpdateException exception) when (attempt < _maximumCodeAttempts)
            {
                dbContext.Entry(trustCode).State = EntityState.Detached;

                // The same form submitted twice at once, or a code that happened to be taken.
                if (await dbContext.TrustCodes.GetByRequestIdAsync(request, cancellationToken) is TrustCode concurrent)
                {
                    return ShowIssued(concurrent);
                }

                logger.LogWarning(exception, "Issuing a trust code collided on attempt {Attempt}; trying a new code.", attempt);
            }
        }
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

            return ShowError("That trust code changed while it was being revoked. Check it again.");
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
