using ModsDude.Client.Core.Helpers;
using System.Text.Json;

namespace ModsDude.Client.Core.Persistence;
internal class StateStore : IStateStore
{
    private const int _currentVersion = 1;
    private const string _filename = "state.json";

    private readonly static string _filepath = Path.Combine(FileSystemHelper.GetAppDataDirectory(), _filename);
    private readonly static JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

    private readonly State _state;
    private readonly Dictionary<string, object?> _cache = [];


    public StateStore()
    {
        if (File.Exists(_filepath))
        {
            var raw = File.ReadAllText(_filepath);
            try
            {
                _state = JsonSerializer.Deserialize<State>(raw) ?? new(_currentVersion);
            }
            catch (JsonException)
            {
                _state = new(_currentVersion);
                File.Move(_filepath, Path.Combine(FileSystemHelper.GetAppDataDirectory(), $"state_corrupted_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.json"));
            }
        }
        else
        {
            _state = new(_currentVersion);
        }
    }


    public T? Get<T>(string key)
        where T : class
    {
        if (_cache.TryGetValue(key, out var fromCache))
        {
            return fromCache as T;
        }

        if (_state.Misc.TryGetValue(key, out var node))
        {
            var fromJson = JsonSerializer.Deserialize<T>(node);
            _cache[key] = fromJson;

            return fromJson;
        }

        return null;
    }

    public void Set<T>(string key, T value)
        where T : class
    {
        var node = JsonSerializer.SerializeToNode(value);
        _state.Misc[key] = node!;
        _cache[key] = value;

        Save();
    }

    public void Save()
    {
        var path = _filepath;

        File.WriteAllText(path, JsonSerializer.Serialize(_state, _serializerOptions));
    }
}
