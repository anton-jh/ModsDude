using ModsDude.Client.Core.Builds;
using ModsDude.Client.Core.Notices;

namespace ModsDude.Client.Core.Tests.Notices;

public class UpdateNoticeTests
{
    [Fact]
    public void It_names_the_version_and_offers_a_restart()
    {
        var notice = UpdateNotice.For(new BuildNumber(14));

        Assert.Contains("b14", notice.Headline);
        Assert.Equal(NoticeSeverity.Info, notice.Severity);

        var action = Assert.Single(notice.Actions);
        Assert.Equal(NoticeActionKind.RestartToUpdate, action.Kind);
        Assert.True(action.IsPrimary);
    }

    [Fact]
    public void It_can_be_dismissed_and_is_not_a_subject_of_any_game()
    {
        var notice = UpdateNotice.For(new BuildNumber(14));

        Assert.True(notice.CanDismiss);
        Assert.Null(notice.Subject);
    }

    [Fact]
    public void A_newer_version_is_a_different_signature_under_the_same_key_so_a_dismissal_does_not_outlive_it()
    {
        var older = UpdateNotice.For(new BuildNumber(14));
        var newer = UpdateNotice.For(new BuildNumber(15));

        Assert.Equal(older.Key, newer.Key);
        Assert.NotEqual(older.Signature, newer.Signature);
    }

    [Fact]
    public void It_is_never_worth_a_toast()
    {
        var toast = new DriftToastPlanner().Observe([UpdateNotice.For(new BuildNumber(14))], reposLoaded: true, windowInFront: false);

        Assert.Null(toast);
    }
}
