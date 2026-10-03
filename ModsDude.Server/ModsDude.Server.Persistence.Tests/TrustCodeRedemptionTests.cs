using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// Concurrent redemptions, which the domain cannot see: each request checks a code it read before
/// the other one wrote.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class TrustCodeRedemptionTests(DatabaseFixture fixture)
{
    private static readonly DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);


    [Fact]
    public async Task Two_users_redeeming_one_code_at_once_trusts_only_the_first()
    {
        var first = await GivenAUser();
        var second = await GivenAUser();
        var code = await GivenACode();

        using var firstContext = fixture.CreateDbContext();
        using var secondContext = fixture.CreateDbContext();

        var firstCode = (await firstContext.TrustCodes.GetAsync(code, CancellationToken.None))!;
        var secondCode = (await secondContext.TrustCodes.GetAsync(code, CancellationToken.None))!;

        firstCode.Redeem((await firstContext.Users.GetAsync(first, CancellationToken.None))!, _now);
        secondCode.Redeem((await secondContext.Users.GetAsync(second, CancellationToken.None))!, _now);

        await firstContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());

        using var verification = fixture.CreateDbContext();

        Assert.True((await verification.Users.GetAsync(first, CancellationToken.None))!.IsTrusted);
        Assert.False((await verification.Users.GetAsync(second, CancellationToken.None))!.IsTrusted);
        Assert.Equal(first, (await verification.TrustCodes.GetAsync(code, CancellationToken.None))!.RedeemedBy);
    }

    [Fact]
    public async Task One_user_redeeming_two_codes_at_once_uses_up_only_the_first()
    {
        var user = await GivenAUser();
        var firstCode = await GivenACode();
        var secondCode = await GivenACode();

        using var firstContext = fixture.CreateDbContext();
        using var secondContext = fixture.CreateDbContext();

        (await firstContext.TrustCodes.GetAsync(firstCode, CancellationToken.None))!
            .Redeem((await firstContext.Users.GetAsync(user, CancellationToken.None))!, _now);
        (await secondContext.TrustCodes.GetAsync(secondCode, CancellationToken.None))!
            .Redeem((await secondContext.Users.GetAsync(user, CancellationToken.None))!, _now);

        await firstContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());

        using var verification = fixture.CreateDbContext();

        Assert.Equal(
            TrustCodeStatus.Active,
            (await verification.TrustCodes.GetAsync(secondCode, CancellationToken.None))!.GetStatus(_now));
    }

    [Fact]
    public async Task Revoking_a_code_that_was_redeemed_meanwhile_is_refused()
    {
        var user = await GivenAUser();
        var code = await GivenACode();

        using var redeemContext = fixture.CreateDbContext();
        using var revokeContext = fixture.CreateDbContext();

        var toRedeem = (await redeemContext.TrustCodes.GetAsync(code, CancellationToken.None))!;
        var toRevoke = (await revokeContext.TrustCodes.GetAsync(code, CancellationToken.None))!;

        toRedeem.Redeem((await redeemContext.Users.GetAsync(user, CancellationToken.None))!, _now);
        toRevoke.Revoke(_now);

        await redeemContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => revokeContext.SaveChangesAsync());
    }


    private async Task<UserId> GivenAUser()
    {
        var userId = new UserId($"trust-{Guid.NewGuid()}");

        using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(new User(userId, new DisplayName("Trusty"), _now));
        await dbContext.SaveChangesAsync();

        return userId;
    }

    private async Task<TrustCodeId> GivenACode()
    {
        var code = new TrustCode(InviteCodes.Generate(), new TrustCodeRequestId(Guid.NewGuid()), _now);

        using var dbContext = fixture.CreateDbContext();
        dbContext.TrustCodes.Add(code);
        await dbContext.SaveChangesAsync();

        return code.Id;
    }
}
