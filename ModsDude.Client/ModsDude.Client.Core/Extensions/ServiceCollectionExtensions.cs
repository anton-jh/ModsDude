using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.ModsDudeServer;
using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        services.AddModsDudeClient();
        services.AddGameAdapters(typeof(IGameAdapter).Assembly);
        services.AddSingleton<IStateStore, StateStore>();

        return services;
    }
}
