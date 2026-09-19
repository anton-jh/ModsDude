using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Profiles;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ProfileVersionMovesTests
{
    [Fact]
    public void A_mod_the_profile_does_not_hold_is_an_add()
    {
        Assert.Equal(ProfileVersionMove.Add, Classify("1.1", held: null));
    }

    [Fact]
    public void A_later_version_of_a_held_mod_is_an_update()
    {
        Assert.Equal(ProfileVersionMove.Update, Classify("1.2", held: "1.0"));
    }

    /// <summary>
    /// The row that used to say Update, or nothing at all: the same button as an update, over a move
    /// that goes the other way.
    /// </summary>
    [Fact]
    public void An_earlier_version_of_a_held_mod_is_a_downgrade()
    {
        Assert.Equal(ProfileVersionMove.Downgrade, Classify("1.0", held: "1.2"));
    }

    [Fact]
    public void The_version_the_profile_is_already_on_is_nothing()
    {
        Assert.Equal(ProfileVersionMove.Nothing, Classify("1.2", held: "1.2"));
    }

    /// <summary>
    /// A lock is answered before the direction: what the profile would refuse is the same whichever way
    /// the move goes, and it is what the selection's button has to say.
    /// </summary>
    [Theory]
    [InlineData("1.2", "1.0")]
    [InlineData("1.0", "1.2")]
    public void A_locked_pin_is_locked_whichever_way_the_move_goes(string chosen, string held)
    {
        Assert.Equal(ProfileVersionMove.Locked, Classify(chosen, held, locked: true));
    }

    /// <summary>
    /// Two versions the comparer abstained on come out one after the other in a sorted order, and
    /// calling that an update is how a possible downgrade gets offered as one.
    /// </summary>
    [Fact]
    public void A_pair_the_comparer_could_not_place_is_neither_an_update_nor_a_downgrade()
    {
        var set = new ModVersionSet(
            [Registered("map", "a", 0), Registered("map", "b", 1)],
            [new ModVersionPair(V("a"), V("b"))]);

        Assert.Equal(ProfileVersionMove.Move, ProfileVersionMoves.Classify(V("b"), V("a"), false, set));
        Assert.Equal(ProfileVersionMove.Move, ProfileVersionMoves.Classify(V("a"), V("b"), false, set));
    }

    [Fact]
    public void With_no_order_to_read_a_different_version_is_just_a_move()
    {
        Assert.Equal(ProfileVersionMove.Move, ProfileVersionMoves.Classify(V("1.1"), V("1.0"), false, null));
    }


    private static ProfileVersionMove Classify(string chosen, string? held, bool locked = false)
    {
        var set = new ModVersionSet(
            [Registered("map", "1.0", 0), Registered("map", "1.1", 1), Registered("map", "1.2", 2)],
            []);

        return ProfileVersionMoves.Classify(V(chosen), held is null ? null : V(held), locked, set);
    }

    private static CatalogModVersion Registered(string modId, string version, int sequenceNumber)
        => new(Mod(modId), V(version), modId, "", IsLocal: false, IsOnServer: true, Locked: false)
        {
            SequenceNumber = sequenceNumber
        };
}
