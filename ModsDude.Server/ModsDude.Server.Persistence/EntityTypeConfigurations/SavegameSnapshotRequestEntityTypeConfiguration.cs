using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class SavegameSnapshotRequestEntityTypeConfiguration : IEntityTypeConfiguration<SavegameSnapshotRequest>
{
    public void Configure(EntityTypeBuilder<SavegameSnapshotRequest> builder)
    {
        // One row per person per savegame: a newer request replaces the row, so the table never grows
        // past the people who have written a snapshot of each savegame.
        builder.HasKey(x => new { x.RepoId, x.SavegameId, x.UserId });

        builder.HasOne<Savegame>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.SavegameId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
