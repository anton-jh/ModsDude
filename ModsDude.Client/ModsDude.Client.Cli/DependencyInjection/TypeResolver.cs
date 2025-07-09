using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console.Cli;

namespace ModsDude.Cli.DependencyInjection;

/// <summary>
/// Credit to Stevan Freeborn
/// https://github.com/StevanFreeborn/SpectreHostExample
/// </summary>
internal sealed class TypeResolver(IHost host) : ITypeResolver, IDisposable
{
    private readonly IHost _host = host;

    public object? Resolve(Type? type)
    {
        return type is not null
            ? _host.Services.GetRequiredService(type)
            : null;
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}