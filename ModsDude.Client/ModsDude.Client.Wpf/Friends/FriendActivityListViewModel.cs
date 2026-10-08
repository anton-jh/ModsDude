using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Wpf.Account;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.Collections.ObjectModel;
using System.Windows;

namespace ModsDude.Client.Wpf.Friends;

/// <summary>
/// A list of who else is on which profile: one repo's on its overview, every repo's on Home.
/// </summary>
/// <remarks>
/// <para>
/// <b>Drawn from <see cref="FriendActivityService"/>, never fetched here.</b> Opening a page asks it
/// to read again, and every page showing it redraws when any of them - or the watcher - does. So two
/// pages opened a minute apart cannot disagree about what Alex is on.
/// </para>
/// <para>
/// Redrawn too when a game here changes, because whether the button is offered depends on what this
/// machine is on: following somebody turns their row's button into "You are on this too". And on the
/// service's own tick, because who is playing and how long ago things happened move with the clock.
/// </para>
/// </remarks>
public sealed partial class FriendActivityListViewModel : ObservableObject, IDisposable
{
    private readonly IFriendActivityService _friends;
    private readonly IFriendActivityEnvironment _environment;
    private readonly IUserAvatarFactory _avatarFactory;
    private readonly IFriendFollowService _follow;
    private readonly IGameRepository _games;
    private readonly IToastService _toasts;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    /// <summary>The one repo listed, or null where every repo is.</summary>
    private readonly Guid? _repoId;

    /// <summary>The rows a follow started from here is still going on, by notice key.</summary>
    private readonly HashSet<string> _following = [];


    private FriendActivityListViewModel(
        IFriendActivityService friends,
        IFriendActivityEnvironment environment,
        IUserAvatarFactory avatarFactory,
        IFriendFollowService follow,
        IGameRepository games,
        IToastService toasts,
        ILogger<FriendActivityListViewModel> logger,
        TimeProvider time,
        Guid? repoId)
    {
        _time = time;
        _friends = friends;
        _environment = environment;
        _avatarFactory = avatarFactory;
        _follow = follow;
        _games = games;
        _toasts = toasts;
        _logger = logger;
        _repoId = repoId;

        _friends.Changed += OnChanged;
        _games.GameChanged += OnChanged;

        Rebuild();
    }


    public ObservableCollection<FriendActivityRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>What an empty list says, once it is known to be empty rather than not yet read.</summary>
    public string? EmptyText => HasRows
        ? null
        : !_friends.HasLoaded
            ? CouldNotRead ? "Could not reach the server to see what your friends are on." : "Looking..."
            : _repoId is null
                ? "None of your friends has activated a profile in the last week."
                : "Nobody else in this repo has activated one of its profiles in the last week.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _couldNotRead;


    /// <summary>Reads the list again. Off the UI thread is fine: every redraw is dispatched.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            await _friends.RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogInformation(exception, "Could not read which profiles friends are on.");

            Post(() => CouldNotRead = true);
        }
    }

    public void Dispose()
    {
        _friends.Changed -= OnChanged;
        _games.GameChanged -= OnChanged;
    }


    [RelayCommand]
    private async Task Follow(FriendActivityRowViewModel row)
    {
        var key = FriendActivityRules.NoticeKey(row.Model);

        _following.Add(key);
        Rebuild();

        try
        {
            var (message, severity) = await _follow.FollowAsync(row.Model, CancellationToken.None);

            _toasts.Show(message, severity);
        }
        finally
        {
            _following.Remove(key);
            Rebuild();
        }
    }


    private void OnChanged(object? sender, EventArgs e) => Post(Rebuild);

    private static void Post(Action action) => Application.Current?.Dispatcher.InvokeAsync(action);

    private void Rebuild()
    {
        var now = _time.GetUtcNow();

        var rows = FriendActivityRules.Order(
            _friends.Rows.Where(x => _repoId is not Guid repoId || x.RepoId == repoId),
            now.UtcDateTime);

        var ambiguous = UserDisplay.FindAmbiguous(rows.Select(x => x.User).DistinctBy(x => x.Id));

        Rows.Clear();

        foreach (var row in rows)
        {
            Rows.Add(new FriendActivityRowViewModel(
                row,
                _environment,
                _avatarFactory.Create(row.User),
                showTag: ambiguous.Contains(row.User.Id),
                showWhere: _repoId is null,
                isFollowing: _following.Contains(FriendActivityRules.NoticeKey(row)),
                now));
        }

        if (_friends.HasLoaded)
        {
            CouldNotRead = false;
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(EmptyText));
    }


    public sealed class Factory(IServiceProvider serviceProvider)
    {
        public FriendActivityListViewModel Create(Guid repoId) => Create((Guid?)repoId);

        public FriendActivityListViewModel CreateForAllRepos() => Create(null);

        private FriendActivityListViewModel Create(Guid? repoId)
            => new(
                serviceProvider.GetRequiredService<IFriendActivityService>(),
                serviceProvider.GetRequiredService<IFriendActivityEnvironment>(),
                serviceProvider.GetRequiredService<IUserAvatarFactory>(),
                serviceProvider.GetRequiredService<IFriendFollowService>(),
                serviceProvider.GetRequiredService<IGameRepository>(),
                serviceProvider.GetRequiredService<IToastService>(),
                serviceProvider.GetRequiredService<ILogger<FriendActivityListViewModel>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                repoId);
    }
}
