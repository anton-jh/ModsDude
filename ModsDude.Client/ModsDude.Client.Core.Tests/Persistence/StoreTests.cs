using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Tests.Sync;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModsDude.Client.Core.Tests.Persistence;

public class StoreTests : IDisposable
{
    private const string _file = "state.json";

    private readonly TempDirectory _directory = new("store");


    [Fact]
    public void A_missing_file_reads_as_empty_state()
    {
        var store = Create(Schema(3, 3));

        Assert.Equal(3, store.Read(x => x.Version));
        Assert.Empty(store.Read(x => x.Names.ToList()));
    }

    [Fact]
    public void A_current_file_reads_as_it_is()
    {
        WriteFile("""{ "Version": 3, "Names": ["a", "b"] }""");

        var store = Create(Schema(3, 3));

        Assert.Equal(["a", "b"], store.Read(x => x.Names.ToList()));
        Assert.Single(Directory.GetFiles(_directory.Path));
    }

    [Fact]
    public void An_older_file_is_carried_forward_one_version_at_a_time_and_written_back()
    {
        WriteFile("""{ "Version": 1, "Name": "a" }""");

        var store = Create(Schema(3, 1,
            v1 => new JsonObject { ["Version"] = 2, ["Names"] = new JsonArray(v1["Name"]!.GetValue<string>()) },
            v2 => { v2["Names"]!.AsArray().Add("added by 3"); return v2; }));

        Assert.Equal(["a", "added by 3"], store.Read(x => x.Names.ToList()));

        var written = JsonNode.Parse(File.ReadAllText(_directory.Combine(_file)))!;
        Assert.Equal(3, written["Version"]!.GetValue<int>());
        Assert.Equal(2, written["Names"]!.AsArray().Count);
    }

    [Fact]
    public void The_file_before_a_migration_is_kept_aside()
    {
        const string original = """{ "Version": 2, "Names": ["a"] }""";
        WriteFile(original);

        var store = Create(Schema(3, 2, v2 => v2));
        store.Read(x => x.Version);

        var backup = Assert.Single(Directory.GetFiles(_directory.Path, "state_v2_backup_*.json"));
        Assert.Equal(original, File.ReadAllText(backup));
    }

    [Fact]
    public void A_file_from_a_newer_version_is_set_aside_rather_than_misread()
    {
        WriteFile("""{ "Version": 4, "Names": ["a"] }""");

        var store = Create(Schema(3, 3));

        Assert.Empty(store.Read(x => x.Names.ToList()));
        Assert.Single(Directory.GetFiles(_directory.Path, "state_discarded_*.json"));
    }

    [Fact]
    public void A_file_older_than_any_migration_is_set_aside()
    {
        WriteFile("""{ "Version": 1, "Names": ["a"] }""");

        var store = Create(Schema(3, 2, v2 => v2));

        Assert.Empty(store.Read(x => x.Names.ToList()));
        Assert.Single(Directory.GetFiles(_directory.Path, "state_discarded_*.json"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "Names": ["a"] }""")]
    [InlineData("""{ "Version": "three" }""")]
    [InlineData("""{ "Version": 3, "Names": "not a list" }""")]
    // A converter throwing something other than JsonException, as the id converters do.
    [InlineData("""{ "Version": 3, "Names": [], "Id": "not-an-id" }""")]
    public void An_unreadable_file_is_set_aside_and_state_starts_empty(string content)
    {
        WriteFile(content);

        var store = Create(Schema(3, 3));

        Assert.Empty(store.Read(x => x.Names.ToList()));

        var aside = Assert.Single(Directory.GetFiles(_directory.Path, "state_discarded_*.json"));
        Assert.Equal(content, File.ReadAllText(aside));
    }

    [Fact]
    public void A_migration_that_throws_sets_the_file_aside()
    {
        WriteFile("""{ "Version": 2, "Names": ["a"] }""");

        var store = Create(Schema(3, 2, _ => throw new ArgumentException("Cannot carry this forward.")));

        Assert.Empty(store.Read(x => x.Names.ToList()));
        Assert.Single(Directory.GetFiles(_directory.Path, "state_discarded_*.json"));
    }

    [Fact]
    public void A_change_is_written_to_disk()
    {
        var store = Create(Schema(3, 3));

        store.Update(x => x.Names.Add("a"));

        Assert.Equal(["a"], Create(Schema(3, 3)).Read(x => x.Names.ToList()));
    }

    [Fact]
    public void A_change_that_fails_to_be_written_is_dropped_from_memory_too()
    {
        WriteFile("""{ "Version": 3, "Names": ["a"] }""");
        var store = Create(Schema(3, 3));
        store.Read(x => x.Version);

        var blocker = _directory.Combine(_file);
        File.Delete(blocker);
        Directory.CreateDirectory(blocker);

        Assert.ThrowsAny<Exception>(() => store.Update(x => x.Names.Add("b")));

        Directory.Delete(blocker);
        WriteFile("""{ "Version": 3, "Names": ["a"] }""");

        Assert.Equal(["a"], store.Read(x => x.Names.ToList()));
    }

    [Fact]
    public void A_change_that_throws_part_way_is_dropped_from_memory()
    {
        WriteFile("""{ "Version": 3, "Names": ["a"] }""");
        var store = Create(Schema(3, 3));

        Assert.Throws<InvalidOperationException>(() => store.Update(x =>
        {
            x.Names.Add("b");

            throw new InvalidOperationException("Halfway.");
        }));

        Assert.Equal(["a"], store.Read(x => x.Names.ToList()));
    }

    [Fact]
    public void An_unchanged_update_writes_nothing()
    {
        var store = Create(Schema(3, 3));

        Assert.False(store.UpdateIf(_ => false));
        Assert.False(File.Exists(_directory.Combine(_file)));
    }

    [Fact]
    public void A_schema_without_a_migration_for_every_step_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Create(Schema(3, 1, v1 => v1)));
    }


    public void Dispose() => _directory.Dispose();


    private Store<TestState> Create(StoreSchema schema)
        => new(_directory.Path, _file, schema, NullLogger.Instance);

    private static StoreSchema Schema(int current, int oldest, params Func<JsonObject, JsonObject>[] migrations)
        => new(current, oldest, migrations);

    private void WriteFile(string content) => File.WriteAllText(_directory.Combine(_file), content);


    public sealed class TestState
    {
        public int Version { get; set; } = 3;

        public List<string> Names { get; init; } = [];

        [JsonConverter(typeof(StrictIdConverter))]
        public string? Id { get; init; }
    }

    /// <summary>Throws the way the strongly typed id converters do on a value they cannot parse.</summary>
    private sealed class StrictIdConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString()!;

            return Guid.TryParse(value, out _) ? value : throw new FormatException($"'{value}' is not an id.");
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }
}
