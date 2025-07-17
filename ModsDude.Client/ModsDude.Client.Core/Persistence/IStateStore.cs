namespace ModsDude.Client.Core.Persistence;

public interface IStateStore
{
    T? Get<T>(string key) where T : class;
    void Save();
    void Set<T>(string key, T value) where T : class;
}
