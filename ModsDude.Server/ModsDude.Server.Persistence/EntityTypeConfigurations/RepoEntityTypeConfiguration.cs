using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using System.Reflection;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class RepoEntityTypeConfiguration : IEntityTypeConfiguration<Repo>
{
    internal const string MembershipsField = "_memberships";


    public void Configure(EntityTypeBuilder<Repo> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(NameRule.MaximumLength);
        builder.ComplexProperty(x => x.AdapterData);
        builder.Property(x => x.Created);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.MembersVersion).IsConcurrencyToken();

        // No index on the name, unique or otherwise. Nothing looks a repo up by name - the only way
        // into one is an invite code - so the name is display text, and two repos called the same
        // are simply two repos called the same. RepoTag is what tells them apart on screen.

        if (typeof(Repo).GetField(MembershipsField, BindingFlags.NonPublic | BindingFlags.Instance) is null)
        {
            // Has to throw here as we do NOT want EF to create a shadow property if the field does not exist
            throw new Exception($"{nameof(Repo)} does not have a field called {MembershipsField}");
        }
        builder.HasMany<RepoMembership>(MembershipsField).WithOne().HasForeignKey(x => x.RepoId);
        builder.Navigation(MembershipsField).AutoInclude();
    }
}
