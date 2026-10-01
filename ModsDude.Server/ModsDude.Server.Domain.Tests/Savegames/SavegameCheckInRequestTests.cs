using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Savegames;

public class SavegameCheckInRequestTests
{
    private static readonly DateTime _at = new(2026, 3, 3, 20, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void A_request_answers_its_own_id_and_no_other()
    {
        var first = new SavegameCheckInRequestId(Guid.NewGuid());
        var request = Create(first);

        Assert.True(request.Answers(first));
        Assert.False(request.Answers(new SavegameCheckInRequestId(Guid.NewGuid())));
    }

    /// <summary>
    /// A client only repeats its latest check-in, so a newer one replaces the record and the older
    /// one is no longer answered.
    /// </summary>
    [Fact]
    public void A_newer_check_in_replaces_the_older_one_and_its_answer()
    {
        var first = new SavegameCheckInRequestId(Guid.NewGuid());
        var second = new SavegameCheckInRequestId(Guid.NewGuid());
        var takenFrom = new SavegameCheckoutId(Guid.NewGuid());
        var request = Create(first);

        request.Replace(second, _at.AddHours(1), new SavegameSnapshotNumber(8), callerHoldsClaim: false, takenFrom);

        Assert.False(request.Answers(first));
        Assert.True(request.Answers(second));
        Assert.Equal(_at.AddHours(1), request.At);
        Assert.Equal(new SavegameSnapshotNumber(8), request.AnsweredWith);
        Assert.False(request.CallerHoldsClaim);
        Assert.Equal(takenFrom, request.TakenFrom);
    }


    private static SavegameCheckInRequest Create(SavegameCheckInRequestId requestId) => new(
        new RepoId(Guid.NewGuid()),
        new SavegameId(Guid.NewGuid()),
        new UserId("anton"),
        requestId,
        _at,
        new SavegameSnapshotNumber(7),
        callerHoldsClaim: true,
        takenFrom: null);
}
