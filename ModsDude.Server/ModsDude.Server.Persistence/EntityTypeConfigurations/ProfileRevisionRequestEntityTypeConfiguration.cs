using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Profiles;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ProfileRevisionRequestEntityTypeConfiguration : IEntityTypeConfiguration<ProfileRevisionRequest>
{
    public void Configure(EntityTypeBuilder<ProfileRevisionRequest> builder)
    {
        // One row per person per profile: a newer request replaces the row.
        builder.HasKey(x => new { x.RepoId, x.ProfileId, x.UserId });

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.ProfileId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
