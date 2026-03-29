using System.ComponentModel;

namespace ModsDude.Client.Core.Models;

public class LocalInstance
    : INotifyPropertyChanged
{
    public LocalInstance(Guid repoId, string name, string adapterInstanceSettings)
    {
        RepoId = repoId;
        Name = name;
        AdapterInstanceSettings = adapterInstanceSettings;
    }


    public event PropertyChangedEventHandler? PropertyChanged;


    public Guid RepoId { get; }

    public string Name
    {
        get => field;
        set
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public string AdapterInstanceSettings { get; internal set; }
}
