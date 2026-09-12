using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// The list an adapter answers with, and the tripwire the callers that have not been widened yet go
/// through.
/// </summary>
public class ModTargetsTests
{
    [Fact]
    public void The_one_target_is_what_a_narrow_caller_gets()
    {
        var targets = new ModTargets(new ModTarget(new TargetKey("mods"), null, @"C:\mods"));

        Assert.Equal(@"C:\mods", targets.RequireSingleTarget().Path);
    }

    /// <summary>
    /// The whole reason the helper is named rather than being <c>.Single()</c>. A caller that quietly
    /// took the first target would work perfectly for Farming Simulator and silently leave two of
    /// BeamNG's three folders on the old mod list, which is the failure this phase exists to prevent.
    /// </summary>
    [Fact]
    public void A_narrow_caller_handed_several_targets_throws_rather_than_taking_the_first()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Two().RequireSingleTarget());

        Assert.Contains("server", exception.Message);
        Assert.Contains("client", exception.Message);
    }

    /// <summary>Several is the tripwire whichever way it is asked.</summary>
    [Fact]
    public void Several_targets_are_refused_even_of_the_forgiving_form()
    {
        Assert.Throws<InvalidOperationException>(() => Two().SingleTargetOrNone());
    }

    /// <summary>
    /// Somebody connected a game and has not filled a path in. Ordinary, and said in a sentence they
    /// can act on rather than thrown as a fault - which is the whole difference between none and
    /// several.
    /// </summary>
    [Fact]
    public void A_game_reaching_no_folder_is_told_so_rather_than_crashing()
    {
        Assert.Throws<UserFriendlyException>(() => ModTargets.None.RequireSingleTarget());
    }

    /// <summary>
    /// And a caller that has something sensible to say about reaching no folder - the ownership check
    /// answers "claims nothing" - gets to say it, without an exception in the middle.
    /// </summary>
    [Fact]
    public void A_caller_that_can_live_without_a_folder_gets_none_rather_than_an_exception()
    {
        Assert.Null(ModTargets.None.SingleTargetOrNone());
    }

    [Fact]
    public void A_target_can_be_found_by_its_key()
    {
        Assert.Equal(@"C:\client\mods", Two()[new TargetKey("client")]?.Path);
    }

    /// <summary>
    /// What a settings edit that empties a field looks like to everything keyed on the target: the
    /// key is simply not there any more. Nothing throws - the manifest behind it is stale and a
    /// savegame binding behind it is this machine still holding a save, and those are decided where
    /// they live rather than here.
    /// </summary>
    [Fact]
    public void A_key_no_longer_produced_by_the_settings_finds_nothing()
    {
        var targets = new ModTargets(new ModTarget(new TargetKey("server"), "Dedicated server", @"C:\server\mods"));

        Assert.Null(targets[new TargetKey("client")]);
    }

    /// <summary>
    /// Distinct keys are a construction rather than something every adapter author is trusted with.
    /// Two targets sharing one key would put two folders on one manifest, and the second sync would
    /// report everything the first installed as drift.
    /// </summary>
    [Fact]
    public void An_adapter_returning_two_targets_under_one_key_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new ModTargets(
            new ModTarget(new TargetKey("mods"), null, @"C:\one"),
            new ModTarget(new TargetKey("mods"), null, @"C:\two")));
    }

    /// <summary>
    /// The one thing a key may not be, because <c>SavegameSlotRef</c> renders as
    /// <c>{target}:{slot}</c> and has to be able to read it back.
    /// </summary>
    [Theory]
    [InlineData("mp:client")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_key_that_could_not_be_read_back_is_refused(string raw)
    {
        Assert.Throws<ArgumentException>(() => new TargetKey(raw));
    }

    /// <summary>
    /// Deliberately not refused. The key ends up in a filename, and encoding it is the store's
    /// business - a rule adapter authors had to obey would be one that fails as a manifest that
    /// cannot be written, discovered at sync time on somebody else's machine.
    /// </summary>
    [Theory]
    [InlineData("mp/client")]
    [InlineData("mp client")]
    [InlineData("CON")]
    public void A_key_that_is_merely_awkward_in_a_filename_is_accepted(string raw)
    {
        Assert.Equal(raw, new TargetKey(raw).Value);
    }


    private static ModTargets Two() => new(
        new ModTarget(new TargetKey("server"), "Dedicated server", @"C:\server\mods"),
        new ModTarget(new TargetKey("client"), "MP client", @"C:\client\mods"));
}
