using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class SavegamePublishRequestEntityTypeConfiguration : IEntityTypeConfiguration<SavegamePublishRequest>
{
    public void Configure(EntityTypeBuilder<SavegamePublishRequest> builder)
    {
        // One row per savegame: only the publish that created it can be repeated.
        builder.HasKey(x => new { x.RepoId, x.SavegameId });

        builder.HasIndex(x => x.RequestId).IsUnique();

        builder.HasOne<Savegame>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.SavegameId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
