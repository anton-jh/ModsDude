using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Users;

namespace ModsDude.Client.Core.Tests.Users;

public class UserDisplayTests
{
    [Fact]
    public void Nobody_is_ambiguous_when_every_name_is_different()
    {
        var ambiguous = UserDisplay.FindAmbiguous([
            User("a", "Anton"),
            User("b", "Bertil")
        ]);

        Assert.Empty(ambiguous);
    }

    [Fact]
    public void Both_of_two_users_sharing_a_name_are_ambiguous()
    {
        var ambiguous = UserDisplay.FindAmbiguous([
            User("a", "Anton"),
            User("b", "Anton"),
            User("c", "Bertil")
        ]);

        Assert.Equal(["a", "b"], ambiguous.Order());
    }

    [Fact]
    public void A_name_that_differs_only_in_case_is_still_the_same_name_to_a_reader()
    {
        var ambiguous = UserDisplay.FindAmbiguous([
            User("a", "Anton"),
            User("b", "anton")
        ]);

        Assert.Equal(2, ambiguous.Count);
    }

    [Fact]
    public void A_colour_follows_the_tag()
    {
        Assert.Equal(UserDisplay.ColorFor("4821"), UserDisplay.ColorFor("4821"));
        Assert.NotEqual(UserDisplay.ColorFor("4821"), UserDisplay.ColorFor("4822"));
    }

    [Fact]
    public void A_colour_is_a_hex_triplet()
    {
        var color = UserDisplay.ColorFor("4821");

        Assert.Equal(7, color.Length);
        Assert.StartsWith("#", color);
    }

    [Theory]
    [InlineData("anton", "A")]
    [InlineData("  öystein", "Ö")]
    [InlineData("7 of nine", "7")]
    [InlineData("...", "")]
    [InlineData("", "")]
    public void The_avatar_carries_the_first_thing_in_the_name_worth_drawing(string name, string expected)
    {
        Assert.Equal(expected, UserDisplay.InitialFor(name));
    }


    private static UserDto User(string id, string displayName)
    {
        return new UserDto()
        {
            Id = id,
            DisplayName = displayName,
            Tag = "0000"
        };
    }
}
