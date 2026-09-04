using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class SavegameCheckoutEntityTypeConfiguration : IEntityTypeConfiguration<SavegameCheckout>
{
    public void Configure(EntityTypeBuilder<SavegameCheckout> builder)
    {
        // A claim is addressed on its own, by the version that was checked in against it, so its id
        // stands alone rather than being qualified by the savegame the way a version's number is.
        builder.HasKey(x => x.Id);

        // Cascade: a deleted savegame takes its claims with it. The log outlives the blobs and
        // survives pruning, but it does not outlive the thing it is a log of.
        builder.HasOne<Savegame>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.SavegameId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.EndedReason).HasConversion<string>();

        // One open claim per savegame, in the database rather than in the endpoint. This is what
        // makes "the current holder is the open row" a fact instead of a convention: two people
        // taking a save at the same instant produce one holder and one refusal, and there is no
        // current-checkout field on Savegame that could disagree with the log beside it. Ended rows
        // are exempt from the index, which is what lets a savegame accumulate a history of them.
        builder.HasIndex(x => new { x.RepoId, x.SavegameId })
            .IsUnique()
            .HasFilter("\"EndedAt\" IS NULL");

        // Reads one savegame's claims newest first, which is half of the timeline the detail pane
        // renders. Without it that read scans every claim in the system to show one save's.
        builder.HasIndex(x => new { x.RepoId, x.SavegameId, x.TakenAt });
    }
}
