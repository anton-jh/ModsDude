using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Users;

public class UserTests
{
    private static readonly DateTime _now = new(2026, 1, 1);


    [Fact]
    public void A_new_user_is_not_blocked()
    {
        var user = CreateUser();

        Assert.False(user.IsBlocked);
        Assert.Null(user.BlockedAt);
    }

    [Fact]
    public void Blocking_records_when()
    {
        var user = CreateUser();

        user.Block(_now);

        Assert.True(user.IsBlocked);
        Assert.Equal(_now, user.BlockedAt);
    }

    [Fact]
    public void Blocking_again_keeps_the_first_time()
    {
        var user = CreateUser();

        user.Block(_now);
        user.Block(_now.AddDays(1));

        Assert.Equal(_now, user.BlockedAt);
    }

    [Fact]
    public void Unblocking_clears_the_block()
    {
        var user = CreateUser();

        user.Block(_now);
        user.Unblock();

        Assert.False(user.IsBlocked);
        Assert.Null(user.BlockedAt);
    }

    [Fact]
    public void Unblocking_a_user_who_is_not_blocked_changes_nothing()
    {
        var user = CreateUser();

        user.Unblock();

        Assert.False(user.IsBlocked);
    }

    [Fact]
    public void Trust_can_be_granted_and_revoked_repeatedly()
    {
        var user = CreateUser();

        user.GrantTrust();
        user.GrantTrust();
        Assert.True(user.IsTrusted);

        user.RevokeTrust();
        user.RevokeTrust();
        Assert.False(user.IsTrusted);
    }

    [Fact]
    public void Blocking_leaves_trust_alone()
    {
        var user = CreateUser();
        user.GrantTrust();

        user.Block(_now);

        Assert.True(user.IsTrusted);
    }


    private static User CreateUser() => new(new UserId("user"), new DisplayName("User"), _now);
}
