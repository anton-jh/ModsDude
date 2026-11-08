using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Persistence.ValueConverters;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class RepoMembershipEntityTypeConfiguration : IEntityTypeConfiguration<RepoMembership>
{
    public void Configure(EntityTypeBuilder<RepoMembership> builder)
    {
        builder.HasKey(x => new { x.UserId, x.RepoId });

        builder.Property(x => x.Level)
            .HasConversion<RepoMembershipLevelValueConverter>();

        builder.ToTable(x => x.HasCheckConstraint("CK_RepoMemberships_Level", RepoMembershipLevelValueConverter.CheckConstraintSql("Level")));
    }
}
