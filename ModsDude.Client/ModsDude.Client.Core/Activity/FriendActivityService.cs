using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Activity;

/// <summary>
/// Which profile the people this user shares repos with have their games on, as of the last read -
/// and which of that is news.
/// </summary>
/// <remarks>
/// <para>
/// <b>One read for every surface.</b> Home, a repo's overview and the notice column all draw from
/// <see cref="Rows"/>, which is every repo at once; a repo's overview filters it rather than asking
/// the server a question of its own, so two pages open one after the other never disagree.
/// </para>
/// <para>
/// <b>News is measured in the server's time.</b> A row is news where it changed after the newest
/// change this account had already been told about - remembered across runs in
/// <see cref="IFriendActivitySeen"/>, so opening the app says what happened while it
/// was closed. The very first read for an account is the baseline and announces nothing: a week of
/// other people's evenings is history, not news.
/// </para>
/// <para>
/// Two kinds of news, deliberately. <see cref="News"/> is everything that changed this session, for
/// the column to draw as cards until dismissed or superseded; <see cref="Announced"/> fires once per
/// change, for the one listener that must not repeat itself - a Windows toast.
/// </para>
/// </remarks>
public sealed class FriendActivityService(
    IActivityClient activityClient,
    CurrentUserService currentUserService,
    IFriendActivitySeen seen)
    : IUserScopedState
{
    private readonly Lock _lock = new();

    private IReadOnlyList<GameActivityDto> _rows = [];
    private string? _userId;
    private DateTime? _newsSince;
    private DateTime _announcedUntil;
    private Task? _refreshing;


    /// <summary>Raised after every read that landed, on whichever thread it completed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised with the rows that changed since the last time this fired. Once per change, never
    /// again for the same one.
    /// </summary>
    public event EventHandler<IReadOnlyList<GameActivityDto>>? Announced;


    /// <summary>Every friend's game in the last week, most recently active first.</summary>
    public IReadOnlyList<GameActivityDto> Rows
    {
        get
        {
            lock (_lock)
            {
                return _rows;
            }
        }
    }

    /// <summary>Whether anything has been read yet, so a page can tell "nobody" from "not asked".</summary>
    public bool HasLoaded { get; private set; }

    /// <summary>The rows that changed since this session began.</summary>
    public IReadOnlyList<GameActivityDto> News
    {
        get
        {
            lock (_lock)
            {
                return _newsSince is DateTime since
                    ? [.. _rows.Where(x => x.ChangedAt > since)]
                    : [];
            }
        }
    }


    /// <summary>
    /// Reads the list again. Calls that arrive while one is in flight share it.
    /// </summary>
    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_refreshing is { IsCompleted: false } running)
            {
                return running;
            }

            return _refreshing = RefreshCoreAsync(cancellationToken);
        }
    }

    public void ClearUserState()
    {
        lock (_lock)
        {
            _rows = [];
            _userId = null;
            _newsSince = null;
            _announcedUntil = default;
        }

        HasLoaded = false;

        Changed?.Invoke(this, EventArgs.Empty);
    }


    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var rows = (await activityClient.GetGameActivityV1Async(null, cancellationToken))
            .OrderByDescending(x => x.TouchedAt)
            .ToList();

        var userId = _userId ?? (await currentUserService.Get(cancellationToken)).Id;

        List<GameActivityDto> fresh;

        lock (_lock)
        {
            _userId = userId;

            DateTime? newest = rows.Count > 0 ? rows.Max(x => x.ChangedAt) : null;

            if (_newsSince is null)
            {
                _newsSince = seen.Get(userId) ?? newest ?? DateTime.MinValue;

                _announcedUntil = _newsSince.Value;
            }

            var until = _announcedUntil;

            fresh = [.. rows.Where(x => x.ChangedAt > until)];

            if (newest is DateTime latest && latest > _announcedUntil)
            {
                _announcedUntil = latest;
            }

            if (seen.Get(userId) is not DateTime stored || _announcedUntil > stored)
            {
                seen.Set(userId, _announcedUntil);
            }

            _rows = rows;
        }

        HasLoaded = true;

        Changed?.Invoke(this, EventArgs.Empty);

        if (fresh.Count > 0)
        {
            Announced?.Invoke(this, fresh);
        }
    }
}
