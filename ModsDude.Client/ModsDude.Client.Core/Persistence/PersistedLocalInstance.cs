using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Persistence;

public class PersistedLocalInstance
{
    public required Guid Id { get; init; }

    /// <summary>The game this instance belongs to. Every repo with the same scope offers it.</summary>
    public required InstanceScope Scope { get; init; }

    /// <summary>
    /// Which adapter version authored <see cref="AdapterInstanceSettings"/>. Not part of the scope,
    /// so a repo on a newer compatibility version still offers this instance and has to be able to
    /// read the older settings.
    /// </summary>
    public required GameAdapterId GameAdapterId { get; init; }

    public required string Name { get; set; }
    public required string AdapterInstanceSettings { get; set; }

    /// <summary>
    /// The folder the adapter says this instance owns, recorded so the ownership check can run
    /// across every scope. An instance whose scope has no repo on this machine cannot hydrate its
    /// adapter, and it still owns its folder.
    /// </summary>
    public string? ModFolder { get; set; }

    public ActiveProfile? ActiveProfile { get; set; }
}
