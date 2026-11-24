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

        // Every change moves one of these, so a run advancing the generation and an admin rebinding at
        // the same time cannot both commit.
        builder.Property(x => x.BindingId).IsConcurrencyToken();
        builder.Property(x => x.StorageAccount).HasMaxLength(255).IsConcurrencyToken();
        builder.Property(x => x.Generation).IsConcurrencyToken();
        builder.Property(x => x.PendingGeneration).IsConcurrencyToken();

        builder.ToTable(x =>
        {
            x.HasCheckConstraint("CK_StorageBindings_Singleton", $"\"Id\" = {StorageBinding.SingletonId}");
            x.HasCheckConstraint("CK_StorageBindings_GenerationNotNegative", "\"Generation\" >= 0");
            x.HasCheckConstraint("CK_StorageBindings_PendingIsNext", "\"PendingGeneration\" IS NULL OR \"PendingGeneration\" = \"Generation\" + 1");
            x.HasCheckConstraint("CK_StorageBindings_PendingHasAdvanceId", "(\"PendingGeneration\" IS NULL) = (\"PendingAdvanceId\" IS NULL)");
        });
    }
}
