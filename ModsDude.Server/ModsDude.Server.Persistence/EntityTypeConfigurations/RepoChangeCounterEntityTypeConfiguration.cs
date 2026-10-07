using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.Changes;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class RepoChangeCounterEntityTypeConfiguration : IEntityTypeConfiguration<RepoChangeCounter>
{
    public void Configure(EntityTypeBuilder<RepoChangeCounter> builder)
    {
        builder.HasKey(x => x.RepoId);
        builder.HasOne<Repo>().WithOne().HasForeignKey<RepoChangeCounter>(x => x.RepoId).OnDelete(DeleteBehavior.Cascade);
    }
}
