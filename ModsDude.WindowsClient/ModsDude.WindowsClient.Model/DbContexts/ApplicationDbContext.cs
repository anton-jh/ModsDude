using Microsoft.EntityFrameworkCore;
using ModsDude.WindowsClient.Model.Helpers;
using ModsDude.WindowsClient.Model.Models;

namespace ModsDude.WindowsClient.Model.DbContexts;
public class ApplicationDbContext
    : DbContext
{
    private const string _dbFilename = "db.sqlite";


    public required DbSet<LocalInstance> LocalInstances { get; init; }


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"DataSource={Path.Combine(FileSystemHelper.GetAppDataDirectory(), _dbFilename)}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
