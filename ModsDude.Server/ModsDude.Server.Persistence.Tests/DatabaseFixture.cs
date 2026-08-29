using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// A real PostgreSQL database, migrated from the same migrations the API runs. These tests cover
/// behaviour the database decides rather than the model, so an in-memory or SQLite substitute would
/// answer for itself instead of for PostgreSQL — which is the whole point of them.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// Set <c>MODSDUDE_TEST_DATABASE</c> to point the suite at another server; CI supplies the
    /// connection string of its PostgreSQL service container this way. The default targets the
    /// local development server, on a database of its own so that a run cannot take the development
    /// database with it — <see cref="InitializeAsync"/> drops and recreates it every time.
    /// </summary>
    public const string ConnectionStringEnvironmentVariable = "MODSDUDE_TEST_DATABASE";

    private const string _defaultConnectionString =
        "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=modsdude-tests";


    private ServiceProvider _services = null!;
    private IDbContextFactory<ApplicationDbContext> _dbContextFactory = null!;


    public static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : _defaultConnectionString;


    public async Task InitializeAsync()
    {
        _services = new ServiceCollection()
            .AddDbContextFactory<ApplicationDbContext>(options => options.UseNpgsql(ConnectionString))
            .BuildServiceProvider();

        _dbContextFactory = _services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        using var dbContext = _dbContextFactory.CreateDbContext();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
    }

    /// <summary>
    /// A context with its own change tracker, so a test can write through one and read back through
    /// another without the first one's identity map answering the question.
    /// </summary>
    public ApplicationDbContext CreateDbContext()
    {
        return _dbContextFactory.CreateDbContext();
    }
}


[CollectionDefinition(nameof(DatabaseCollection))]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
