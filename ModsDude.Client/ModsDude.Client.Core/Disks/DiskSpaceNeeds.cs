using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Disks;

/// <summary>The bytes a piece of work is about to write, per volume.</summary>
public sealed class DiskSpaceNeeds
{
    private readonly Dictionary<string, long> _bytes = new(StringComparer.OrdinalIgnoreCase);


    public static DiskSpaceNeeds For(string path, long bytes)
    {
        var needs = new DiskSpaceNeeds();

        needs.Add(path, bytes);

        return needs;
    }

    /// <summary>Counts <paramref name="bytes"/> against the volume <paramref name="path"/> is on.</summary>
    public void Add(string path, long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        var volume = FileSystemHelper.NormalizeVolumeRoot(path);

        _bytes[volume] = _bytes.GetValueOrDefault(volume) + bytes;
    }

    public IReadOnlyList<DiskSpaceNeed> ByVolume
        => [.. _bytes
            .Select(x => new DiskSpaceNeed(x.Key, x.Value))
            .OrderBy(x => x.VolumeRoot, StringComparer.OrdinalIgnoreCase)];
}


public sealed record DiskSpaceNeed(string VolumeRoot, long Bytes);
