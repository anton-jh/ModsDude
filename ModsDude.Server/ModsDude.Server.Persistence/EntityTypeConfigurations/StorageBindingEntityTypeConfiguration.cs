using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;

internal class StorageBindingEntityTypeConfiguration : IEntityTypeConfiguration<StorageBinding>
{
    public void Configure(EntityTypeBuilder<StorageBinding> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.ToTable(x => x.HasCheckConstraint("CK_StorageBindings_Singleton", $"\"Id\" = {StorageBinding.SingletonId}"));
    }
}
