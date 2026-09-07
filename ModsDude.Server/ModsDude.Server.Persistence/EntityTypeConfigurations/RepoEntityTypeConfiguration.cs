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

        // Filtered, so an archived repo stops holding its name the moment it is archived and any
        // number of archived ones may share it - they are told apart by when they were archived.
        // The clash is resolved on the way back, by renaming, which is when somebody is present to
        // decide. Same shape as the checkout log's one-open-row index.
        //
        // Unique at all for the first time here: the name has always been documented as globally
        // unique and was only ever checked by the endpoint, so two people creating one name at the
        // same moment both won.
        builder.HasIndex(x => x.Name)
            .IsUnique()
            .HasFilter("\"ArchivedAt\" IS NULL");

        if (typeof(Repo).GetField(_membershipsField, BindingFlags.NonPublic | BindingFlags.Instance) is null)
        {
            // Has to throw here as we do NOT want EF to create a shadow property if the field does not exist
            throw new Exception($"{nameof(Repo)} does not have a field called {_membershipsField}");
        }
        builder.HasMany<RepoMembership>(_membershipsField).WithOne().HasForeignKey(x => x.RepoId);
        builder.Navigation(_membershipsField).AutoInclude();
    }
}
