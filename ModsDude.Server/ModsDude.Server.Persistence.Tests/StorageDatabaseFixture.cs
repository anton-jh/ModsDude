using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Server.Persistence.DbContexts;
using Npgsql;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// A database of its own for storage maintenance, which reads every row in the database and keeps a
/// singleton binding: in the shared database every other test's mod versions would be blobs it finds
/// missing. Emptied before each test.
/// </summary>
public class StorageDatabaseFixture : IAsyncLifetime
{
    private ServiceProvider _services = null!;
    private IDbContextFactory<ApplicationDbContext> _dbContextFactory = null!;


    public string ConnectionString { get; } = new NpgsqlConnectionStringBuilder(DatabaseFixture.ConnectionString)
    {
        Database = "modsdude-tests-storage"
    }.ConnectionString;


    public async Task InitializeAsync()
    {
        _services = new ServiceCollection()
            .AddDbContextFactory<ApplicationDbContext>(options => options.UseNpgsql(ConnectionString))
            .BuildServiceProvider();

        _dbContextFactory = _services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        using var dbContext = CreateDbContext();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        using var dbContext = CreateDbContext();

        await dbContext.Database.EnsureDeletedAsync();

        await _services.DisposeAsync();
    }

    public ApplicationDbContext CreateDbContext() => _dbContextFactory.CreateDbContext();

    /// <summary>Empties every table the tests write to. Truncating fires no delete trigger.</summary>
    public async Task ResetAsync()
    {
        using var dbContext = CreateDbContext();

        await dbContext.Database.ExecuteSqlRawAsync("""
            TRUNCATE "Repos", "Users", "StorageBindings", "BlobDeletionCandidates", "DeletedBlobs",
                "StorageAuditFindings", "StorageMaintenanceStatuses" RESTART IDENTITY CASCADE
            """);
    }
}
