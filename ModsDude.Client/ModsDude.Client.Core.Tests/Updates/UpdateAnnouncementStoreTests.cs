using ModsDude.Client.Core.Updates;

namespace ModsDude.Client.Core.Tests.Updates;

public class UpdateAnnouncementStoreTests
{
    private static readonly DateTime _announcedAt = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void It_says_changed_only_when_the_announcement_did()
    {
        var store = new UpdateAnnouncementStore();
        var changes = 0;
        store.Changed += (_, _) => changes++;

        store.Set(_announcedAt);
        store.Set(_announcedAt);
        store.Set(null);
        store.Set(null);

        Assert.Equal(2, changes);
        Assert.Null(store.AnnouncedAt);
    }
}
