using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Helpers;
using System.Text.Json;

namespace ModsDude.Client.Core.Persistence;

/// <summary>
/// State persisted as one json file. It is only ever touched under one lock - read through
/// <see cref="Read"/>, changed and written through <see cref="Update"/> - so a write can never
/// serialise a collection another thread is changing.
/// </summary>
/// <param name="isCompatible">
/// Decides whether state read from disk can be used as-is. Returning false discards it the same way
/// a parse failure does — the file is moved aside and a fresh instance is returned. This is what the
/// schema-version bump relies on; without it a bumped version silently deserializes old JSON into
/// the new shape rather than being discarded.
/// </param>
public class Store<T>(string filename, Func<T, bool>? isCompatible = null, ILogger? logger = null)
    where T : class, new()
{
    private readonly static JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };
    private readonly string _filepath = Path.Combine(FileSystemHelper.GetAppDataDirectory(), filename);
    private T? _state;
    private readonly Lock _lock = new();

    /// <summary>Never null: a store built without one is a store nothing is listening to.</summary>
    private ILogger Log { get; } = logger ?? NullLogger.Instance;


    public TResult Read<TResult>(Func<T, TResult> read)
    {
        lock (_lock)
        {
            return read(Load());
        }
    }

    public void Update(Action<T> update)
    {
        lock (_lock)
        {
            update(Load());
            Write();
        }
    }

    public bool UpdateIf(Func<T, bool> update)
    {
        lock (_lock)
        {
            var changed = update(Load());

            if (changed)
            {
                Write();
            }

            return changed;
        }
    }


    private T Load()
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
                        // Deliberate, not a fault: a schema bump discards old state by design.
                        // Still worth a line, because it is why somebody's settings are gone.
                        Log.LogInformation("{File} is not compatible with this version and was moved aside.", _filepath);

                        _state = new();
                        MoveAside();
                    }
                    else
                    {
                        _state = loaded;
                    }
                }
                catch (JsonException exception)
                {
                    // The user's connected games, store assignments and savegame bindings, gone.
                    // Recoverable - the file is moved aside rather than deleted - but only by
                    // somebody who knows it happened, which is what this line is for.
                    Log.LogError(exception, "{File} could not be read and was moved aside; starting from empty state.", _filepath);

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

    private void Write()
    {
        AtomicFile.WriteAllText(_filepath, JsonSerializer.Serialize(_state, _serializerOptions));
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
