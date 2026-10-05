using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters.DynamicForms;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

public class BeamNgLocalGameAdapter(
    EmptyAdapterSettings baseSettings,
    BeamNgLocalSettings localSettings,
    ILoggerFactory loggerFactory)
    : BeamNgBaseGameAdapter(baseSettings, loggerFactory), ILocalGameAdapter
{
    // Typed as Func<TCapability> rather than Func<object>, which is what the lookup matches on.
    private readonly List<object> _capabilities = [
        new Func<ILocalModAdapter>(() => new BeamNgLocalModAdapter(localSettings, loggerFactory))
        ];


    public DynamicForm LocalSettings { get; } = localSettings;


    public Func<T>? GetLocalCapabilityAdapterFactory<T>()
    {
        return _capabilities.OfType<Func<T>>().SingleOrDefault();
    }
}
