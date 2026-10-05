using ModsDude.Server.Domain.Backups;

namespace ModsDude.Server.Domain.Tests.Backups;

public class BackupStatusTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);


    [Fact]
    public void An_empty_container_reports_every_tier_missing()
    {
        var overview = BackupStatus.Summarise([], _now);

        Assert.Equal([BackupTier.Hourly, BackupTier.Daily], overview.Tiers.Select(x => x.Tier));
        Assert.All(overview.Tiers, tier =>
        {
            Assert.Equal(BackupFreshness.Missing, tier.Freshness);
            Assert.Null(tier.Newest);
            Assert.Null(tier.Oldest);
            Assert.Equal(0, tier.Count);
        });
        Assert.Empty(overview.Unrecognised);
    }

    [Fact]
    public void A_tier_without_backups_is_missing_while_the_other_is_ok()
    {
        var overview = BackupStatus.Summarise([Blob("hourly", _now.AddMinutes(-10))], _now);

        Assert.Equal(BackupFreshness.Ok, Tier(overview, BackupTier.Hourly).Freshness);
        Assert.Equal(BackupFreshness.Missing, Tier(overview, BackupTier.Daily).Freshness);
    }

    [Theory]
    [InlineData("hourly", 2 * 60, BackupFreshness.Ok)]
    [InlineData("hourly", 2 * 60 + 1, BackupFreshness.Late)]
    [InlineData("daily", 26 * 60, BackupFreshness.Ok)]
    [InlineData("daily", 26 * 60 + 1, BackupFreshness.Late)]
    public void A_tier_is_late_only_once_its_newest_backup_is_older_than_allowed(string tier, int ageMinutes, BackupFreshness expected)
    {
        var overview = BackupStatus.Summarise([Blob(tier, _now.AddMinutes(-ageMinutes))], _now);

        Assert.Equal(expected, overview.Tiers.Single(x => x.Count == 1).Freshness);
    }

    [Fact]
    public void Freshness_is_judged_by_the_newest_backup_not_the_oldest()
    {
        var overview = BackupStatus.Summarise(
            [Blob("hourly", _now.AddHours(-40)), Blob("hourly", _now.AddMinutes(-30))],
            _now);

        Assert.Equal(BackupFreshness.Ok, Tier(overview, BackupTier.Hourly).Freshness);
    }

    [Fact]
    public void A_tier_reports_its_newest_oldest_and_count()
    {
        var overview = BackupStatus.Summarise(
            [
                Blob("daily", _now.AddDays(-1), length: 200),
                Blob("daily", _now.AddDays(-29), length: 100),
                Blob("daily", _now.AddDays(-15), length: 150)
            ],
            _now);

        var daily = Tier(overview, BackupTier.Daily);

        Assert.Equal(_now.AddDays(-1), daily.Newest?.TakenAt);
        Assert.Equal(200, daily.Newest?.Length);
        Assert.Equal(_now.AddDays(-29), daily.Oldest?.TakenAt);
        Assert.Equal(3, daily.Count);
    }

    [Fact]
    public void The_result_does_not_depend_on_listing_order()
    {
        ListedBackupBlob[] blobs =
        [
            Blob("hourly", _now.AddHours(-3)),
            Blob("daily", _now.AddDays(-2)),
            Blob("hourly", _now.AddHours(-1)),
            new("stray.txt", 1),
            Blob("daily", _now.AddHours(-9)),
            new("another/stray", 1)
        ];

        var forwards = BackupStatus.Summarise(blobs, _now);
        var backwards = BackupStatus.Summarise(Enumerable.Reverse(blobs), _now);

        Assert.Equal(forwards.Tiers, backwards.Tiers);
        Assert.Equal(forwards.Unrecognised, backwards.Unrecognised);
        Assert.Equal(["another/stray", "stray.txt"], forwards.Unrecognised);
    }

    [Fact]
    public void Blobs_outside_the_layout_are_unrecognised_rather_than_counted()
    {
        var overview = BackupStatus.Summarise(
            [
                new("weekly/modsdude-20261005T110000Z.dump", 1),
                new("hourly/modsdude-20261005T110000Z.sql", 1),
                new("hourly/other-20261005T110000Z.dump", 1),
                new("hourly/modsdude-2026-10-05.dump", 1),
                new("hourly/nested/modsdude-20261005T110000Z.dump", 1),
                new("modsdude-20261005T110000Z.dump", 1)
            ],
            _now);

        Assert.All(overview.Tiers, x => Assert.Equal(0, x.Count));
        Assert.Equal(6, overview.Unrecognised.Count);
    }

    [Fact]
    public void A_backup_name_is_read_as_utc()
    {
        Assert.True(StoredBackup.TryParse(new ListedBackupBlob("daily/modsdude-20261005T010203Z.dump", 42), out var backup));

        Assert.Equal(BackupTier.Daily, backup.Tier);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 1, 2, 3, TimeSpan.Zero), backup.TakenAt);
        Assert.Equal(42, backup.Length);
    }


    private static ListedBackupBlob Blob(string tier, DateTimeOffset takenAt, long length = 1)
    {
        return new ListedBackupBlob($"{tier}/modsdude-{takenAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}.dump", length);
    }

    private static BackupTierStatus Tier(BackupOverview overview, BackupTier tier)
    {
        return overview.Tiers.Single(x => x.Tier == tier);
    }
}
