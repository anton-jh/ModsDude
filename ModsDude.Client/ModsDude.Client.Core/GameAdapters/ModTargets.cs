using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// One mod folder a game reaches on this machine, as the adapter names it.
/// </summary>
/// <remarks>
/// <b>A value the adapter returns, not a persisted entity.</b> No id, no row, no list the user
/// manages: a game that needs more than one target says so in its <c>LocalSettings</c>, and emptying
/// a field there takes the target away again.
/// </remarks>
/// <param name="Key">
/// The adapter's name for this target, stable across settings edits and adapter versions. It ends up
/// in a filename, which is the store's business to encode rather than a rule this has to obey - but
/// it is also what a manifest and a savegame binding are keyed on, so an adapter author renaming one
/// orphans both and is indistinguishable from having removed the target.
/// </param>
/// <param name="DisplayName">
/// What to call this folder, exactly as the user reads it - "Mod folder", "Dedicated server", "MP
/// client". Required even for a game with one target, because it is the whole label of that
/// folder's source in the mod list editor. Wordings that only mention a folder to tell it from its
/// siblings still leave it out where there is one - see <see cref="TargetNames.Distinguishing"/>.
/// </param>
/// <param name="Path">The folder itself. It need not exist right now.</param>
public sealed record ModTarget(TargetKey Key, string DisplayName, string Path)
{
    /// <summary>
    /// Whether the files placed here are safe to hardlink into the content store.
    /// </summary>
    /// <remarks>
    /// False when anything writing to this folder may <b>rewrite a mod file in place</b>, which
    /// through a hardlink would corrupt the store blob shared with every other repo and game on that
    /// volume. False also means "nobody has checked yet", which is why it is the default: the failure
    /// is silent, the blast radius is every repo on the disk, and a slow sync is visible and
    /// recoverable where a corrupted store is neither. Setting it true is an opt-in that means
    /// somebody tested what writes here. Turning it off again replaces every linked file with a copy
    /// on the next apply.
    /// </remarks>
    public bool SupportsHardlinks { get; init; }

    /// <summary>
    /// Whether other programs keep their own mods here too, so sync touches only what it installed.
    /// </summary>
    /// <remarks>
    /// An ordinary target is the profile and nothing else: every mod in it that the profile does not
    /// pin is taken out. A shared one - a launcher's download cache, say - holds files that belong to
    /// somebody else, and those are left exactly where they are, as are additions to it afterwards.
    /// </remarks>
    public bool Shared { get; init; }
}


/// <summary>
/// An adapter's name for one of a game's targets. Opaque outside the adapter that minted it.
/// </summary>
/// <remarks>
/// A type rather than a bare string for the reason <see cref="GameIdentity"/> is one: a target key, a
/// slot id and a folder name are all plausible-looking strings, and putting the wrong one in a
/// lookup fails as a silently empty answer rather than as a compile error.
/// </remarks>
[JsonConverter(typeof(TargetKeyJsonConverter))]
public readonly record struct TargetKey
{
    /// <summary>
    /// Reserved because <see cref="Models.SavegameSlotRef"/> renders as <c>{target}:{slot}</c>, and a
    /// key carrying the separator would make that rendering ambiguous to read back.
    /// </summary>
    internal const string Separator = ":";


    public TargetKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A target key cannot be blank.", nameof(value));
        }
        if (value.Contains(Separator))
        {
            throw new ArgumentException($"A target key cannot contain the separator: '{Separator}'", nameof(value));
        }

        Value = value;
    }


    public string Value { get; }


    public override string ToString() => Value;
}

/// <summary>
/// A target key as a string, because the persisted target list carries one per entry.
/// </summary>
/// <remarks>
/// Needed rather than merely tidy: a record struct with a validating constructor and a get-only
/// property round-trips through the default serializer as <c>{"Value":"mods"}</c> on the way out and
/// as a <em>blank</em> key on the way back, which would read as a target nobody can address.
/// </remarks>
public sealed class TargetKeyJsonConverter : JsonConverter<TargetKey>
{
    public override TargetKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new(reader.GetString() ?? throw new JsonException("Expected a target key string."));
    }

    public override void Write(Utf8JsonWriter writer, TargetKey value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}


/// <summary>
/// One of a game's mod targets, addressed from outside the adapter that named it: which game, and
/// which of its folders.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the per-folder stores are keyed on.</b> A manifest, a drift report, an eviction sweep's
/// pin list and a mod source all belong to one folder of one game, and a game reaching three of them
/// has three of each - so <see cref="GameIdentity"/> alone is not an address. It is a compound value
/// in the shape <see cref="Models.SavegameSlotRef"/> and <see cref="Models.ActiveProfile"/> already
/// have, rather than a prefixed string, which would invite parsing wherever one was handled.
/// </para>
/// <para>
/// The rendering exists for a log line and for <see cref="Models.ModSourceId"/>; nothing reads one
/// back. A manifest's filename is built by <see cref="Helpers.StoreFileName"/> out of the two parts
/// separately, which is what lets it escape each of them.
/// </para>
/// </remarks>
public readonly record struct ModTargetRef(GameIdentity Game, TargetKey Key)
{
    public override string ToString() => $"{Game}{TargetKey.Separator}{Key}";
}


/// <summary>
/// Every mod folder a game reaches, in the order an adapter wants them read.
/// </summary>
/// <remarks>
/// <para>
/// <b>A list, never a count.</b> Farming Simulator has one and BeamNG.drive with BeamMP has three;
/// both are this. A blank folder in an adapter's <c>LocalSettings</c> is a target the adapter omits
/// rather than one with a null path, so a game can reach none at all.
/// </para>
/// <para>
/// Keys are distinct within a game and every target is named - a construction, checked here,
/// rather than something every adapter has to be trusted with.
/// </para>
/// </remarks>
public sealed class ModTargets : IReadOnlyList<ModTarget>
{
    /// <summary>A game whose settings point at no folder at all. Nothing to sync, and not an error.</summary>
    public static ModTargets None { get; } = new();


    private readonly ModTarget[] _targets;


    public ModTargets(params IEnumerable<ModTarget> targets)
    {
        _targets = [.. targets];

        if (_targets.GroupBy(x => x.Key).FirstOrDefault(x => x.Count() > 1) is IGrouping<TargetKey, ModTarget> duplicate)
        {
            throw new ArgumentException($"An adapter returned two targets keyed '{duplicate.Key}'.", nameof(targets));
        }

        if (Array.Find(_targets, x => string.IsNullOrWhiteSpace(x.DisplayName)) is ModTarget unnamed)
        {
            throw new ArgumentException($"An adapter returned target '{unnamed.Key}' without a name.", nameof(targets));
        }
    }


    public int Count => _targets.Length;

    public ModTarget this[int index] => _targets[index];

    /// <summary>The target with this key, or null where the settings no longer produce one.</summary>
    public ModTarget? this[TargetKey key] => Array.Find(_targets, x => x.Key == key);


    public IEnumerator<ModTarget> GetEnumerator() => ((IEnumerable<ModTarget>)_targets).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _targets.GetEnumerator();
}
