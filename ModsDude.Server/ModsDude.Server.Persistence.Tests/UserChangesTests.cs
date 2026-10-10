using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Changes;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The counter a client polls to hear that its own record changed, which the database keeps itself:
/// see <see cref="UserChanges"/>.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class UserChangesTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_new_user_starts_at_zero()
    {
        var userId = await GivenAUser();

        Assert.Equal(0, await Read(userId));
    }

    [Fact]
    public async Task Granting_and_revoking_trust_each_move_the_counter()
    {
        var userId = await GivenAUser();

        await Change(userId, user => user.GrantTrust());
        await Change(userId, user => user.RevokeTrust());

        Assert.Equal(2, await Read(userId));
    }

    [Fact]
    public async Task Renaming_and_changing_the_picture_each_move_the_counter()
    {
        var userId = await GivenAUser();

        await Change(userId, user => user.Rename(new DisplayName($"renamed-{Guid.NewGuid()}"), DateTime.UtcNow));
        await Change(userId, user => user.SetAvatar(new string('a', 64), DateTime.UtcNow));

        Assert.Equal(2, await Read(userId));
    }

    [Fact]
    public async Task Being_seen_and_being_blocked_do_not_move_the_counter()
    {
        var userId = await GivenAUser();

        await Change(userId, user => user.LastSeen = DateTime.UtcNow.AddMinutes(1));
        await Change(userId, user => user.Block(DateTime.UtcNow));

        Assert.Equal(0, await Read(userId));
    }

    [Fact]
    public async Task Granting_trust_to_a_user_who_already_has_it_does_not_move_the_counter()
    {
        var userId = await GivenAUser();
        await Change(userId, user => user.GrantTrust());

        await Change(userId, user => user.GrantTrust());

        Assert.Equal(1, await Read(userId));
    }

    [Fact]
    public async Task Concurrent_changes_are_each_counted()
    {
        var userId = await GivenAUser();

        await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            Change(userId, user => user.Rename(new DisplayName($"name-{i}"), DateTime.UtcNow))));

        Assert.Equal(5, await Read(userId));
    }


    private async Task<UserId> GivenAUser()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return userId;
    }

    private async Task Change(UserId userId, Action<User> change)
    {
        using var dbContext = fixture.CreateDbContext();

        var user = await dbContext.Users.FindAsync([userId], CancellationToken.None);
        change(user!);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<long> Read(UserId userId)
    {
        using var dbContext = fixture.CreateDbContext();

        return await UserChanges.ReadForAsync(dbContext, userId, CancellationToken.None);
    }
}
