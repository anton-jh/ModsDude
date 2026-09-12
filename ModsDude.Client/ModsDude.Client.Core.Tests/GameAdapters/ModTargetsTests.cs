using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// The list an adapter answers with: how a target is addressed, and the two things a key may not be.
/// </summary>
public class ModTargetsTests
{
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
