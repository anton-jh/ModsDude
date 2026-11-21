using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Invites;

public class TrustCodeTests
{
    private static readonly DateTime _created = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _expiresAt = _created.AddDays(2);


    [Fact]
    public void A_fresh_code_is_active()
    {
        Assert.Equal(TrustCodeStatus.Active, CreateCode().GetStatus(_created));
    }

    [Fact]
    public void A_code_expires_at_the_moment_it_says()
    {
        var code = CreateCode();

        Assert.Equal(TrustCodeStatus.Active, code.GetStatus(_expiresAt.AddSeconds(-1)));
        Assert.Equal(TrustCodeStatus.Expired, code.GetStatus(_expiresAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_code_cannot_expire_before_it_is_created(int hours)
    {
        Assert.Throws<DomainValidationException>(
            () => new TrustCode(InviteCodes.Generate(), new TrustCodeRequestId(Guid.NewGuid()), _created, _created.AddHours(hours)));
    }

    [Fact]
    public void Changing_the_expiry_extends_an_active_code()
    {
        var code = CreateCode();
        var later = _expiresAt.AddDays(5);

        code.ChangeExpiry(later, _created.AddHours(1));

        Assert.Equal(later, code.ExpiresAt);
        Assert.Equal(TrustCodeStatus.Active, code.GetStatus(_expiresAt));
    }

    [Fact]
    public void Changing_the_expiry_can_shorten_a_code()
    {
        var code = CreateCode();
        var sooner = _created.AddHours(2);

        code.ChangeExpiry(sooner, _created.AddHours(1));

        Assert.Equal(TrustCodeStatus.Expired, code.GetStatus(sooner));
    }

    [Fact]
    public void Changing_the_expiry_brings_an_expired_code_back()
    {
        var code = CreateCode();
        var now = _expiresAt.AddDays(1);

        code.ChangeExpiry(now.AddDays(1), now);

        Assert.Equal(TrustCodeStatus.Active, code.GetStatus(now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void The_expiry_cannot_be_changed_to_the_past(int hours)
    {
        var code = CreateCode();
        var now = _created.AddHours(1);

        Assert.Throws<DomainValidationException>(() => code.ChangeExpiry(now.AddHours(hours), now));
        Assert.Equal(_expiresAt, code.ExpiresAt);
    }

    [Fact]
    public void The_expiry_of_a_redeemed_code_cannot_be_changed()
    {
        var code = CreateCode();
        code.Redeem(CreateUser("redeemer"), _created);

        Assert.Throws<DomainValidationException>(() => code.ChangeExpiry(_expiresAt.AddDays(1), _created));
        Assert.Equal(_expiresAt, code.ExpiresAt);
    }

    [Fact]
    public void The_expiry_of_a_revoked_code_cannot_be_changed()
    {
        var code = CreateCode();
        code.Revoke(_created);

        Assert.Throws<DomainValidationException>(() => code.ChangeExpiry(_expiresAt.AddDays(1), _created));
        Assert.Equal(TrustCodeStatus.Revoked, code.GetStatus(_created));
    }

    [Fact]
    public void Redeeming_trusts_the_user_and_records_who_and_when()
    {
        var code = CreateCode();
        var user = CreateUser("redeemer");
        var now = _created.AddHours(1);

        code.Redeem(user, now);

        Assert.True(user.IsTrusted);
        Assert.Equal(user.Id, code.RedeemedBy);
        Assert.Equal(now, code.RedeemedAt);
        Assert.Equal(TrustCodeStatus.Redeemed, code.GetStatus(now));
    }

    [Fact]
    public void A_redeemed_code_stays_redeemed_after_it_would_have_expired()
    {
        var code = CreateCode();

        code.Redeem(CreateUser("redeemer"), _created);

        Assert.Equal(TrustCodeStatus.Redeemed, code.GetStatus(_created.AddDays(10)));
    }

    [Fact]
    public void A_code_cannot_be_redeemed_twice()
    {
        var code = CreateCode();
        code.Redeem(CreateUser("first"), _created);

        var second = CreateUser("second");

        Assert.Throws<DomainValidationException>(() => code.Redeem(second, _created));
        Assert.False(second.IsTrusted);
        Assert.Equal(new UserId("first"), code.RedeemedBy);
    }

    [Fact]
    public void An_expired_code_cannot_be_redeemed()
    {
        var code = CreateCode();
        var user = CreateUser("late");

        Assert.Throws<DomainValidationException>(() => code.Redeem(user, code.ExpiresAt));
        Assert.False(user.IsTrusted);
        Assert.Null(code.RedeemedBy);
    }

    [Fact]
    public void A_revoked_code_cannot_be_redeemed()
    {
        var code = CreateCode();
        code.Revoke(_created);
        var user = CreateUser("redeemer");

        Assert.Throws<DomainValidationException>(() => code.Redeem(user, _created));
        Assert.False(user.IsTrusted);
    }

    [Fact]
    public void An_already_trusted_user_does_not_use_up_a_code()
    {
        var user = CreateUser("redeemer");
        CreateCode().Redeem(user, _created);

        var code = CreateCode();

        Assert.Throws<DomainValidationException>(() => code.Redeem(user, _created));
        Assert.Equal(TrustCodeStatus.Active, code.GetStatus(_created));
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_time()
    {
        var code = CreateCode();

        code.Revoke(_created);
        code.Revoke(_created.AddHours(1));

        Assert.Equal(_created, code.RevokedAt);
        Assert.Equal(TrustCodeStatus.Revoked, code.GetStatus(_created.AddHours(1)));
    }

    [Fact]
    public void Revoking_reports_ahead_of_expiry()
    {
        var code = CreateCode();

        code.Revoke(_created);

        Assert.Equal(TrustCodeStatus.Revoked, code.GetStatus(code.ExpiresAt.AddDays(1)));
    }

    [Fact]
    public void A_redeemed_code_cannot_be_revoked()
    {
        var code = CreateCode();
        code.Redeem(CreateUser("redeemer"), _created);

        Assert.Throws<DomainValidationException>(() => code.Revoke(_created));
        Assert.Null(code.RevokedAt);
    }


    private static TrustCode CreateCode()
    {
        return new TrustCode(InviteCodes.Generate(), new TrustCodeRequestId(Guid.NewGuid()), _created, _expiresAt);
    }

    private static User CreateUser(string id)
    {
        return new User(new UserId(id), new DisplayName(id), _created);
    }
}
