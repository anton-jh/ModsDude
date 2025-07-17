using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.ModsDudeServer;

namespace ModsDude.Client.Core.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCore<TAccessTokenAccessor>(this IServiceCollection services)
        where TAccessTokenAccessor : IAccessTokenAccessor
    {
        services.AddSingleton<IAccessTokenAccessor>(sp => sp.GetRequiredService<TAccessTokenAccessor>());
        services.AddModsDudeClient();
        services.AddGameAdapters(typeof(IGameAdapter).Assembly);

        return services;
    }
}
