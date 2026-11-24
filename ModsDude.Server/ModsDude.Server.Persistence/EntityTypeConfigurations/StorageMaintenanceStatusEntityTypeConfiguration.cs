using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;

internal class StorageMaintenanceStatusEntityTypeConfiguration : IEntityTypeConfiguration<StorageMaintenanceStatus>
{
    public void Configure(EntityTypeBuilder<StorageMaintenanceStatus> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.ToTable(x => x.HasCheckConstraint("CK_StorageMaintenanceStatus_Singleton", $"\"Id\" = {StorageMaintenanceStatus.SingletonId}"));
    }
}
