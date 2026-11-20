using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Accounts;
using ModsDude.Server.Persistence.EntityTypeConfigurations;
using ModsDude.Server.Persistence.Extensions;
using Npgsql;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// What the database holds sign-up to, whatever the code checked first: one account per user and per
/// address, and one sign-up per request.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class UserAccountConstraintTests(DatabaseFixture fixture)
{
    private static readonly DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);


    [Fact]
    public async Task An_account_without_its_user_is_refused_at_commit()
    {
        using var dbContext = fixture.CreateDbContext();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        dbContext.UserAccounts.Add(Account(Guid.NewGuid().ToString(), UniqueEmail()));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        Assert.Equal(UserAccountConstraints.UserForeignKey, exception.ConstraintName);
    }

    [Fact]
    public async Task An_account_saved_before_its_user_in_one_transaction_is_accepted()
    {
        var id = Guid.NewGuid().ToString();

        using (var dbContext = fixture.CreateDbContext())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            dbContext.UserAccounts.Add(Account(id, UniqueEmail()));
            await dbContext.SaveChangesAsync();

            dbContext.Users.Add(User(id));
            await dbContext.SaveChangesAsync();

            await transaction.CommitAsync();
        }

        using var verification = fixture.CreateDbContext();
        Assert.True(await verification.UserAccounts.AnyAsync(x => x.Id == id));
    }

    [Fact]
    public async Task Two_accounts_with_one_address_are_refused()
    {
        var email = UniqueEmail();
        await GivenAnAccount(email);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => GivenAnAccount(email.ToUpperInvariant()));
        Assert.True(exception.IsUniqueViolation(UserAccountConstraints.EmailIndex));
    }

    [Fact]
    public async Task Two_accounts_from_one_sign_up_request_are_refused()
    {
        var requestId = Guid.NewGuid();
        await GivenAnAccount(UniqueEmail(), requestId);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => GivenAnAccount(UniqueEmail(), requestId));
        Assert.True(exception.IsUniqueViolation(UserAccountConstraints.SignUpRequestIndex));
    }

    [Fact]
    public async Task Deleting_a_user_deletes_their_account()
    {
        var id = await GivenAnAccount(UniqueEmail());

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.Users.Where(x => x.Id == new UserId(id)).ExecuteDeleteAsync();
        }

        using var verification = fixture.CreateDbContext();
        Assert.False(await verification.UserAccounts.AnyAsync(x => x.Id == id));
    }


    private async Task<string> GivenAnAccount(string email, Guid? requestId = null)
    {
        var id = Guid.NewGuid().ToString();

        using var dbContext = fixture.CreateDbContext();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        dbContext.Users.Add(User(id));
        dbContext.UserAccounts.Add(Account(id, email, requestId));
        await dbContext.SaveChangesAsync();

        await transaction.CommitAsync();

        return id;
    }

    private static User User(string id) => new(new UserId(id), new DisplayName("Someone"), _now);

    /// <summary>Normalized the way Identity's default normalizer does it.</summary>
    private static UserAccount Account(string id, string email, Guid? requestId = null) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        SignUpRequestId = requestId ?? Guid.NewGuid(),
        SecurityStamp = Guid.NewGuid().ToString(),
        ConcurrencyStamp = Guid.NewGuid().ToString()
    };

    private static string UniqueEmail() => $"{Guid.NewGuid():N}@example.com";
}
