using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.Changes;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ModVersionDeletionEntityTypeConfiguration : IEntityTypeConfiguration<ModVersionDeletion>
{
    public void Configure(EntityTypeBuilder<ModVersionDeletion> builder)
    {
        // One change number is one change, so it is the key within a repo.
        builder.HasKey(x => new { x.RepoId, x.ChangeSequence });
        builder.HasOne<Repo>().WithMany().HasForeignKey(x => x.RepoId).OnDelete(DeleteBehavior.Cascade);
    }
}
