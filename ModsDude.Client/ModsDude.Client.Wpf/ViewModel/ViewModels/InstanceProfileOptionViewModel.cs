using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One profile a game could follow, named with its repo where there is more than one.
/// </summary>
/// <remarks>
/// The candidates span every repo sharing the game's scope, not just the repo the user navigated
/// in through. A game is offered by all of them and holds one active profile that may have come
/// from any: a list limited to one repo could not display the game's own current state, and would
/// show a blank for a profile that is plainly active.
/// </remarks>
public sealed class InstanceProfileOptionViewModel(Guid repoId, string repoName, Guid profileId, string profileName, bool qualify)
{
    public ActiveProfile Value { get; } = new(repoId, profileId);

    public string ProfileName { get; } = profileName;

    public string RepoName { get; } = repoName;

    public string Label { get; } = qualify ? $"{profileName}  ({repoName})" : profileName;
}
