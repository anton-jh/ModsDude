using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Wpf.Shell.Modals;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// Asked before a savegame goes onto a mod list that has moved far from the one it was last played
/// on: keep it on that revision, or take it to the latest.
/// </summary>
public partial class SavegameCompatibilityModalViewModel : ModalViewModel
{
    public SavegameCompatibilityModalViewModel(
        string savegameName,
        string profileName,
        int playedRevision,
        int latestRevision,
        SavegameCompatibilityVerdict verdict)
    {
        Title = "Use compatibility mode?";
        Intro = $"The mod list has changed since '{savegameName}' was last played.";
        PlayedText = $"{profileName} rev {playedRevision}";
        LatestText = $"{profileName} rev {latestRevision}";
        AddedCount = verdict.AddedCount;
        RemovedCount = verdict.RemovedCount;
        ChangedCount = verdict.VersionChangedCount;
        LockedChanges = [.. verdict.LockedChanges.Select(Describe)];
    }


    public string Title { get; }
    public string Intro { get; }
    public string PlayedText { get; }
    public string LatestText { get; }
    public int AddedCount { get; }
    public int RemovedCount { get; }
    public int ChangedCount { get; }

    /// <summary>The locked mods that moved or went, each with what happened to it.</summary>
    public IReadOnlyList<string> LockedChanges { get; }

    public bool HasLockedChanges => LockedChanges.Count > 0;

    /// <summary>The choice made, or null where the user backed out.</summary>
    public SavegameRevisionMode? Result { get; private set; }


    [RelayCommand]
    private void UseCompatibilityMode() => Finish(SavegameRevisionMode.Compatibility);

    [RelayCommand]
    private void UseLatest() => Finish(SavegameRevisionMode.Latest);

    [RelayCommand]
    private void Cancel() => Finish(null);


    public override bool TryCancel() => Press(CancelCommand);

    public override bool TryAccept() => Press(UseCompatibilityModeCommand);


    private void Finish(SavegameRevisionMode? result)
    {
        Result = result;
        Done = true;
    }

    private static string Describe(ProfileModChange change)
        => change.Kind is ProfileModChangeKind.Removed
            ? $"{change.DisplayName} · removed"
            : $"{change.DisplayName} · {change.FromVersionId?.Value} → {change.ToVersionId?.Value}";
}
