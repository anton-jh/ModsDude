using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Profiles;

namespace ModsDude.Client.Wpf.Home;

/// <summary>One game connected on this machine, as a tile on Home: what it is on and how that stands.</summary>
/// <param name="ProfileName">The profile it follows, or null where it follows none.</param>
/// <param name="RepoName">The repo of that profile, or null where it follows none or the repo is not listed.</param>
/// <param name="HeldSavegames">How many savegames this game has checked out.</param>
public sealed record HomeGameViewModel(
    Game Game,
    string? ProfileName,
    string? RepoName,
    bool IsRunning,
    ProfileSyncState SyncState,
    int HeldSavegames)
{
    public string Name => Game.Name;

    public bool HasProfile => Game.ActiveProfile is not null;

    public string ProfileText => ProfileName ?? (HasProfile ? "Archived profile" : "No profile");

    public bool HasRepoName => RepoName is not null;

    public bool HasSyncState => SyncState is not ProfileSyncState.None;

    public string SyncLabel => ProfileSyncStatusService.Describe(SyncState);

    public bool IsHolding => HeldSavegames > 0;

    public string HoldingText => HeldSavegames == 1 ? "Holding 1 savegame" : $"Holding {HeldSavegames} savegames";
}
