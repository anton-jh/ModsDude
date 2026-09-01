using ModsDude.Server.Domain.Invites;

namespace ModsDude.Server.Domain.Tests.Invites;

public class InviteCodeTests
{
    [Fact]
    public void A_generated_code_is_twelve_characters_of_the_alphabet()
    {
        var code = InviteCodes.Generate();

        Assert.Equal(InviteCodes.Length, code.Value.Length);
        Assert.DoesNotContain(code.Value, x => x is 'I' or 'L' or 'O' or 'U');
        Assert.All(code.Value, x => Assert.True(char.IsAsciiDigit(x) || char.IsAsciiLetterUpper(x)));
    }

    [Fact]
    public void Two_generated_codes_differ()
    {
        Assert.NotEqual(InviteCodes.Generate(), InviteCodes.Generate());
    }

    [Fact]
    public void A_generated_code_parses_back_to_itself()
    {
        var code = InviteCodes.Generate();

        Assert.True(InviteCodes.TryParse(InviteCodes.Format(code), out var parsed));
        Assert.Equal(code, parsed);
    }

    [Fact]
    public void A_code_is_shown_in_threes_of_four()
    {
        Assert.Equal("ABCD-EFGH-JKMN", InviteCodes.Format(new InviteCode("ABCDEFGHJKMN")));
    }

    [Theory]
    [InlineData("ABCD-EFGH-JKMN")]
    [InlineData("ABCDEFGHJKMN")]
    [InlineData("abcd efgh jkmn")]
    [InlineData("  ABCD-efgh-JKMN  ")]
    [InlineData("ABCD.EFGH.JKMN")]
    public void A_code_survives_however_it_was_written_down(string input)
    {
        Assert.True(InviteCodes.TryParse(input, out var parsed));
        Assert.Equal(new InviteCode("ABCDEFGHJKMN"), parsed);
    }

    [Theory]
    [InlineData("I234567890AB", "1234567890AB")]
    [InlineData("l234567890AB", "1234567890AB")]
    [InlineData("O234567890AB", "0234567890AB")]
    public void The_letters_people_reach_for_instead_of_digits_are_folded_into_them(string input, string expected)
    {
        Assert.True(InviteCodes.TryParse(input, out var parsed));
        Assert.Equal(new InviteCode(expected), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCD-EFGH-JKM")]
    [InlineData("ABCD-EFGH-JKMNP")]
    [InlineData("UBCD-EFGH-JKMN")]
    public void Anything_that_is_not_a_code_is_refused(string? input)
    {
        Assert.False(InviteCodes.TryParse(input, out _));
    }
}
