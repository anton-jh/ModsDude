using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class FileTransferEntityTypeConfiguration : IEntityTypeConfiguration<FileTransfer>
{
    public void Configure(EntityTypeBuilder<FileTransfer> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.File).HasConversion<string>();
        builder.Property(x => x.Direction).HasConversion<string>();

        // No foreign key onto the repo: traffic stays counted after the repo is deleted.
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.RepoId, x.At });
        builder.HasIndex(x => new { x.UserId, x.At });
        builder.HasIndex(x => x.At);

        builder.ToTable(x => x.HasCheckConstraint("CK_FileTransfers_SizeNotNegative", "\"SizeBytes\" >= 0"));
    }
}
