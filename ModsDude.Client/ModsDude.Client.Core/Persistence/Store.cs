using ModsDude.Client.Core.Helpers;
using System.Text.Json;

namespace ModsDude.Client.Core.Persistence;

public class Store<T>(string filename)
    where T : class, new()
{
    private readonly static JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };
    private readonly string _filepath = Path.Combine(FileSystemHelper.GetAppDataDirectory(), filename);
    private T? _state;
    private readonly object _lock = new();


    public T Get()
    {
        lock (_lock)
        {
            if (_state is null)
            {
                if (File.Exists(_filepath))
                {
                    var raw = File.ReadAllText(_filepath);
                    try
                    {
                        _state = JsonSerializer.Deserialize<T>(raw) ?? new();
                    }
                    catch (JsonException)
                    {
                        _state = new();
                        File.Move(_filepath, Path.Combine(FileSystemHelper.GetAppDataDirectory(), $"state_corrupted_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.json"));
                    }
                }
                else
                {
                    _state = new();
                }
            }

            return _state;
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            if (_state is null)
            {
                return;
            }

            File.WriteAllText(_filepath, JsonSerializer.Serialize(_state, _serializerOptions));
        }
    }
}
