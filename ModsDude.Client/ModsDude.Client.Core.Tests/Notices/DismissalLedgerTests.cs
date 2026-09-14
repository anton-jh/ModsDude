using ModsDude.Client.Core.Notices;

namespace ModsDude.Client.Core.Tests.Notices;

/// <summary>
/// The rules the single drift card's one dismiss button used to carry, now that there is a button
/// per notice.
/// </summary>
public class DismissalLedgerTests
{
    [Fact]
    public void A_dismissal_lasts_while_the_notice_says_the_same_thing()
    {
        var ledger = new DismissalLedger();

        ledger.Dismiss("drift/fs25/mods", "2 replaced");

        Assert.True(ledger.IsDismissed("drift/fs25/mods", "2 replaced"));
    }

    /// <summary>
    /// A third stray mod is a different problem from the two that were waved away.
    /// </summary>
    [Fact]
    public void A_dismissal_ends_the_moment_the_notice_says_something_else()
    {
        var ledger = new DismissalLedger();

        ledger.Dismiss("drift/fs25/mods", "2 replaced");

        Assert.False(ledger.IsDismissed("drift/fs25/mods", "3 replaced"));
    }

    /// <summary>
    /// The whole point of the column: waving away a folder that gained two stray mods must not also
    /// silence a locked map, a savegame nobody has checked in, or another game entirely. The single
    /// card could not do this - it had one signature over everything and one button.
    /// </summary>
    [Fact]
    public void Dismissing_one_notice_leaves_every_other_one_showing()
    {
        var ledger = new DismissalLedger();

        ledger.Dismiss("drift/fs25/mods", "2 replaced");

        Assert.False(ledger.IsDismissed("locked/fs25/mods", "the map is gone"));
        Assert.False(ledger.IsDismissed("save/abc", "played since check-out"));
        Assert.False(ledger.IsDismissed("drift/beamng/mods", "2 replaced"));
    }

    [Fact]
    public void Dismiss_all_takes_everything_that_may_be_dismissed()
    {
        var ledger = new DismissalLedger();

        var notices = new[]
        {
            Notice("drift/fs25/mods", "a"),
            Notice("save/abc", "b"),
            Notice("unreachable/fs25", "c") with { CanDismiss = false }
        };

        ledger.DismissAll(notices);

        Assert.True(ledger.IsDismissed("drift/fs25/mods", "a"));
        Assert.True(ledger.IsDismissed("save/abc", "b"));

        // A notice that reports a state rather than an event does not stop being true because
        // somebody pressed a button beside it.
        Assert.False(ledger.IsDismissed("unreachable/fs25", "c"));
    }

    /// <summary>
    /// A problem that was waved away and then actually fixed must leave nothing behind, or the same
    /// problem next week is swallowed by a dismissal nobody remembers making.
    /// </summary>
    [Fact]
    public void A_dismissal_is_forgotten_once_its_notice_stops_being_raised()
    {
        var ledger = new DismissalLedger();

        ledger.Dismiss("drift/fs25/mods", "2 replaced");

        // The re-apply landed, so this build has nothing to say about that folder.
        ledger.Retain(["save/abc"]);

        // And the same drift happens again.
        Assert.False(ledger.IsDismissed("drift/fs25/mods", "2 replaced"));
    }

    [Fact]
    public void Retaining_keeps_the_dismissals_whose_notices_are_still_up()
    {
        var ledger = new DismissalLedger();

        ledger.Dismiss("drift/fs25/mods", "2 replaced");
        ledger.Retain(["drift/fs25/mods", "save/abc"]);

        Assert.True(ledger.IsDismissed("drift/fs25/mods", "2 replaced"));
    }

    /// <summary>
    /// Nothing here is persisted: a dismissed warning that never comes back is a savegame silently at
    /// risk. A new ledger is what a restart produces.
    /// </summary>
    [Fact]
    public void Signing_out_clears_what_the_previous_account_waved_away()
    {
        var ledger = new DismissalLedger();

        ledger.Dismiss("drift/fs25/mods", "2 replaced");
        ledger.Clear();

        Assert.False(ledger.IsDismissed("drift/fs25/mods", "2 replaced"));
    }


    private static Notice Notice(string key, string signature)
        => new(key, signature, NoticeSeverity.Warning, "headline");
}
