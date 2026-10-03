using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class TrustCodeEntityTypeConfiguration : IEntityTypeConfiguration<TrustCode>
{
    public void Configure(EntityTypeBuilder<TrustCode> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.RequestId).IsUnique();

        // One code per user: a second one has nothing left to grant.
        builder.HasIndex(x => x.RedeemedBy).IsUnique();

        // Two users redeeming one code at the same moment: the second write loses instead of both
        // becoming trusted.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.RedeemedBy).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(x =>
        {
            x.HasCheckConstraint(
                "CK_TrustCodes_RedeemedByAndAtTogether",
                "(\"RedeemedBy\" IS NULL) = (\"RedeemedAt\" IS NULL)");
            x.HasCheckConstraint(
                "CK_TrustCodes_NotRedeemedAndRevoked",
                "\"RedeemedAt\" IS NULL OR \"RevokedAt\" IS NULL");
        });
    }
}
