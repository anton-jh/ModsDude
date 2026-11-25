using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Api.Invites;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Accounts;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.EntityTypeConfigurations;
using ModsDude.Server.Persistence.Extensions;

namespace ModsDude.Server.Api.Accounts.SignUp;

public class AccountSignUp(
    ApplicationDbContext dbContext,
    UserManager<UserAccount> userManager,
    IInviteCodeLookup inviteCodes,
    ITimeService timeService,
    ILogger<AccountSignUp> logger)
    : IAccountSignUp
{
    public async Task<SignUpOutcome> SignUpAsync(SignUpForm form, CancellationToken cancellationToken)
    {
        // Before the codes are looked at: a resubmit finds them used, by this very sign-up.
        if (await FindRepeatAsync(form, cancellationToken) is SignUpOutcome repeat)
        {
            return repeat;
        }

        var errors = new List<SignUpError>();

        if (!DisplayName.TryParse(form.DisplayName, out var displayName, out var nameError))
        {
            errors.Add(new(SignUpField.DisplayName, nameError));
        }

        var trustCode = await FindTrustCodeAsync(form.TrustCode, errors, cancellationToken);
        var invites = await FindInvitesAsync(form.InviteCodes, errors, cancellationToken);

        if (string.IsNullOrWhiteSpace(form.TrustCode) && form.InviteCodes.All(string.IsNullOrWhiteSpace))
        {
            errors.Add(new(SignUpField.Form, "Enter a trust code or at least one invite code."));
        }

        if (errors.Count > 0)
        {
            return new SignUpOutcome.Refused(errors);
        }

        return await CreateAsync(form, displayName, trustCode, invites, cancellationToken);
    }


    private async Task<SignUpOutcome> CreateAsync(
        SignUpForm form,
        DisplayName displayName,
        TrustCode? trustCode,
        IReadOnlyList<RepoInviteRedemption> invites,
        CancellationToken cancellationToken)
    {
        var userId = new UserId(Guid.NewGuid().ToString());
        var email = form.Email.Trim();

        // Tracked but not saved yet: the password rules find the user, and its name, in the change tracker.
        dbContext.Users.Add(UserSignUp.Create(userId, displayName, timeService.Now(), trustCode, invites));

        var account = new UserAccount
        {
            Id = userId.Value,
            UserName = userId.Value,
            Email = email,
            SignUpRequestId = form.RequestId
        };

        // Before the transaction, so the breached-password check is not made while the codes' rows are held.
        var passwordErrors = await ValidatePasswordAsync(account, form.Password);
        if (passwordErrors.Count > 0)
        {
            dbContext.ChangeTracker.Clear();

            return new SignUpOutcome.Refused([.. passwordErrors.Select(Describe)]);
        }

        account.PasswordHash = userManager.PasswordHasher.HashPassword(account, form.Password);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            var created = await userManager.CreateAsync(account);
            if (!created.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();

                return new SignUpOutcome.Refused([.. created.Errors.Select(Describe)]);
            }

            await transaction.CommitAsync(cancellationToken);

            return new SignUpOutcome.Succeeded(account.Id, email, IsRepeat: false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();

            logger.LogInformation(exception, "Sign-up lost a race for one of its codes.");

            return new SignUpOutcome.Refused([new(SignUpField.Form, "A code changed while you were signing up. Try again.")]);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(UserAccountConstraints.SignUpRequestIndex))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();

            return await FindRepeatAsync(form, cancellationToken)
                ?? new SignUpOutcome.Refused([new(SignUpField.Form, "This form was already sent. Reload the page and try again.")]);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(UserAccountConstraints.EmailIndex))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();

            return new SignUpOutcome.Refused([EmailTaken]);
        }
    }

    private async Task<List<IdentityError>> ValidatePasswordAsync(UserAccount account, string password)
    {
        var errors = new List<IdentityError>();

        foreach (var validator in userManager.PasswordValidators)
        {
            errors.AddRange((await validator.ValidateAsync(userManager, account, password)).Errors);
        }

        return errors;
    }

    /// <summary>
    /// The outcome of the sign-up this form already made, or null where it made none. The password must
    /// match, so a request id alone never says anything about an account.
    /// </summary>
    private async Task<SignUpOutcome?> FindRepeatAsync(SignUpForm form, CancellationToken cancellationToken)
    {
        var existing = await dbContext.UserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SignUpRequestId == form.RequestId, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        if (existing.NormalizedEmail == userManager.NormalizeEmail(form.Email.Trim())
            && await userManager.CheckPasswordAsync(existing, form.Password))
        {
            return new SignUpOutcome.Succeeded(existing.Id, existing.Email ?? form.Email.Trim(), IsRepeat: true);
        }

        return new SignUpOutcome.Refused([new(SignUpField.Form, "This form was already sent. Reload the page and try again.")]);
    }

    private async Task<TrustCode?> FindTrustCodeAsync(string? input, List<SignUpError> errors, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var lookup = await inviteCodes.FindTrustCodeAsync(input, cancellationToken);
        if (!lookup.IsFound)
        {
            errors.Add(new(SignUpField.TrustCode, lookup.Problem.Detail ?? "No such trust code."));
            return null;
        }

        if (lookup.Value.GetStatus(timeService.Now()) is var status and not TrustCodeStatus.Active)
        {
            errors.Add(new(SignUpField.TrustCode, Problems.TrustCodeNotUsable(status).Detail!));
            return null;
        }

        return lookup.Value;
    }

    private async Task<IReadOnlyList<RepoInviteRedemption>> FindInvitesAsync(
        IReadOnlyList<string?> inputs,
        List<SignUpError> errors,
        CancellationToken cancellationToken)
    {
        var found = new List<RepoInviteRedemption>();

        for (var index = 0; index < inputs.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(inputs[index]))
            {
                continue;
            }

            var lookup = await inviteCodes.FindInviteAsync(inputs[index], cancellationToken);
            if (!lookup.IsFound)
            {
                errors.Add(new(SignUpField.InviteCode, lookup.Problem.Detail ?? "No such invite code.", index));
                continue;
            }

            var (repo, invite) = lookup.Value;

            if (found.Any(x => x.Repo.Id == repo.Id))
            {
                errors.Add(new(SignUpField.InviteCode, "Another invite code above is for the same repo.", index));
                continue;
            }

            if (repo.GetInviteStatus(invite, timeService.Now()) is var status and not InviteStatus.Active)
            {
                errors.Add(new(SignUpField.InviteCode, Problems.InviteNotUsable(status).Detail!, index));
                continue;
            }

            found.Add(lookup.Value);
        }

        return found;
    }


    private static SignUpError EmailTaken
        => new(SignUpField.Email, "An account with this email already exists.");

    private static SignUpError Describe(IdentityError error)
    {
        return error.Code switch
        {
            nameof(IdentityErrorDescriber.DuplicateEmail) => EmailTaken,
            nameof(IdentityErrorDescriber.InvalidEmail) => new(SignUpField.Email, "That is not an email address."),
            _ when error.Code.StartsWith("Password", StringComparison.Ordinal) => new(SignUpField.Password, error.Description),
            _ => new(SignUpField.Form, error.Description)
        };
    }
}
