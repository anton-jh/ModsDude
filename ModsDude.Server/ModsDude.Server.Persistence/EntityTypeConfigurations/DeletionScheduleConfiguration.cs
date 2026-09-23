using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Retention;
using System.Linq.Expressions;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;

internal static class DeletionScheduleConfiguration
{
    /// <summary>
    /// The two columns retention writes, shared by every entity it schedules. The date and the reason
    /// are set together or not at all: a date with no reason cannot be re-checked against the rule that
    /// made it, and a reason with no date schedules nothing.
    /// </summary>
    public static void ConfigureDeletionSchedule<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, DateOnly?>> date,
        Expression<Func<TEntity, DeletionReason?>> reason)
        where TEntity : class
    {
        builder.Property(date);
        builder.Property(reason).HasConversion<string>();

        builder.ToTable(x => x.HasCheckConstraint(
            $"CK_{builder.Metadata.GetTableName()}_DeletionIsScheduledWithAReason",
            "(\"DeletionScheduledFor\" IS NULL) = (\"DeletionReason\" IS NULL)"));
    }
}
