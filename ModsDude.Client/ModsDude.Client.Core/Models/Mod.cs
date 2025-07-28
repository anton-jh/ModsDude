using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Models;

public class Mod
{
    private readonly List<Version> _olderVersions;


    public Mod(ModDto mod)
    {
        if (mod.Versions.Count == 0)
        {
            throw new ArgumentException("Mod does not have any versions. At least one version is required.", nameof(mod));
        }

        var versions = mod.Versions
            .Select(x => new Version(this, x.VersionId, x.DisplayName, x.SequenceNumber))
            .OrderByDescending(x => x.SequenceNumber).ToList();

        Latest = versions.First();
        _olderVersions = versions.Skip(1).ToList();

        Id = mod.Id;
    }


    public string Id { get; }
    public Version Latest { get; }
    public IEnumerable<Version> AllVersions => _olderVersions.Prepend(Latest);
    public string DisplayName => Latest.DisplayName;


    public class Version(Mod parent, string id, string displayName, int sequenceNumber)
    {
        public Mod Parent { get; } = parent;
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public int SequenceNumber { get; } = sequenceNumber;
    }
}
