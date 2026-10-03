using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Statistics;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class StorageUsageSampleEntityTypeConfiguration : IEntityTypeConfiguration<StorageUsageSample>
{
    public void Configure(EntityTypeBuilder<StorageUsageSample> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Container).HasConversion<string>();

        // No foreign key onto the repo: the history of a deleted repo's storage stays part of the trend.
        builder.HasIndex(x => new { x.Date, x.Container, x.RepoId }).IsUnique().AreNullsDistinct(false);

        builder.ToTable(x =>
        {
            x.HasCheckConstraint("CK_StorageUsageSamples_StoredNotNegative", "\"StoredBytes\" >= 0 AND \"BlobCount\" >= 0");
            x.HasCheckConstraint("CK_StorageUsageSamples_RegisteredNotNegative", "\"RegisteredBytes\" IS NULL OR \"RegisteredBytes\" >= 0");
        });
    }
}
