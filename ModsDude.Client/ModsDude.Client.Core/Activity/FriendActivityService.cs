using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Stores;
using ModsDude.Client.Core.Users;

namespace ModsDude.Client.Core.Activity;

/// <summary>
/// Which profile the people this user shares repos with have their games on, as of the last read -
/// and which of that is news.
/// </summary>
/// <remarks>
/// <para>
/// <b>One read for every surface.</b> A repo's overview and the notice column both draw from
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
    ICurrentUserStore currentUser,
    IFriendActivitySeen seen,
    IStoreDispatcher dispatcher,
    ILogger<FriendActivityService> logger)
    : IFriendActivityService
{
    private readonly Lock _lock = new();
    private readonly StoreLoads<WholeList> _loads = new(dispatcher, logger);

    private IReadOnlyList<GameActivityDto> _rows = [];
    private DateTime? _newsSince;
    private DateTime _announcedUntil;


    public event EventHandler? Changed;

    public event EventHandler<IReadOnlyList<GameActivityDto>>? Announced;


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

    public bool HasLoaded { get; private set; }

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


    public Task RefreshAsync(CancellationToken cancellationToken)
        => _loads.ReadAsync(default, ReadAsync, Apply, cancellationToken);

    public void ClearUserState()
    {
        _loads.Reset();

        lock (_lock)
        {
            _rows = [];
            _newsSince = null;
            _announcedUntil = default;
        }

        HasLoaded = false;

        Changed?.Invoke(this, EventArgs.Empty);
    }


    private async Task<(List<GameActivityDto> Rows, string UserId)> ReadAsync(CancellationToken cancellationToken)
    {
        var rows = (await activityClient.GetGameActivityV1Async(null, cancellationToken))
            .OrderByDescending(x => x.TouchedAt)
            .ThenBy(x => x.User.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Game, StringComparer.Ordinal)
            .ToList();

        return (rows, (await currentUser.GetAsync(cancellationToken)).Id);
    }

    private void Apply((List<GameActivityDto> Rows, string UserId) read)
    {
        var (rows, userId) = read;
        List<GameActivityDto> fresh;

        lock (_lock)
        {
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
