using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Server.Application.Dependencies;

namespace ModsDude.Server.ModHub.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddModHub(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ModHubOptions>(configuration.GetSection(ModHubOptions.SectionName));

        var options = configuration.GetSection(ModHubOptions.SectionName).Get<ModHubOptions>() ?? new ModHubOptions();

        services.AddHttpClient(ModHubSite.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IModHubSite, ModHubSite>();

        return services;
    }
}
