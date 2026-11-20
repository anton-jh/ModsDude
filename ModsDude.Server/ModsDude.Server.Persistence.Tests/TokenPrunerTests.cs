using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Persistence.Accounts;
using ModsDude.Server.Persistence.DbContexts;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ModsDude.Server.Persistence.Tests;

[Collection(nameof(DatabaseCollection))]
public class TokenPrunerTests : IAsyncLifetime
{
    private static readonly DateTime _now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ServiceProvider _services = new ServiceCollection()
        .AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(DatabaseFixture.ConnectionString))
        .AddSingleton<ITimeService>(new FixedTime(_now))
        .AddScoped<ITokenPruner, TokenPruner>()
        .AddOpenIddict()
        .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>())
        .Services
        .BuildServiceProvider();


    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _services.DisposeAsync();


    [Fact]
    public async Task Old_revoked_tokens_are_deleted_and_the_rest_kept()
    {
        var oldRevoked = await GivenAToken(Statuses.Revoked, created: _now - TokenPruner.MinimumAge - TimeSpan.FromDays(1));
        var recentRevoked = await GivenAToken(Statuses.Revoked, created: _now - TimeSpan.FromDays(1));
        var oldValid = await GivenAToken(Statuses.Valid, created: _now - TokenPruner.MinimumAge - TimeSpan.FromDays(1), expires: _now.AddYears(50));

        await PruneAsync();

        Assert.False(await TokenExists(oldRevoked));
        Assert.True(await TokenExists(recentRevoked));
        Assert.True(await TokenExists(oldValid));
    }

    [Fact]
    public async Task Pruning_again_deletes_nothing_more()
    {
        var oldRevoked = await GivenAToken(Statuses.Revoked, created: _now - TokenPruner.MinimumAge - TimeSpan.FromDays(1));
        var recentRevoked = await GivenAToken(Statuses.Revoked, created: _now - TimeSpan.FromDays(1));

        await PruneAsync();
        var second = await PruneAsync();

        Assert.Equal(0, second.Tokens);
        Assert.False(await TokenExists(oldRevoked));
        Assert.True(await TokenExists(recentRevoked));
    }


    private async Task<TokenPruneResult> PruneAsync()
    {
        using var scope = _services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ITokenPruner>().PruneAsync(CancellationToken.None);
    }

    private async Task<string> GivenAToken(string status, DateTime created, DateTime? expires = null)
    {
        using var scope = _services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();

        var token = await tokens.CreateAsync(new OpenIddictTokenDescriptor
        {
            Status = status,
            Subject = Guid.NewGuid().ToString(),
            Type = TokenTypeHints.RefreshToken,
            CreationDate = new DateTimeOffset(created),
            ExpirationDate = expires is DateTime expiry ? new DateTimeOffset(expiry) : null
        });

        return (await tokens.GetIdAsync(token))!;
    }

    private async Task<bool> TokenExists(string id)
    {
        using var scope = _services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>().FindByIdAsync(id) is not null;
    }


    private sealed class FixedTime(DateTime now) : ITimeService
    {
        public DateTime Now() => now;
    }
}
