using ModsDude.Client.Core.Helpers;
using System.Text.Json;

namespace ModsDude.Client.Core.Persistence;

/// <param name="isCompatible">
/// Decides whether state read from disk can be used as-is. Returning false discards it the same way
/// a parse failure does — the file is moved aside and a fresh instance is returned. This is what the
/// schema-version bump relies on; without it a bumped version silently deserializes old JSON into
/// the new shape rather than being discarded.
/// </param>
public class Store<T>(string filename, Func<T, bool>? isCompatible = null)
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
                        var loaded = JsonSerializer.Deserialize<T>(raw);

                        if (loaded is null || isCompatible?.Invoke(loaded) == false)
                        {
                            _state = new();
                            MoveAside();
                        }
                        else
                        {
                            _state = loaded;
                        }
                    }
                    catch (JsonException)
                    {
                        _state = new();
                        MoveAside();
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

            // Written through a temp file and moved into place. A plain WriteAllText that is
            // interrupted leaves a truncated file, which is exactly the corruption Get() recovers
            // from by discarding the user's instance list.
            var tempPath = _filepath + ".tmp";

            File.WriteAllText(tempPath, JsonSerializer.Serialize(_state, _serializerOptions));
            File.Move(tempPath, _filepath, true);
        }
    }


    private void MoveAside()
    {
        var name = Path.GetFileNameWithoutExtension(_filepath);
        var target = Path.Combine(
            FileSystemHelper.GetAppDataDirectory(),
            $"{name}_discarded_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.json");

        File.Move(_filepath, target);
    }
}
