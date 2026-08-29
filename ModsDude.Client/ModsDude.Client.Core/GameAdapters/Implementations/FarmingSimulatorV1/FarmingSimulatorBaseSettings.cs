using ModsDude.Client.Core.GameAdapters.DynamicForms;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

public class FarmingSimulatorBaseSettings : DynamicForm<FarmingSimulatorBaseSettings>
{
    // Deliberately not [CanBeModified]: the instance scope is derived from this, so an admin
    // editing it would silently orphan every instance on every member's machine. An FS22 repo
    // cannot become an FS25 repo.
    [Required, Title("Game version")]
    public FarmingSimulatorGameVersion? GameVersion { get; set; }


    protected override IEnumerable<DynamicFormValidationError<FarmingSimulatorBaseSettings>> PerformValidation()
    {
        if (GameVersion is null)
        {
            yield return new("Pick which game in the series this repo is for.", nameof(GameVersion));
        }
    }
}

/// <summary>
/// The games this adapter serves. The member name is what the instance scope keys on; the value is
/// the year the game names its data folder after.
/// </summary>
public enum FarmingSimulatorGameVersion
{
    [Title("Farming Simulator 22")]
    Fs22 = 2022,

    [Title("Farming Simulator 25")]
    Fs25 = 2025
}
