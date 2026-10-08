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
/// <b>One read for every surface.</b> Home, a repo's overview and the notice column all draw from
/// <see cref="Rows"/>, which is every repo at once; a repo's overview filters it rather than asking
/// the server a question of its own, so two pages open one after the other never disagree.
/// </para>
/// <para>
/// <b>News is measured in the server's time.</b> A row is news where it changed, or its game started
/// being played, after the newest news this account had already been told about - remembered across
/// runs in <see cref="IFriendActivitySeen"/>, so opening the app says what happened while it was
/// closed. The very first read for an account is the baseline and announces nothing: a week of other
/// people's evenings is history, not news.
/// </para>
/// <para>
/// <b>Playing is measured against this machine's clock</b>, because a game stops being played when
/// its heartbeats stop, and nothing on the server moves then. So <see cref="Changed"/> also fires
/// every <see cref="TickInterval"/>, and everything drawn from the rows is drawn again.
/// </para>
/// <para>
/// Two kinds of news, deliberately. <see cref="News"/> is everything new this session, for the column
/// to draw as cards until dismissed or superseded; <see cref="Announced"/> fires once per change, for
/// the one listener that must not repeat itself - a Windows toast.
/// </para>
/// </remarks>
public sealed class FriendActivityService : IFriendActivityService, IDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly IActivityClient _activityClient;
    private readonly ICurrentUserStore _currentUser;
    private readonly IFriendActivitySeen _seen;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private readonly StoreLoads<WholeList> _loads;
    private readonly ITimer _tick;

    private IReadOnlyList<GameActivityDto> _rows = [];
    private DateTime? _newsSince;
    private DateTime _announcedUntil;


    public FriendActivityService(
        IActivityClient activityClient,
        ICurrentUserStore currentUser,
        IFriendActivitySeen seen,
        IStoreDispatcher dispatcher,
        TimeProvider time,
        ILogger<FriendActivityService> logger)
    {
        _activityClient = activityClient;
        _currentUser = currentUser;
        _seen = seen;
        _time = time;
        _loads = new(dispatcher, logger);
        _tick = time.CreateTimer(_ => OnTick(), null, TickInterval, Timeout.InfiniteTimeSpan);
    }


    public event EventHandler? Changed;

    public event EventHandler<IReadOnlyList<FriendNews>>? Announced;


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

    public IReadOnlyList<FriendNews> News
    {
        get
        {
            var now = Now();

            lock (_lock)
            {
                return _newsSince is DateTime since
                    ? [.. _rows.Select(x => FriendActivityRules.ToNews(x, since, now)).OfType<FriendNews>()]
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

    public void Dispose() => _tick.Dispose();


    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private void OnTick()
    {
        if (HasLoaded)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        _tick.Change(TickInterval, Timeout.InfiniteTimeSpan);
    }

    private async Task<(List<GameActivityDto> Rows, string UserId)> ReadAsync(CancellationToken cancellationToken)
    {
        var rows = (await _activityClient.GetGameActivityV1Async(null, cancellationToken))
            .OrderByDescending(x => x.TouchedAt)
            .ThenBy(x => x.User.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Game, StringComparer.Ordinal)
            .ToList();

        return (rows, (await _currentUser.GetAsync(cancellationToken)).Id);
    }

    private void Apply((List<GameActivityDto> Rows, string UserId) read)
    {
        var (rows, userId) = read;
        var now = Now();
        List<FriendNews> fresh;

        lock (_lock)
        {
            DateTime? newest = rows.Count > 0 ? rows.Max(FriendActivityRules.NewsAt) : null;

            if (_newsSince is null)
            {
                _newsSince = _seen.Get(userId) ?? newest ?? DateTime.MinValue;

                _announcedUntil = _newsSince.Value;
            }

            var until = _announcedUntil;

            fresh = [.. rows.Select(x => FriendActivityRules.ToNews(x, until, now)).OfType<FriendNews>()];

            if (newest is DateTime latest && latest > _announcedUntil)
            {
                _announcedUntil = latest;
            }

            if (_seen.Get(userId) is not DateTime stored || _announcedUntil > stored)
            {
                _seen.Set(userId, _announcedUntil);
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
