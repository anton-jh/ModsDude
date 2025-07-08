using Microsoft.Extensions.Hosting;
using ModsDude.Cli.Commands;
using Spectre.Console.Cli;

namespace ModsDude.Cli.DependencyInjection;

/// <summary>
/// Credit to Stevan Freeborn
/// https://github.com/StevanFreeborn/SpectreHostExample
/// </summary>
internal static class HostBuilderExtensions
{
    public static CommandApp<GreetingCommand> BuildApp(this IHostBuilder builder)
    {
        TypeRegistrar registrar = new(builder);
        CommandApp<GreetingCommand> app = new(registrar);
        return app;
    }
}
