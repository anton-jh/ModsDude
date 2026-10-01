using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.Shell.Toasts;

namespace ModsDude.Client.Wpf.Friends;

public interface IFriendFollowService
{
    /// <returns>What to tell the user, and how loudly.</returns>
    Task<(string Message, ToastSeverity Severity)> FollowAsync(GameActivityDto activity, CancellationToken cancellationToken);
}
