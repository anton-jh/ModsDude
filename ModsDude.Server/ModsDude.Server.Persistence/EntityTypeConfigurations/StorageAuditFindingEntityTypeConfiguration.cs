using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.ValueConverters;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;

internal class StorageAuditFindingEntityTypeConfiguration : IEntityTypeConfiguration<StorageAuditFinding>
{
    public void Configure(EntityTypeBuilder<StorageAuditFinding> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Container).HasConversion<string>();
        builder.Property(x => x.Kind).HasConversion<string>();
        builder.Property(x => x.BlobName).HasMaxLength(1024);
        builder.Property(x => x.Version).HasMaxLength(256);
        builder.Property(x => x.Metadata).AsBlobMetadata();

        builder.HasIndex(x => new { x.Kind, x.Container, x.BlobName }).IsUnique();
    }
}
