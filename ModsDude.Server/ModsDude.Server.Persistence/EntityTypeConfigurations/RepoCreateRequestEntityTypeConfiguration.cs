using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;

internal class RepoCreateRequestEntityTypeConfiguration : IEntityTypeConfiguration<RepoCreateRequest>
{
    public void Configure(EntityTypeBuilder<RepoCreateRequest> builder)
    {
        // Scoped to the person: somebody else sending the same ID is not repeating this request.
        builder.HasKey(x => new { x.UserId, x.RequestId });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Repo>()
            .WithMany()
            .HasForeignKey(x => x.RepoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
