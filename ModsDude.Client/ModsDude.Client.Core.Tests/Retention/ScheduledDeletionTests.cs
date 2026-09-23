using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Retention;
using System.Globalization;

namespace ModsDude.Client.Core.Tests.Retention;

public class ScheduledDeletionTests
{
    private static readonly DateOnly _today = new(2026, 9, 23);


    [Fact]
    public void Nothing_scheduled_says_nothing()
    {
        Assert.Null(ScheduledDeletion.Describe(null, _today));
        Assert.Null(ScheduledDeletion.Explain(null, RetainedKind.Snapshot));
    }

    [Fact]
    public void A_date_this_year_leaves_the_year_out()
    {
        using var _ = new CultureScope("en-GB");

        Assert.Equal("To be deleted 7 October", ScheduledDeletion.Describe(new DateOnly(2026, 10, 7), _today));
    }

    [Fact]
    public void A_date_next_year_says_which()
    {
        using var _ = new CultureScope("en-GB");

        Assert.Equal("To be deleted 5 January 2027", ScheduledDeletion.Describe(new DateOnly(2027, 1, 5), _today));
    }

    [Theory]
    [InlineData(RetainedKind.Snapshot)]
    [InlineData(RetainedKind.Revision)]
    [InlineData(RetainedKind.ModVersion)]
    public void Every_kind_explains_both_reasons(RetainedKind kind)
    {
        Assert.NotNull(ScheduledDeletion.Explain(DeletionReason.OutsideWindow, kind));
        Assert.NotNull(ScheduledDeletion.Explain(DeletionReason.WindingDown, kind));
    }


    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
