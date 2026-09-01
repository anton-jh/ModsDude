using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.ValueConverters;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class RepoInviteEntityTypeConfiguration : IEntityTypeConfiguration<RepoInvite>
{
    public void Configure(EntityTypeBuilder<RepoInvite> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code);
        builder.HasIndex(x => x.Code).IsUnique();

        builder.Property(x => x.GrantedLevel)
            .HasConversion<RepoMembershipLevelValueConverter>();

        // Two people redeeming the last use of a capped invite at the same moment would otherwise
        // both read Uses, both write Uses + 1, and both get in. The second write loses instead.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<Repo>().WithMany().HasForeignKey(x => x.RepoId).OnDelete(DeleteBehavior.Cascade);
    }
}
