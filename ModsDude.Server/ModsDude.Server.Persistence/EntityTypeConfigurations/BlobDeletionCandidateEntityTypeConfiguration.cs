using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.Storage;
using ModsDude.Server.Persistence.ValueConverters;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;

internal class BlobDeletionCandidateEntityTypeConfiguration : IEntityTypeConfiguration<BlobDeletionCandidate>
{
    public void Configure(EntityTypeBuilder<BlobDeletionCandidate> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Container).HasConversion<string>();
        builder.Property(x => x.Reason).HasConversion<string>();
        builder.Property(x => x.BlobName).HasMaxLength(1024);
        builder.Property(x => x.RequestedBy).HasMaxLength(256);
        builder.Property(x => x.ExpectedVersion).HasMaxLength(256);
        builder.Property(x => x.QueuedAt).HasDefaultValueSql("now()");

        // Rows are written by queue_blob_deletion, which relies on this index to keep one row per blob.
        builder.HasIndex(x => new { x.Container, x.BlobName }).IsUnique().HasDatabaseName(BlobDeletionQueue.UniqueIndex);

        // No foreign keys: a candidate outlives the repo and the user it was queued for.
        builder.ToTable(x =>
        {
            x.HasCheckConstraint("CK_BlobDeletionCandidates_Container", EnumNames.CheckConstraintSql<StorageContainer>(nameof(BlobDeletionCandidate.Container)));
            x.HasCheckConstraint("CK_BlobDeletionCandidates_Reason", EnumNames.CheckConstraintSql<BlobDeletionReason>(nameof(BlobDeletionCandidate.Reason)));
            x.HasCheckConstraint("CK_BlobDeletionCandidates_AdminIsComplete",
                $"\"Reason\" <> '{BlobDeletionReason.Admin}' OR (\"ExpectedVersion\" IS NOT NULL AND \"RequestedBy\" IS NOT NULL)");
        });
    }
}
