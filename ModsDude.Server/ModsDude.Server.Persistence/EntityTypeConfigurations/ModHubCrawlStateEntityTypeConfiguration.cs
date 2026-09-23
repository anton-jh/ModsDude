using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.ModHub;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ModHubCrawlStateEntityTypeConfiguration : IEntityTypeConfiguration<ModHubCrawlState>
{
    public void Configure(EntityTypeBuilder<ModHubCrawlState> builder)
    {
        builder.HasKey(x => x.Game);

        builder.Ignore(x => x.CurrentAsOf);
    }
}
