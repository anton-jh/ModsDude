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
public sealed class StateStoreFriendActivitySeen(StateStore store) : IFriendActivitySeen
{
    public DateTime? Get(string userId)
        => store.Get().FriendActivitySeenUntil.TryGetValue(userId, out var until) ? until : null;

    public void Set(string userId, DateTime until)
    {
        store.Get().FriendActivitySeenUntil[userId] = until;
        store.Save();
    }
}
