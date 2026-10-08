using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Activity;

/// <summary>A friend's game that is news, and whether the news is that they are playing it.</summary>
/// <param name="IsPlaying">
/// True where they started playing since the news began and still are. Otherwise the news is what
/// they last switched to or checked out.
/// </param>
public sealed record FriendNews(GameActivityDto Activity, bool IsPlaying);
