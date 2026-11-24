using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.ValueConverters;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;

internal class DeletedBlobEntityTypeConfiguration : IEntityTypeConfiguration<DeletedBlob>
{
    public void Configure(EntityTypeBuilder<DeletedBlob> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Container).HasConversion<string>();
        builder.Property(x => x.Reason).HasConversion<string>();
        builder.Property(x => x.BlobName).HasMaxLength(1024);
        builder.Property(x => x.RequestedBy).HasMaxLength(256);
        builder.Property(x => x.Metadata).AsBlobMetadata();

        builder.HasIndex(x => x.Deleted);
    }
}
