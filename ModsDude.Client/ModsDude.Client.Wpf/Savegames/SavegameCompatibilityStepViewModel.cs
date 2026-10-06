using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Wpf.Shell.Modals;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// Asked before a savegame goes onto a mod list that has moved far from the one it was last played
/// on: keep it on that revision, or take it to the latest.
/// </summary>
public sealed class SavegameCompatibilityStepViewModel : WizardStepViewModel
{
    public SavegameCompatibilityStepViewModel(
        string savegameName,
        string profileName,
        int playedRevision,
        int latestRevision,
        SavegameCompatibilityVerdict verdict)
    {
        Intro = $"The mod list has changed since '{savegameName}' was last played.";
        PlayedText = $"{profileName} rev {playedRevision}";
        LatestText = $"{profileName} rev {latestRevision}";
        AddedCount = verdict.AddedCount;
        RemovedCount = verdict.RemovedCount;
        ChangedCount = verdict.VersionChangedCount;
        LockedChanges = [.. verdict.LockedChanges.Select(Describe)];

        Choices =
        [
            new WizardChoice("Compatibility mode", () => Result = SavegameRevisionMode.Compatibility) { IsDefault = true },
            new WizardChoice("Latest", () => Result = SavegameRevisionMode.Latest) { IsAccent = false }
        ];
    }


    public override string Title => "Use compatibility mode?";

    public string Intro { get; }
    public string PlayedText { get; }
    public string LatestText { get; }
    public int AddedCount { get; }
    public int RemovedCount { get; }
    public int ChangedCount { get; }

    /// <summary>The locked mods that moved or went, each with what happened to it.</summary>
    public IReadOnlyList<string> LockedChanges { get; }

    public bool HasLockedChanges => LockedChanges.Count > 0;

    /// <summary>The choice made. Read only once the step has been answered.</summary>
    public SavegameRevisionMode Result { get; private set; }


    private static string Describe(ProfileModChange change)
        => change.Kind is ProfileModChangeKind.Removed
            ? $"{change.DisplayName} · removed"
            : $"{change.DisplayName} · {change.FromVersionId?.Value} → {change.ToVersionId?.Value}";
}
