using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace ModsDude.Client.Core.Utilities;

public interface IFactory<T>
{
    T Create();
}

public class GenericFactory<T>(Func<T> factory) : IFactory<T>
{
    public T Create()
    {
        return factory();
    }
}

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

    public static IServiceCollection AddFactory(this IServiceCollection services, Type serviceType)
    {
        services.AddTransient(serviceType);

        var factoryInterfaceType = typeof(IFactory<>).MakeGenericType(serviceType);
        var genericFactoryType = typeof(GenericFactory<>).MakeGenericType(serviceType);

        services.AddTransient(factoryInterfaceType, serviceProvider =>
        {
            var factoryFuncType = typeof(Func<>).MakeGenericType(serviceType);
            var func = Delegate.CreateDelegate(
                factoryFuncType,
                serviceProvider,
                typeof(ServiceProviderServiceExtensions)
                    .GetMethod(nameof(ServiceProviderServiceExtensions.GetRequiredService), [typeof(IServiceProvider)])!
                    .MakeGenericMethod(serviceType)
            );

            return Activator.CreateInstance(genericFactoryType, func)!;
        });

        return services;
    }

    public static IServiceCollection AddFactory<T>(this IServiceCollection services, Func<T> factory)
    {
        services.AddTransient<IFactory<T>>(_ => new GenericFactory<T>(factory));
        return services;
    }

    public static IServiceCollection AddFactory(this IServiceCollection services, Type serviceType, Func<object> factory)
    {
        var factoryInterfaceType = typeof(IFactory<>).MakeGenericType(serviceType);
        var genericFactoryType = typeof(GenericFactory<>).MakeGenericType(serviceType);

        services.AddTransient(factoryInterfaceType, _ => Activator.CreateInstance(serviceType, [factory])!);
        return services;
    }

    public static IServiceCollection AddFactory<T>(this IServiceCollection services, Func<IServiceProvider, T> factory)
    {
        services.AddTransient<IFactory<T>>(sp => new GenericFactory<T>(() => factory(sp)));
        return services;
    }
}
