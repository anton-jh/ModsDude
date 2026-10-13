using ModsDude.Client.Core.Disks;

namespace ModsDude.Client.Core.Tests.Disks;

internal sealed class FakeDiskSpace : IDiskSpace
{
    private readonly Dictionary<string, long?> _volumes = new(StringComparer.OrdinalIgnoreCase);


    /// <summary>What every volume not set on its own has free. Null for a volume that cannot be read.</summary>
    public long? Available { get; set; } = 1L << 50;

    public List<string> Asked { get; } = [];


    public void Set(string volumeRoot, long? available) => _volumes[volumeRoot] = available;

    public long? GetAvailableBytes(string volumeRoot)
    {
        Asked.Add(volumeRoot);

        return _volumes.TryGetValue(volumeRoot, out var available) ? available : Available;
    }
}
