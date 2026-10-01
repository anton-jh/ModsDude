using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Activity;

/// <summary>
/// The newest friend activity each account has already been told about - see
/// <see cref="LocalState.FriendActivitySeenUntil"/>.
/// </summary>
public interface IFriendActivitySeen
{
    DateTime? Get(string userId);

    void Set(string userId, DateTime until);
}


/// <summary><see cref="IFriendActivitySeen"/> over the real <c>state.json</c>.</summary>
public sealed class StateStoreFriendActivitySeen(IStateStore store) : IFriendActivitySeen
{
    public DateTime? Get(string userId)
        => store.Read(state => state.FriendActivitySeenUntil.TryGetValue(userId, out var until) ? until : (DateTime?)null);

    public void Set(string userId, DateTime until)
        => store.Update(state => state.FriendActivitySeenUntil[userId] = until);
}
