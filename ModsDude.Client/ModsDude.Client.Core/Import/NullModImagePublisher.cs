using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Import;

/// <summary>
/// What import runs against when nothing has registered a real publisher. Import has to work without
/// imagery - the mod file is the thing that matters and the pictures are decoration - so the absence
/// of one is a registration, not an error.
/// </summary>
public sealed class NullModImagePublisher : IModImagePublisher
{
    public Task PublishAsync(Guid repoId, ModKey modId, ModVersionKey versionId, LocalMod mod, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
