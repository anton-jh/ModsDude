using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>What a check-in does to the claim, and a check-in repeated after its answer was lost.</summary>
public class SavegameCheckInClaimTests
{
    private static readonly SavegameSlotRef _slot1 = SavegameHarness.Slot1;


    [Fact]
    public async Task Keeping_playing_keeps_the_claim_on_the_server_too()
    {
        using var harness = await CheckedOutAndPlayedAsync();

        var result = await CheckInAsync(harness, keepPlaying: true);

        Assert.True(result.HoldsClaim);
        Assert.True(harness.Server.ClaimIsMine);
        Assert.True(Assert.Single(harness.Server.CheckIns).KeepPlaying);
        Assert.Null(Binding(harness)!.Value.PendingCheckIn);
    }

    [Fact]
    public async Task Handing_back_ends_the_claim_on_the_server()
    {
        using var harness = await CheckedOutAndPlayedAsync();

        var result = await CheckInAsync(harness, keepPlaying: false);

        Assert.False(result.HoldsClaim);
        Assert.Null(harness.Server.Claim);
        Assert.False(Assert.Single(harness.Server.CheckIns).KeepPlaying);
        Assert.Null(Binding(harness));
    }

    /// <summary>
    /// Launching the game and quitting mints nothing, and keeping playing still keeps the claim - the
    /// case that used to leave the save free on the server while this machine went on holding it.
    /// </summary>
    [Fact]
    public async Task Keeping_playing_with_nothing_changed_keeps_the_claim()
    {
        using var harness = new SavegameHarness();
        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        var result = await CheckInAsync(harness, keepPlaying: true);

        Assert.True(result.HoldsClaim);
        Assert.True(harness.Server.ClaimIsMine);
        Assert.Single(harness.Server.Snapshots);
    }

    [Fact]
    public async Task A_check_in_whose_answer_was_lost_is_repeated_and_answered_as_the_first_time()
    {
        using var harness = await CheckedOutAndPlayedAsync();
        harness.Server.LoseNextAnswer = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => CheckInAsync(harness, keepPlaying: false));

        // The server minted it; this machine never heard. The binding and the slot are untouched.
        Assert.Equal(2, harness.Server.Snapshots.Count);
        Assert.NotNull(Binding(harness)!.Value.PendingCheckIn);
        Assert.Equal("a savegame, played once", harness.ReadSlotFile(_slot1));

        var result = await CheckInAsync(harness, keepPlaying: false);

        Assert.Equal(harness.Server.CheckIns[0].RequestId, harness.Server.CheckIns[1].RequestId);
        Assert.Equal(harness.Server.Snapshots[^1].Number, result.Snapshot.Number);
        Assert.Equal(2, harness.Server.Snapshots.Count);
        Assert.Null(Binding(harness));
    }

    /// <summary>
    /// A repeat is answered as the first one was, so the client goes by the answer and not by what it
    /// asked for this time - or the two ends would disagree about who holds the save again.
    /// </summary>
    [Fact]
    public async Task A_repeat_asking_to_keep_playing_follows_the_first_answer_that_handed_it_back()
    {
        using var harness = await CheckedOutAndPlayedAsync();
        harness.Server.LoseNextAnswer = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => CheckInAsync(harness, keepPlaying: false));

        var result = await CheckInAsync(harness, keepPlaying: true);

        Assert.False(result.HoldsClaim);
        Assert.Null(harness.Server.Claim);
        Assert.Null(Binding(harness));
        Assert.Single(harness.RecycleBin.Recycled);
    }

    /// <summary>
    /// The keep-playing check-in landed, its answer was lost, and the user played on. Other contents
    /// cannot repeat the old request, so the server is asked what became of it: the binding is
    /// rebased onto the snapshot it minted, and the new check-in goes on top - not refused as stale
    /// against the user's own snapshot.
    /// </summary>
    [Fact]
    public async Task Playing_on_after_a_lost_answer_settles_it_and_checks_in_on_top()
    {
        using var harness = await CheckedOutAndPlayedAsync();
        harness.Server.LoseNextAnswer = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => CheckInAsync(harness, keepPlaying: true));

        harness.WriteSlotFile(_slot1, "a savegame, played twice");

        var result = await CheckInAsync(harness, keepPlaying: true);

        Assert.Equal(1, harness.Server.CheckInLookups);
        Assert.NotEqual(harness.Server.CheckIns[0].RequestId, harness.Server.CheckIns[1].RequestId);
        Assert.Equal(2, harness.Server.CheckIns[1].BasedOn);
        Assert.Equal(3, result.Snapshot.Number);
        Assert.Equal(3, Binding(harness)!.Value.Snapshot);
        Assert.Null(Binding(harness)!.Value.PendingCheckIn);
    }

    /// <summary>The check-in never arrived, so there is nothing to settle onto: it is dropped and a new one sent.</summary>
    [Fact]
    public async Task Playing_on_after_a_check_in_that_never_arrived_sends_a_new_one()
    {
        using var harness = await CheckedOutAndPlayedAsync();
        harness.Server.DropNextCheckIn = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => CheckInAsync(harness, keepPlaying: true));

        harness.WriteSlotFile(_slot1, "a savegame, played twice");

        var result = await CheckInAsync(harness, keepPlaying: true);

        Assert.Equal(1, harness.Server.CheckInLookups);
        Assert.Equal(1, Assert.Single(harness.Server.CheckIns).BasedOn);
        Assert.Equal(2, result.Snapshot.Number);
        Assert.Null(Binding(harness)!.Value.PendingCheckIn);
    }

    /// <summary>
    /// The lost check-in handed the save back, and the user played on in a slot nobody holds any more.
    /// That play is kept where it is as a save of their own; checking it in on top of an ended claim
    /// is refused.
    /// </summary>
    [Fact]
    public async Task Playing_on_after_a_lost_hand_back_keeps_the_play_and_refuses_the_check_in()
    {
        using var harness = await CheckedOutAndPlayedAsync();
        harness.Server.LoseNextAnswer = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => CheckInAsync(harness, keepPlaying: false));

        harness.WriteSlotFile(_slot1, "a savegame, played twice");

        await Assert.ThrowsAsync<Exceptions.UserFriendlyException>(() => CheckInAsync(harness, keepPlaying: true));

        Assert.Single(harness.Server.CheckIns);
        Assert.Null(Binding(harness));
        Assert.Empty(harness.RecycleBin.Recycled);
        Assert.Equal("a savegame, played twice", harness.ReadSlotFile(_slot1));
        Assert.Equal(SavegameSlotAvailability.Unrecognised, await harness.Slots.ClassifySlotAsync(harness.Game, _slot1, CancellationToken.None));
    }

    [Fact]
    public async Task A_check_in_after_an_answered_one_is_a_new_request()
    {
        using var harness = await CheckedOutAndPlayedAsync();

        await CheckInAsync(harness, keepPlaying: true);

        harness.WriteSlotFile(_slot1, "a savegame, played twice");

        await CheckInAsync(harness, keepPlaying: true);

        Assert.NotEqual(harness.Server.CheckIns[0].RequestId, harness.Server.CheckIns[1].RequestId);
        Assert.Equal(3, harness.Server.Snapshots.Count);
    }

    [Fact]
    public async Task Keeping_playing_a_save_somebody_took_over_is_refused_naming_them_and_changes_nothing()
    {
        using var harness = await CheckedOutAndPlayedAsync();
        harness.Server.TakenOverBy("Friend");

        var exception = await Assert.ThrowsAsync<ApiException<CustomProblemDetails>>(() => CheckInAsync(harness, keepPlaying: true));

        Assert.Equal(ProblemType.SavegameClaimHeldByOther, exception.Result.Type);
        Assert.Equal("Friend", exception.Result.Holder!.User.DisplayName);
        Assert.Single(harness.Server.Snapshots);
        Assert.NotNull(Binding(harness));
        Assert.Empty(harness.RecycleBin.Recycled);
    }

    [Fact]
    public async Task Agreeing_to_take_it_back_keeps_playing_and_says_from_whom()
    {
        using var harness = await CheckedOutAndPlayedAsync();
        harness.Server.TakenOverBy("Friend");

        await Assert.ThrowsAsync<ApiException<CustomProblemDetails>>(() => CheckInAsync(harness, keepPlaying: true));

        var result = await CheckInAsync(harness, keepPlaying: true, takeOver: true);

        Assert.True(result.HoldsClaim);
        Assert.Equal("Friend", result.TakenFrom!.User.DisplayName);
        Assert.True(harness.Server.ClaimIsMine);
        Assert.NotNull(Binding(harness));
    }

    /// <summary>A check-in that hands the save back takes nothing from anybody.</summary>
    [Fact]
    public async Task Handing_back_a_save_somebody_took_over_leaves_their_claim()
    {
        using var harness = await CheckedOutAndPlayedAsync();
        harness.Server.TakenOverBy("Friend");

        var result = await CheckInAsync(harness, keepPlaying: false);

        Assert.False(result.HoldsClaim);
        Assert.Equal("Friend", harness.Server.Claim!.User.DisplayName);
        Assert.Null(Binding(harness));
    }

    [Fact]
    public async Task A_cancelled_check_in_sends_nothing_and_leaves_the_binding()
    {
        using var harness = await CheckedOutAndPlayedAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.CheckIn.CheckInAsync(
            harness.Game, harness.Server.SavegameId, null, keepPlaying: false, force: false, takeOver: false, cancelled.Token));

        Assert.Empty(harness.Server.CheckIns);
        Assert.NotNull(Binding(harness));
        Assert.Equal("a savegame, played once", harness.ReadSlotFile(_slot1));
    }


    private static async Task<SavegameHarness> CheckedOutAndPlayedAsync()
    {
        var harness = new SavegameHarness();

        await harness.SeedHeadAsync("a savegame");
        await harness.CheckOut.CheckOutAsync(harness.Game, harness.Server.Savegame, _slot1, SavegameRevisionMode.Latest, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a savegame, played once");

        return harness;
    }

    private static Task<SavegameCheckInResult> CheckInAsync(SavegameHarness harness, bool keepPlaying, bool takeOver = false)
        => harness.CheckIn.CheckInAsync(
            harness.Game, harness.Server.SavegameId, null, keepPlaying, force: false, takeOver, CancellationToken.None);

    private static SavegameCheckoutBinding? Binding(SavegameHarness harness)
        => harness.Bindings.GetBinding(harness.Game.Identity, harness.Server.SavegameId);
}
