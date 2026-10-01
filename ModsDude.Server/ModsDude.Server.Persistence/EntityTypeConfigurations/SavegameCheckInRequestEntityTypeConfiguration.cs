using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class SavegameCheckInRequestEntityTypeConfiguration : IEntityTypeConfiguration<SavegameCheckInRequest>
{
    public void Configure(EntityTypeBuilder<SavegameCheckInRequest> builder)
    {
        // One row per person per savegame: a newer check-in replaces the row, so the table never grows
        // past the people who have checked each savegame in.
        builder.HasKey(x => new { x.RepoId, x.SavegameId, x.UserId });

        builder.HasOne<Savegame>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.SavegameId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
