using ModsDude.Client.Core.Notices;

namespace ModsDude.Client.Core.Tests.Notices;

public class UpdateAnnouncementNoticeTests
{
    private static readonly DateTime _announcedAt = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void It_is_pending_offers_nothing_and_can_be_dismissed()
    {
        var notice = UpdateAnnouncementNotice.For(_announcedAt);

        Assert.Equal(NoticeSeverity.Pending, notice.Severity);
        Assert.Empty(notice.Actions);
        Assert.True(notice.CanDismiss);
        Assert.Null(notice.Subject);
    }

    [Fact]
    public void A_later_announcement_is_a_different_signature_under_the_same_key_so_a_dismissal_does_not_outlive_it()
    {
        var earlier = UpdateAnnouncementNotice.For(_announcedAt);
        var later = UpdateAnnouncementNotice.For(_announcedAt.AddHours(1));

        Assert.Equal(earlier.Key, later.Key);
        Assert.NotEqual(earlier.Signature, later.Signature);
    }

    [Fact]
    public void The_same_announcement_keeps_its_signature()
    {
        Assert.Equal(UpdateAnnouncementNotice.For(_announcedAt).Signature, UpdateAnnouncementNotice.For(_announcedAt).Signature);
    }

    [Fact]
    public void It_is_never_worth_a_toast()
    {
        var toast = new DriftToastPlanner().Observe([UpdateAnnouncementNotice.For(_announcedAt)], reposLoaded: true, windowInFront: false);

        Assert.Null(toast);
    }
}
