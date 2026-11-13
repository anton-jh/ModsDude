using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Domain.Tests.Savegames;

/// <summary>
/// A detail is stored in its kind's invariant form, so every reader can word it in their own culture
/// and time zone. A value in any other form is refused rather than stored as somebody's local wording.
/// </summary>
public class SavegameDetailTests
{
    [Theory]
    [InlineData(SavegameDetailKind.Text, "Zielonka")]
    [InlineData(SavegameDetailKind.Date, "2024-11-30")]
    [InlineData(SavegameDetailKind.Instant, "2024-11-30T23:00:00.0000000+00:00")]
    [InlineData(SavegameDetailKind.Duration, "2717")]
    public void A_value_in_its_kinds_form_is_accepted(SavegameDetailKind kind, string value)
    {
        var detail = new SavegameDetail("key", "Label", kind, value, 0);

        Assert.Equal(value, detail.Value);
    }

    [Theory]
    [InlineData(SavegameDetailKind.Date, "30/11/2024")]
    [InlineData(SavegameDetailKind.Date, "2024-11-30T00:00:00")]
    [InlineData(SavegameDetailKind.Instant, "2024-11-30 23:00")]
    [InlineData(SavegameDetailKind.Instant, "2024-11-30T23:00:00.0000000+02:00")]
    [InlineData(SavegameDetailKind.Duration, "45 h")]
    [InlineData(SavegameDetailKind.Duration, "-5")]
    [InlineData(SavegameDetailKind.Text, " ")]
    public void A_value_in_another_form_is_refused(SavegameDetailKind kind, string value)
    {
        Assert.False(SavegameDetailValues.IsValid(kind, value));
        Assert.Throws<DomainValidationException>(() => new SavegameDetail("key", "Label", kind, value, 0));
    }
}
