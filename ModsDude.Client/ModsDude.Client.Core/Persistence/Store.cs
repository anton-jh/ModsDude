using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Helpers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModsDude.Client.Core.Persistence;

/// <summary>
/// State persisted as one json file. It is only ever touched under one lock - read through
/// <see cref="Read"/>, changed and written through <see cref="Update"/> - so a write can never
/// serialise a collection another thread is changing.
/// </summary>
/// <remarks>
/// <para>
/// <b>An older file is migrated, never dropped.</b> The file as it was is copied aside first, then
/// carried forward one version at a time and written back. A file this build cannot read - written by
/// a newer build, older than the oldest migration, or unreadable - is moved aside and the store starts
/// empty, with the reason logged.
/// </para>
/// <para>
/// <b>Memory never runs ahead of the disk.</b> A change that fails to be written is dropped from
/// memory too, and the next read loads the file again.
/// </para>
/// </remarks>
public class Store<T>(string directory, string filename, StoreSchema schema, ILogger logger)
    where T : class, new()
{
    private readonly static JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };
    private readonly string _filepath = Path.Combine(directory, filename);
    private readonly StoreSchema _schema = schema.Validate();
    private readonly Lock _lock = new();
    private T? _state;


    public TResult Read<TResult>(Func<T, TResult> read)
    {
        lock (_lock)
        {
            return read(Load());
        }
    }

    public void Update(Action<T> update)
    {
        UpdateIf(state =>
        {
            update(state);

            return true;
        });
    }

    public bool UpdateIf(Func<T, bool> update)
    {
        lock (_lock)
        {
            var state = Load();

            try
            {
                var changed = update(state);

                if (changed)
                {
                    Write(state);
                }

                return changed;
            }
            catch
            {
                _state = null;

                throw;
            }
        }
    }


    private T Load()
    {
        return _state ??= File.Exists(_filepath) ? ReadFile() : new();
    }

    /// <remarks>
    /// Reading the file is not guarded: a file that cannot be opened right now is not a file that
    /// cannot be understood, and setting it aside would lose it.
    /// </remarks>
    private T ReadFile()
    {
        var raw = File.ReadAllText(_filepath);

        JsonObject document;
        int version;

        try
        {
            document = JsonNode.Parse(raw) as JsonObject ?? throw new JsonException("The file is not a json object.");
            version = document[StoreSchema.VersionProperty]?.GetValue<int>() ?? throw new JsonException("The file has no version.");
        }
        catch (Exception exception)
        {
            return Discard(exception, "could not be read");
        }

        if (version > _schema.CurrentVersion)
        {
            return Discard(null, $"was written by a newer version ({version}, this one reads up to {_schema.CurrentVersion})");
        }

        if (version < _schema.OldestVersion)
        {
            return Discard(null, $"is version {version}, older than the oldest this one carries forward ({_schema.OldestVersion})");
        }

        T state;

        try
        {
            for (var from = version; from < _schema.CurrentVersion; from++)
            {
                document = _schema.Migrations[from - _schema.OldestVersion](document);
            }

            document[StoreSchema.VersionProperty] = _schema.CurrentVersion;

            state = document.Deserialize<T>() ?? throw new JsonException("The file is empty.");
        }
        catch (Exception exception)
        {
            return Discard(exception, "could not be read");
        }

        if (version < _schema.CurrentVersion)
        {
            var backup = CopyAside($"v{version}_backup");

            Write(state);

            logger.LogInformation("{File} was carried forward from version {From} to {To}; the original is at {Backup}.",
                _filepath, version, _schema.CurrentVersion, backup);
        }

        return state;
    }

    /// <summary>Sets the file aside and starts from empty state, saying why.</summary>
    private T Discard(Exception? exception, string reason)
    {
        var aside = MoveAside();

        logger.LogError(exception, "{File} {Reason}. It was moved to {Aside}; starting from empty state.", _filepath, reason, aside);

        return new();
    }

    private void Write(T state)
    {
        AtomicFile.DeleteLeftovers(_filepath, logger);
        AtomicFile.WriteAllText(_filepath, JsonSerializer.Serialize(state, _serializerOptions));
    }

    /// <summary>
    /// Moves the file aside, or copies it where it cannot be moved. Throws where neither works, so a
    /// file that could not be kept is never written over.
    /// </summary>
    private string MoveAside()
    {
        var target = AsidePath("discarded");

        try
        {
            File.Move(_filepath, target);

            return target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not move {File} aside; copying it instead.", _filepath);

            File.Copy(_filepath, target);

            return target;
        }
    }

    private string CopyAside(string label)
    {
        var target = AsidePath(label);

        File.Copy(_filepath, target);

        return target;
    }

    private string AsidePath(string label)
    {
        var name = Path.GetFileNameWithoutExtension(_filepath);

        return Path.Combine(directory, $"{name}_{label}_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.json");
    }
}
