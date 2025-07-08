using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.WindowsClient.Model.Models;

namespace ModsDude.WindowsClient.Model.EntityTypeConfigurations;
internal class LocalInstanceEntityTypeConfiguration : IEntityTypeConfiguration<LocalInstance>
{
    public void Configure(EntityTypeBuilder<LocalInstance> builder)
    {
        builder.HasKey(x => new { x.Id, x.UserId, x.RepoId });
    }
}
