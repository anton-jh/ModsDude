using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace ModsDude.WindowsClient.Model.GameAdapters;
public static class GameAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddGameAdapters(this IServiceCollection services, Assembly assembly)
    {
        var types = assembly.GetTypes()
            .Where(x => !x.IsAbstract && x.IsAssignableTo(typeof(IGameAdapter)));

        foreach (var type in types)
        {
            services.AddSingleton(typeof(IGameAdapter), type);
        }

        services.AddSingleton<GameAdapterRegistry>();

        return services;
    }
}
