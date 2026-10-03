using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.ModsDudeServer;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCore<TAccessTokenAccessor>(this IServiceCollection services, string serverBaseUrl)
        where TAccessTokenAccessor : IAccessTokenAccessor
    {
        services.AddSingleton<IAccessTokenAccessor>(sp => sp.GetRequiredService<TAccessTokenAccessor>());
        services.AddModsDudeClient(serverBaseUrl);
        services.AddGameAdapters(typeof(IGameAdapter).Assembly);
        services.AddSingleton(TimeProvider.System);

        // Their own HttpClients: they talk to blob storage over a SAS, not to the API, and must not
        // carry the access token the generated clients attach.
        services.AddHttpClient<IModFileUploader, BlockBlobModFileUploader>();
        services.AddHttpClient<IModFileDownloader, HttpModFileDownloader>();

        // One per process, so every transfer in a direction shares its limit. Read from settings once;
        // the settings page applies a change to this same object rather than to a copy.
        services.AddSingleton(sp => Transfers.TransferLimits.From(
            sp.GetRequiredService<Services.IClientSettingsRepository>().Read(x => x.Transfers),
            sp.GetRequiredService<TimeProvider>()));

        // Only if nothing else has: a client that can decode mod archives registers a real
        // publisher, and this is called after the app has composed its own services.
        services.TryAddSingleton<IModImagePublisher, NullModImagePublisher>();
        services.AddSingleton<IModImportService, ModImportService>();

        services.AddSingleton<IContentStoreProvider, ContentStoreProvider>();
        services.AddSingleton<IRecycleBin, ShellRecycleBin>();
        services.AddSingleton<ISyncManifestStore, SyncManifestStore>();
        services.AddSingleton<IDriftService, DriftService>();
        services.AddSingleton<IStoreIntegrityService, StoreIntegrityService>();
        services.AddSingleton<IModSyncService, ModSyncService>();
        services.AddSingleton<GameFiles.IGameFileEditor, GameFiles.GameFileEditor>();
        services.AddSingleton<GameProcesses.IGameRunningGuard, GameProcesses.GameRunningGuard>();
        services.AddSingleton<IContentStoreMaintenance, ContentStoreMaintenance>();
        services.AddSingleton<Savegames.ISavegamePacker, Savegames.SavegamePacker>();

        // StateStore itself is registered by the host (App.xaml.cs), so this only names the seam the
        // binding store reads it through - which exists so a test can reach persisted state without
        // rewriting the developer's own state.json.
        services.AddSingleton<Savegames.IPersistedGameState, Savegames.StateStoreGameState>();
        services.AddSingleton<Savegames.ISavegameBindingStore, Savegames.SavegameBindingStore>();
        services.AddSingleton<Savegames.ISavegamePendingPublishes, Savegames.SavegamePendingPublishes>();

        // TryAdd so a host that knows its repos by another route keeps its own.
        services.TryAddSingleton<Savegames.ILocalSavegameAdapters, Savegames.RepoSavegameAdapters>();
        services.AddSingleton<Savegames.ISavegameSlots, Savegames.SavegameSlots>();
        services.AddSingleton<Savegames.IHeldSlotReader, Savegames.HeldSlotReader>();
        services.AddSingleton<Savegames.IHeldSavegames, Savegames.HeldSavegames>();
        services.AddSingleton<Savegames.ISavegamePlayAttribution, Savegames.SavegamePlayAttribution>();
        services.AddSingleton<Savegames.ISavegameDriftCheck, Savegames.SavegameDriftCheck>();
        services.AddSingleton<Savegames.ISavegameRecycler, Savegames.SavegameRecycler>();
        services.AddSingleton<Savegames.ISavegameTransfer, Savegames.SavegameTransfer>();
        services.AddSingleton<Savegames.ISavegameRenamer, Savegames.SavegameRenamer>();
        services.AddSingleton<Savegames.ISavegameHolds, Savegames.SavegameHolds>();
        services.AddSingleton<Savegames.ISavegameCheckOut, Savegames.SavegameCheckOut>();
        services.AddSingleton<Savegames.ISavegameCheckIn, Savegames.SavegameCheckIn>();
        services.AddSingleton<Savegames.ISavegamePublisher, Savegames.SavegamePublisher>();
        services.AddSingleton<Savegames.ISavegameCompatibilityCheck, Savegames.SavegameCompatibilityCheck>();

        // One per app: the drift answer is app-level, and every view reads the same one.
        services.AddSingleton<IDriftMonitor, DriftMonitor>();

        return services;
    }
}
