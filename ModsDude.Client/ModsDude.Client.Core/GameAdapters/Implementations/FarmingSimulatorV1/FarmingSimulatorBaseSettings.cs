using ModsDude.Client.Core.GameAdapters.DynamicForms;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorBaseSettings : AdapterSettings<FarmingSimulatorBaseSettings>
{
    [Required, Title("Test property 1")]
    public string? TestProperty1 { get; set; }

    [Required, Title("Test property 2")]
    public string? TestProperty2 { get; set; }

    [Title("Test property 3")]
    public string? TestProperty3 { get; set; }


    protected override IEnumerable<AdapterSettingsValidationError<FarmingSimulatorBaseSettings>> Validate()
    {
        if (TestProperty1 == "fail")
        {
            yield return new("Test fail for 1", nameof(TestProperty1));

            yield return new("Test fail for 1 and 2", nameof(TestProperty2), nameof(TestProperty3));
        }
    }
}
