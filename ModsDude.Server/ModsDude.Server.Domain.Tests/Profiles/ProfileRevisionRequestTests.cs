using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Profiles;

public class ProfileRevisionRequestTests
{
    private static readonly DateTime _at = new(2026, 3, 3, 20, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void A_request_answers_its_own_id_and_no_other()
    {
        var first = new ProfileRevisionRequestId(Guid.NewGuid());
        var request = Create(first);

        Assert.True(request.Answers(first));
        Assert.False(request.Answers(new ProfileRevisionRequestId(Guid.NewGuid())));
    }

    [Fact]
    public void A_new_request_records_what_it_was_answered_with()
    {
        var request = Create(new ProfileRevisionRequestId(Guid.NewGuid()));

        Assert.Equal(_at, request.At);
        Assert.Equal(new RevisionNumber(7), request.AnsweredWith);
    }

    [Fact]
    public void A_newer_request_replaces_the_older_one_and_its_answer()
    {
        var first = new ProfileRevisionRequestId(Guid.NewGuid());
        var second = new ProfileRevisionRequestId(Guid.NewGuid());
        var request = Create(first);

        request.Replace(second, _at.AddHours(1), new RevisionNumber(8));

        Assert.False(request.Answers(first));
        Assert.True(request.Answers(second));
        Assert.Equal(_at.AddHours(1), request.At);
        Assert.Equal(new RevisionNumber(8), request.AnsweredWith);
    }


    private static ProfileRevisionRequest Create(ProfileRevisionRequestId requestId) => new(
        new RepoId(Guid.NewGuid()),
        new ProfileId(Guid.NewGuid()),
        new UserId("anton"),
        requestId,
        _at,
        new RevisionNumber(7));
}
