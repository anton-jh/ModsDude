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

        builder.Property(x => x.Username);
        builder.HasIndex(x => x.Username).IsUnique();

        builder.HasMany(x => x.RepoMemberships).WithOne().HasForeignKey(x => x.UserId);
        builder.Navigation(x => x.RepoMemberships).AutoInclude();
    }
}
