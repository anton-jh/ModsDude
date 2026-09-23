using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.ModHub;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ModHubModEntityTypeConfiguration : IEntityTypeConfiguration<ModHubMod>
{
    public void Configure(EntityTypeBuilder<ModHubMod> builder)
    {
        builder.HasKey(x => new { x.Game, x.ModHubId });

        // What a lookup matches on. Not unique: ModHub does not promise that, and a lookup answering
        // with two mods is better than a crawl failing on a constraint.
        builder.HasIndex(x => new { x.Game, x.FileNameKey });

        // The refresh reads the longest-unread mods first.
        builder.HasIndex(x => new { x.Game, x.FetchedAt });
    }
}
