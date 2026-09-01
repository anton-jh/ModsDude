using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class UserEntityTypeConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id);

        // Not unique, and never was allowed to become the thing that made a user unique: two people
        // called Anton keep the name each of them chose, and a list showing both of them says which
        // is which with their tag.
        builder.Property(x => x.DisplayName);

        builder.HasMany(x => x.RepoMemberships).WithOne().HasForeignKey(x => x.UserId);
        builder.Navigation(x => x.RepoMemberships).AutoInclude();
    }
}
