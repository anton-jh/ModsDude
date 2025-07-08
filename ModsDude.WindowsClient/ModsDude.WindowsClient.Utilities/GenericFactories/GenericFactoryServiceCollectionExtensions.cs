using Microsoft.Extensions.DependencyInjection;

namespace ModsDude.WindowsClient.Utilities.GenericFactories;

public static class GenericFactoryServiceCollectionExtensions
{
    public static IServiceCollection AddFactory<T>(this IServiceCollection services)
        where T : class
    {
        services.AddTransient<T>();

        services.AddTransient<IFactory<T>>(
            sp => new GenericFactory<T>(
                () => sp.GetRequiredService<T>()));

        return services;
    }

    public static IServiceCollection AddFactory<T>(this IServiceCollection services, Func<T> factory)
    {
        services.AddTransient<IFactory<T>>(_ => new GenericFactory<T>(factory));
        return services;
    }

    public static IServiceCollection AddFactory<T>(this IServiceCollection services, Func<IServiceProvider, T> factory)
    {
        services.AddTransient<IFactory<T>>(sp => new GenericFactory<T>(() => factory(sp)));
        return services;
    }
}
