using Microsoft.Extensions.DependencyInjection;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>A fresh instance on every call, for a page that takes nothing but services.</summary>
public interface IFactory<out T>
{
    T Create();
}


public static class FactoryServiceCollectionExtensions
{
    public static IServiceCollection AddFactory<T>(this IServiceCollection services)
        where T : class
    {
        services.AddTransient<T>();
        services.AddSingleton<IFactory<T>>(sp => new ServiceFactory<T>(sp));

        return services;
    }


    private sealed class ServiceFactory<T>(IServiceProvider services) : IFactory<T>
        where T : notnull
    {
        public T Create() => services.GetRequiredService<T>();
    }
}
