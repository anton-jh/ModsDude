namespace ModsDude.Client.Core.Models;

public class LocalInstance
{
    public LocalInstance(Guid repoId, string name, string adapterInstanceSettings)
    {
        RepoId = repoId;
        Name = name;
        AdapterInstanceSettings = adapterInstanceSettings;
    }


    public Guid RepoId { get; }
    public string Name { get; internal set; }
    public string AdapterInstanceSettings { get; internal set; }
}
