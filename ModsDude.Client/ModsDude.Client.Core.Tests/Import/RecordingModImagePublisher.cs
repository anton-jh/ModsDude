using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests.Import;

internal sealed class RecordingModImagePublisher : IModImagePublisher
{
    private readonly Lock _lock = new();
    private readonly List<ModVersionIdentity> _published = [];


    public Func<ModVersionIdentity, LocalMod, Task>? OnPublish { get; set; }

    public IReadOnlyList<ModVersionIdentity> Published
    {
        get
        {
            lock (_lock)
            {
                return [.. _published];
            }
        }
    }


    public async Task PublishAsync(Guid repoId, ModKey modId, ModVersionKey versionId, LocalMod mod, CancellationToken cancellationToken)
    {
        var identity = new ModVersionIdentity(modId, versionId);

        if (OnPublish is not null)
        {
            await OnPublish(identity, mod);
        }

        lock (_lock)
        {
            _published.Add(identity);
        }
    }
}
