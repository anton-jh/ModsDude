using ModsDude.Client.Core.Helpers;
using System.Text.Json;

namespace ModsDude.Client.Core.Persistence;

public interface IStateStore<T>
    where T : class, IState<T>
{
    T Get();
    void Save();
}

internal class StateStore<T> : IStateStore<T>
    where T : class, IState<T>
{
    private const int _currentVersion = 1;
    private const string _filename = "state.json";

    private readonly static string _filepath = Path.Combine(FileSystemHelper.GetAppDataDirectory(), _filename);
    private readonly static JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

    private T? _state = null;


    public T Get()
    {
        if (_state is null)
        {
            if (File.Exists(_filepath))
            {
                var raw = File.ReadAllText(_filepath);
                try
                {
                    _state = JsonSerializer.Deserialize<T>(raw) ?? T.Create(_currentVersion);
                }
                catch (JsonException)
                {
                    _state = T.Create(_currentVersion);
                    File.Move(_filepath, Path.Combine(FileSystemHelper.GetAppDataDirectory(), $"state_corrupted_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.json"));
                }
            }
            else
            {
                _state = T.Create(_currentVersion);
            }
        }

        return _state;
    }

    public void Save()
    {
        var path = _filepath;

        File.WriteAllText(path, JsonSerializer.Serialize(_state, _serializerOptions));
    }
}
