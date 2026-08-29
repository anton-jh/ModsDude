using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.ModsDudeServer;

namespace ModsDude.Client.Core.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCore<TAccessTokenAccessor>(this IServiceCollection services, string serverBaseUrl)
        where TAccessTokenAccessor : IAccessTokenAccessor
    {
        services.AddSingleton<IAccessTokenAccessor>(sp => sp.GetRequiredService<TAccessTokenAccessor>());
        services.AddModsDudeClient(serverBaseUrl);
        services.AddGameAdapters(typeof(IGameAdapter).Assembly);

        // Its own HttpClient: it talks to blob storage over a SAS, not to the API, and must not
        // carry the access token the generated clients attach.
        services.AddHttpClient<IModFileUploader, BlockBlobModFileUploader>();

        // Only if nothing else has: a client that can decode mod archives registers a real
        // publisher, and this is called after the app has composed its own services.
        services.TryAddSingleton<IModImagePublisher, NullModImagePublisher>();
        services.AddSingleton<ModImportService>();

        return services;
    }
}
