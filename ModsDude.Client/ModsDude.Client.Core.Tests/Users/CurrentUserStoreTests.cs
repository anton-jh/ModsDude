using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Tests.Stores;
using ModsDude.Client.Core.Users;

namespace ModsDude.Client.Core.Tests.Users;

public class CurrentUserStoreTests
{
    [Fact]
    public async Task The_user_is_read_once_and_then_held()
    {
        var server = new FakeUsersClient();
        var store = Create(server);

        await store.GetAsync(CancellationToken.None);
        var user = await store.GetAsync(CancellationToken.None);

        Assert.Equal(1, server.Reads);
        Assert.Equal("Me", user.DisplayName);
        Assert.Same(user, store.User);
    }

    [Fact]
    public async Task A_change_holds_the_user_the_server_answered_with()
    {
        var server = new FakeUsersClient();
        var store = Create(server);
        var changes = 0;
        store.Changed += (_, _) => changes++;

        await store.WriteAsync(ct => server.SetDisplayNameV1Async(new SetDisplayNameRequest { DisplayName = "Renamed" }, ct), CancellationToken.None);

        Assert.Equal("Renamed", store.User?.DisplayName);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task A_user_change_forgets_the_user_and_drops_a_read_still_out()
    {
        var server = new FakeUsersClient();
        var store = Create(server);
        await store.GetAsync(CancellationToken.None);
        var gate = server.Hold();

        var read = store.RefreshAsync(CancellationToken.None);
        store.ClearUserState();
        gate.SetResult(new CurrentUserDto { Id = "previous", DisplayName = "Previous" });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Null(store.User);
    }


    private static CurrentUserStore Create(FakeUsersClient server)
        => new(server, InlineStoreDispatcher.Instance, NullLogger<CurrentUserStore>.Instance);


    private sealed class FakeUsersClient : IUsersClient
    {
        private TaskCompletionSource<CurrentUserDto>? _held;

        public int Reads { get; private set; }

        public TaskCompletionSource<CurrentUserDto> Hold()
            => _held = new TaskCompletionSource<CurrentUserDto>(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ICollection<UserDto>> GetUsersV1Async(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CurrentUserDto> GetCurrentUserV1Async(CancellationToken cancellationToken = default)
        {
            Reads++;

            return _held is { } held
                ? held.Task.WaitAsync(cancellationToken)
                : Task.FromResult(new CurrentUserDto { Id = "me", DisplayName = "Me" });
        }

        public Task<CurrentUserDto> SetDisplayNameV1Async(SetDisplayNameRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new CurrentUserDto { Id = "me", DisplayName = request.DisplayName });

        public Task<CurrentUserDto> SetAvatarV1Async(SetAvatarRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CurrentUserDto> RemoveAvatarV1Async(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
