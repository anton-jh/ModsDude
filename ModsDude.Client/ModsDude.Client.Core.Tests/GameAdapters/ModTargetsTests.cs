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
        var targets = new ModTargets(
            new ModTarget(new TargetKey("server"), "Dedicated server", @"C:\server\mods"),
            new ModTarget(new TargetKey("client"), "MP client", @"C:\client\mods"));

        var exception = Assert.Throws<InvalidOperationException>(() => targets.RequireSingleTarget());

        Assert.Contains("server", exception.Message);
        Assert.Contains("client", exception.Message);
    }

    /// <summary>
    /// A game whose settings point at no folder reaches nothing, which is an ordinary answer. It is
    /// only a narrow caller - one that was written when every game had exactly one folder - that
    /// cannot do anything with it.
    /// </summary>
    [Fact]
    public void A_narrow_caller_handed_no_target_throws_too()
    {
        Assert.Throws<InvalidOperationException>(() => ModTargets.None.RequireSingleTarget());
    }

    [Fact]
    public void A_target_can_be_found_by_its_key()
    {
        var targets = new ModTargets(
            new ModTarget(new TargetKey("server"), "Dedicated server", @"C:\server\mods"),
            new ModTarget(new TargetKey("client"), "MP client", @"C:\client\mods"));

        Assert.Equal(@"C:\client\mods", targets[new TargetKey("client")]?.Path);
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
}
