using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Domain.Tests.Storage;

public class BlobDeletionRuleTests
{
    private const string _name = "11111111-1111-1111-1111-111111111111/a_mod/1.0.0";

    private static readonly DateTime _now = new(2026, 11, 23, 4, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan _minimumAge = TimeSpan.FromDays(1);


    [Fact]
    public void An_old_blob_nothing_refers_to_is_deleted()
    {
        Assert.Equal(BlobDeletionVerdict.Delete, Decide(Candidate(), Stored(ageDays: 2), isReferenced: false).Verdict);
    }

    [Fact]
    public void A_blob_something_refers_to_is_dropped_from_the_queue()
    {
        Assert.Equal(BlobDeletionVerdict.Drop, Decide(Candidate(), Stored(ageDays: 2), isReferenced: true).Verdict);
    }

    [Fact]
    public void A_blob_that_is_gone_is_dropped_from_the_queue()
    {
        Assert.Equal(BlobDeletionVerdict.Drop, Decide(Candidate(), null, isReferenced: false).Verdict);
    }

    [Fact]
    public void An_upload_whose_blob_is_not_there_yet_stays_queued_while_it_may_still_be_under_way()
    {
        var upload = Candidate(BlobDeletionReason.UploadIssued, queuedAt: _now.AddHours(-1));

        Assert.Equal(BlobDeletionVerdict.Keep, Decide(upload, null, isReferenced: false).Verdict);
    }

    [Fact]
    public void An_upload_whose_blob_never_came_is_dropped_once_the_minimum_age_has_passed()
    {
        var upload = Candidate(BlobDeletionReason.UploadIssued, queuedAt: _now - _minimumAge);

        Assert.Equal(BlobDeletionVerdict.Drop, Decide(upload, null, isReferenced: false).Verdict);
    }

    [Theory]
    [InlineData(BlobDeletionReason.ReferenceRemoved)]
    [InlineData(BlobDeletionReason.Admin)]
    public void A_blob_that_is_not_there_stays_queued_while_an_upload_may_be_under_way_whatever_it_was_queued_as_last(BlobDeletionReason reason)
    {
        // An upload queued it, and something queued it again as another reason before the bytes came.
        var requeued = Candidate(reason, queuedAt: _now.AddHours(-1));

        Assert.Equal(BlobDeletionVerdict.Keep, Decide(requeued, null, isReferenced: false).Verdict);
    }

    [Fact]
    public void A_blob_written_too_recently_waits_even_when_nothing_refers_to_it()
    {
        // A registration claims the blob before it commits; the claim is what makes it recent.
        Assert.Equal(BlobDeletionVerdict.Keep, Decide(Candidate(), Stored(ageDays: 0.5), isReferenced: false).Verdict);
    }

    [Fact]
    public void A_blob_too_recent_to_judge_waits_rather_than_being_dropped_when_something_refers_to_it()
    {
        Assert.Equal(BlobDeletionVerdict.Keep, Decide(Candidate(), Stored(ageDays: 0.5), isReferenced: true).Verdict);
    }

    [Fact]
    public void A_blob_written_exactly_the_minimum_age_ago_is_old_enough()
    {
        Assert.Equal(BlobDeletionVerdict.Delete, Decide(Candidate(), Stored(ageDays: 1), isReferenced: false).Verdict);
    }

    [Fact]
    public void A_blob_without_a_version_to_delete_on_is_kept()
    {
        var stored = Stored(ageDays: 2) with { Version = null };

        Assert.Equal(BlobDeletionVerdict.Keep, Decide(Candidate(), stored, isReferenced: false).Verdict);
    }

    [Fact]
    public void A_name_that_does_not_parse_is_never_deleted()
    {
        var candidate = new BlobDeletionCandidate(StorageContainer.Mods, "stray.txt", BlobDeletionReason.ReferenceRemoved);

        Assert.Equal(BlobDeletionVerdict.Keep, Decide(candidate, Stored(ageDays: 2) with { Name = "stray.txt" }, isReferenced: false).Verdict);
    }

    [Fact]
    public void An_admin_choice_is_deleted_only_while_the_blob_is_the_version_the_admin_saw()
    {
        var chosen = Candidate(BlobDeletionReason.Admin, expectedVersion: "v1");

        Assert.Equal(BlobDeletionVerdict.Delete, Decide(chosen, Stored(ageDays: 2, version: "v1"), isReferenced: false).Verdict);
        Assert.Equal(BlobDeletionVerdict.Drop, Decide(chosen, Stored(ageDays: 2, version: "v2"), isReferenced: false).Verdict);
    }

    [Fact]
    public void An_admin_choice_that_something_refers_to_now_is_dropped()
    {
        var chosen = Candidate(BlobDeletionReason.Admin, expectedVersion: "v1");

        Assert.Equal(BlobDeletionVerdict.Drop, Decide(chosen, Stored(ageDays: 2, version: "v1"), isReferenced: true).Verdict);
    }

    [Fact]
    public void A_blob_queued_again_since_it_was_read_is_kept()
    {
        Assert.Equal(BlobDeletionVerdict.Keep, Decide(Candidate(), Stored(ageDays: 2), isReferenced: false, isRequeued: true).Verdict);
    }


    private static BlobDeletionDecision Decide(BlobDeletionCandidate candidate, StoredBlob? stored, bool isReferenced, bool isRequeued = false)
    {
        return BlobDeletionRule.Decide(candidate, stored, isReferenced, isRequeued, _now, _minimumAge);
    }

    private static BlobDeletionCandidate Candidate(
        BlobDeletionReason reason = BlobDeletionReason.ReferenceRemoved,
        string? expectedVersion = null,
        DateTime queuedAt = default)
    {
        return new BlobDeletionCandidate(
            StorageContainer.Mods, _name, reason, expectedVersion: expectedVersion, requestedBy: expectedVersion is null ? null : "admin", queuedAt: queuedAt);
    }

    private static StoredBlob Stored(double ageDays, string version = "v1")
    {
        return new StoredBlob(_name, new DateTimeOffset(_now.AddDays(-ageDays)), 10, version, new Dictionary<string, string>());
    }
}
