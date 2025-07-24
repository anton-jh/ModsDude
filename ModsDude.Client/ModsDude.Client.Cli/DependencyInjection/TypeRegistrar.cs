using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModsDude.Client.Core.Utilities;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.DependencyInjection;

/// <summary>
/// Credit to Stevan Freeborn
/// https://github.com/StevanFreeborn/SpectreHostExample
/// </summary>
internal sealed class TypeRegistrar(IHostBuilder builder) : ITypeRegistrar
{
    private readonly IHostBuilder _builder = builder;

    public ITypeResolver Build()
    {
        return new TypeResolver(_builder.Build());
    }

    public void Register(Type service, Type implementation)
    {
        _builder.ConfigureServices((_, services) =>
        {
            services.AddTransient(service, implementation);
            services.AddFactory(service);
        });
    }

    public void RegisterInstance(Type service, object implementation)
    {
        _builder.ConfigureServices((_, services) => services.AddSingleton(service, implementation));
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        _builder.ConfigureServices((_, services) =>
        {
            services.AddTransient(service, _ => factory());
            services.AddFactory(service, factory);
        });
    }
}
