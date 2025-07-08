using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using System.Reflection;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class RepoEntityTypeConfiguration : IEntityTypeConfiguration<Repo>
{
    private const string _membershipsField = "_memberships";


    public void Configure(EntityTypeBuilder<Repo> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name);
        builder.ComplexProperty(x => x.AdapterData);
        builder.Property(x => x.Created);

        if (typeof(Repo).GetField(_membershipsField, BindingFlags.NonPublic | BindingFlags.Instance) is null)
        {
            // Has to throw here as we do NOT want EF to create a shadow property if the field does not exist
            throw new Exception($"{nameof(Repo)} does not have a field called {_membershipsField}");
        }
        builder.HasMany<RepoMembership>(_membershipsField).WithOne().HasForeignKey(x => x.RepoId);
        builder.Navigation(_membershipsField).AutoInclude();
    }
}
