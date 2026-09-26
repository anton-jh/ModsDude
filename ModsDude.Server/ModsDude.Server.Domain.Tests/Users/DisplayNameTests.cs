using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Users;

public class DisplayNameTests
{
    [Fact]
    public void A_typed_name_is_trimmed()
    {
        Assert.True(DisplayName.TryParse("  Anton  ", out var name, out _));
        Assert.Equal(new DisplayName("Anton"), name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_typed_name_cannot_be_blank(string? input)
    {
        Assert.False(DisplayName.TryParse(input, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void A_typed_name_can_be_exactly_the_maximum_length()
    {
        Assert.True(DisplayName.TryParse(new string('a', DisplayName.MaximumLength), out _, out _));
    }

    [Fact]
    public void A_typed_name_cannot_be_longer_than_the_maximum()
    {
        Assert.False(DisplayName.TryParse(new string('a', DisplayName.MaximumLength + 1), out _, out _));
    }

    [Fact]
    public void Surrounding_whitespace_does_not_count_towards_the_length()
    {
        Assert.True(DisplayName.TryParse($"  {new string('a', DisplayName.MaximumLength)}  ", out _, out _));
    }

    [Fact]
    public void A_typed_name_cannot_hold_control_characters()
    {
        Assert.False(DisplayName.TryParse("An\nton", out _, out _));
    }

    [Fact]
    public void A_claim_that_is_too_long_is_cut_to_fit_rather_than_refused()
    {
        var name = DisplayName.FromClaim(new string('a', DisplayName.MaximumLength + 10));

        Assert.Equal(DisplayName.MaximumLength, name.Value.Length);
    }

    [Fact]
    public void A_claim_loses_its_control_characters()
    {
        Assert.Equal(new DisplayName("Anton"), DisplayName.FromClaim("An\u0000ton"));
    }

    [Fact]
    public void A_claim_of_only_control_characters_falls_back()
    {
        Assert.Equal(new DisplayName(DisplayName.Fallback), DisplayName.FromClaim("\t\n"));
    }

    [Fact]
    public void Renaming_a_user_stamps_when_their_profile_changed()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var renamed = created.AddDays(1);
        var user = new User(new UserId("subject"), new DisplayName("Anton"), created);

        user.Rename(new DisplayName("Toni"), renamed);

        Assert.Equal(new DisplayName("Toni"), user.DisplayName);
        Assert.Equal(renamed, user.ProfileLastUpdated);
    }
}
