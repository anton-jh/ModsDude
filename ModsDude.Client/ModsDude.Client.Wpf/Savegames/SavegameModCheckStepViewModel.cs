using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Wpf.Shell.Modals;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// How the mods a savegame lists differ from the profile revision it is about to be published on,
/// asked only where they differ. Information, apart from a locked mod, which is a warning.
/// </summary>
public sealed class SavegameModCheckStepViewModel : WizardStepViewModel
{
    private const int NamesInTooltip = 20;

    private readonly Func<ModKey, string> _nameOf;


    /// <param name="nameOf">What to call a mod: its name where the repo has it, its id otherwise.</param>
    /// <param name="canReview">Whether the user may edit the profile, and so be offered its editor.</param>
    public SavegameModCheckStepViewModel(
        string profileName,
        int revision,
        SavegameModComparison comparison,
        Func<ModKey, string> nameOf,
        bool canReview)
    {
        _nameOf = nameOf;

        Message = $"Compared with {profileName} revision {revision}.";

        NotInProfile = Row(comparison.NotInProfile);
        NotInSavegame = Row(comparison.NotInSavegame);
        OtherVersion = Row(comparison.OtherVersion);

        LockedMods = [.. comparison.Locked.Select(x => nameOf(x.ModId))];

        Choices =
        [
            new WizardChoice("Publish anyway", () => ReviewChosen = false) { IsDefault = true },
            new WizardChoice("Review in the editor", () => ReviewChosen = true) { IsAccent = false, VisibleWhen = () => canReview }
        ];
    }


    public override string Title => "Mods differ";

    public string Message { get; }

    public SavegameModCheckRow NotInProfile { get; }
    public SavegameModCheckRow NotInSavegame { get; }
    public SavegameModCheckRow OtherVersion { get; }

    /// <summary>Named rather than counted: a locked mod differing is what can break a save.</summary>
    public IReadOnlyList<string> LockedMods { get; }

    public bool HasLocked => LockedMods.Count > 0;

    /// <summary>Whether the step was answered by going to the editor instead of publishing.</summary>
    public bool ReviewChosen { get; private set; }


    private SavegameModCheckRow Row(IReadOnlyList<SavegameModDifference> differences)
    {
        var names = differences.Take(NamesInTooltip).Select(x => _nameOf(x.ModId)).ToList();

        if (differences.Count > NamesInTooltip)
        {
            names.Add($"and {differences.Count - NamesInTooltip:N0} more");
        }

        return new SavegameModCheckRow(differences.Count.ToString("N0"), names.Count > 0 ? string.Join(Environment.NewLine, names) : null);
    }
}


/// <param name="ToolTip">The mods counted, by name, or null where there are none.</param>
public sealed record SavegameModCheckRow(string Value, string? ToolTip);
