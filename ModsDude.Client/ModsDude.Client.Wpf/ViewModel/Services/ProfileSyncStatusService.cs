using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// Where a profile stands against the game that follows it.
/// </summary>
public enum ProfileSyncState
{
    /// <summary>This profile is not the one the game follows, so there is nothing to say about it.</summary>
    None,

    /// <summary>The game follows it and its folders match what was applied.</summary>
    InSync,

    /// <summary>The game follows it and something in the folders, or a held savegame, has moved.</summary>
    Drifted,

    /// <summary>
    /// The game is meant to follow it and nothing has been applied to match yet - a profile just
    /// activated, a folder just repointed, a folder never synced.
    /// </summary>
    NotApplied,

    /// <summary>An apply to the game's folders is running right now.</summary>
    Applying
}


/// <summary>
/// The answer to "is the profile this game follows actually on disk", asked of a repo and a profile
/// rather than of a monitor, so a sidebar row, a repo tile and the header can all say the same thing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reads the drift monitor rather than the notice.</b> The notice can be waved away and this is a
/// standing fact about the folders, so a dismissed notice must not turn a drifted profile green.
/// </para>
/// <para>
/// <b>One event for four sources</b>: the monitor's own answer, the lease table (an apply started or
/// finished), and the game list (a game added or removed, or pointed at a different profile - which
/// the monitor's answer does not always change, since the notice reads the same either way). It is
/// raised on the UI thread, because two of those are raised by whichever thread finished the work.
/// </para>
/// </remarks>
public sealed class ProfileSyncStatusService
{
    private readonly DriftMonitor _driftMonitor;
    private readonly ProfileApplyService _applyService;


    public ProfileSyncStatusService(
        DriftMonitor driftMonitor,
        IResourceLeases leases,
        ProfileApplyService applyService,
        GameRepository games)
    {
        _driftMonitor = driftMonitor;
        _applyService = applyService;

        _driftMonitor.Changed += OnSourceChanged;
        leases.Changed += OnSourceChanged;
        games.GameChanged += OnSourceChanged;
        games.Games.CollectionChanged += OnSourceChanged;
    }


    /// <summary>Raised on the UI thread after anything that can change an answer from here.</summary>
    public event EventHandler? Changed;


    /// <summary>
    /// Which profile of this repo the game follows, or null where it follows another repo's or none -
    /// or where no game is connected, which is the same answer.
    /// </summary>
    public Guid? ActiveProfileOf(Repo repo)
    {
        return repo.Games.FirstOrDefault()?.ActiveProfile is ActiveProfile active && active.RepoId == repo.Id
            ? active.ProfileId
            : null;
    }

    /// <summary>The state of whichever profile of this repo the game follows.</summary>
    public ProfileSyncState StateOf(Repo repo)
    {
        return ActiveProfileOf(repo) is Guid profileId
            ? StateOf(repo, profileId)
            : ProfileSyncState.None;
    }

    public ProfileSyncState StateOf(Repo repo, Guid profileId)
    {
        if (ActiveProfileOf(repo) != profileId || repo.Games.FirstOrDefault() is not Game game)
        {
            return ProfileSyncState.None;
        }

        if (_applyService.IsBusy(repo, game))
        {
            return ProfileSyncState.Applying;
        }

        var drift = _driftMonitor.Drifted
            .Where(x => x.Game.Identity == game.Identity)
            .ToList();

        if (drift.Count == 0)
        {
            return ProfileSyncState.InSync;
        }

        // Moved is one thing and never-arrived is another, and the two want different words: a folder
        // that was applied and has changed under the user is drifted, while one nothing was applied to
        // has not drifted from anything.
        return drift.Any(x => x.Report.Status is DriftStatus.Drifted || x.Report.HasSavegameDrift)
            ? ProfileSyncState.Drifted
            : ProfileSyncState.NotApplied;
    }

    /// <summary>What the state is called wherever it is written out.</summary>
    public static string Describe(ProfileSyncState state) => state switch
    {
        ProfileSyncState.InSync => "In sync",
        ProfileSyncState.Drifted => "Drifted",
        ProfileSyncState.NotApplied => "Not applied",
        ProfileSyncState.Applying => "Applying…",
        _ => ""
    };


    private void OnSourceChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
