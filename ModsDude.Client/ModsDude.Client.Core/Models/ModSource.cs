using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModsDude.Client.Core.Models;

/// <summary>
/// Somewhere to look for mods to import. Not a sync target: sync makes a game's mod folder
/// match a profile, which means uninstalling from it, and nothing will ever delete, move or
/// quarantine a file in Downloads or a folder the user pointed at.
/// See docs/09-mod-catalog.md#sources-are-not-sync-targets.
/// </summary>
public record ModSource(ModSourceId Id, string Name, string Path, ModSourceKind Kind);

public enum ModSourceKind
{
    /// <summary>
    /// The repo itself, as somewhere mods come from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a folder, and it is not scanned</b> - the catalog already holds what the repo has
    /// registered, and this is a name for that half of the merged list so it can be switched off like
    /// any other source. Turning it off is the only way to ask the question "what is on this computer
    /// that this repo does not have", which is what somebody looking for things to import is asking.
    /// </para>
    /// <para>
    /// It has no scan, no error state and no count of files, so nothing that walks sources ever sees
    /// it: it is composed by the surface that offers it and consumed by that surface's filter. See
    /// docs/09-mod-catalog.md#the-source-list.
    /// </para>
    /// </remarks>
    Repo,

    /// <summary>
    /// One of a game's mod folders. Present automatically, and disabling it here does not affect
    /// syncing to it. One per target, named after the folder where a game reaches more than one.
    /// </summary>
    Game,

    /// <summary>The system Downloads folder. Once per machine, not per game.</summary>
    Downloads,

    /// <summary>A folder the user added for this session. Never persisted.</summary>
    AdHoc,

    /// <summary>
    /// Another profile in this repo, as somewhere versions come from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It costs no scan.</b> A profile's pins are registered versions by foreign key, so the
    /// catalog already holds every one of them - what this source contributes is a membership set
    /// and a version per mod, not a walk over a folder. Like <see cref="Repo"/> it is composed by
    /// the surface that offers it and consumed by that surface's filter, and
    /// <see cref="Services.ModCatalog"/> never sees it.
    /// </para>
    /// <para>
    /// It does read the network, which no other source does. The rule it must not break is that
    /// <em>navigating</em> touches nothing, and enabling a chip is not navigating - so it is
    /// view-scoped like an ad-hoc folder, added by picking a profile and gone when the page is.
    /// </para>
    /// </remarks>
    Profile,

    /// <summary>
    /// Somewhere outside this machine that knows of newer versions - ModHub, for Farming Simulator.
    /// Supplied by the game's adapter; see <see cref="GameAdapters.IRemoteModSourcesAdapter"/>.
    /// </summary>
    /// <remarks>
    /// <b>It points and never supplies.</b> What it contributes is a link on a mod's row to where a newer
    /// version can be downloaded, not a version the editor can pin: there are no bytes behind it. Once the
    /// file is downloaded into a folder that is read, the version is an ordinary one and the link goes.
    /// It starts switched on: what it reads is the ModsDude server, which the page reads anyway, not a
    /// disk - the thing the other sources start off to protect.
    /// </remarks>
    Remote
}

/// <summary>
/// Identifies a source across sessions, which is what lets "do not look in this folder" be
/// remembered. Ad-hoc sources get one too, so the same code can enable and disable every kind, but
/// theirs is never written to disk.
/// </summary>
[JsonConverter(typeof(ModSourceIdJsonConverter))]
public readonly record struct ModSourceId
{
    private readonly string? _value;


    private ModSourceId(string value)
    {
        _value = value;
    }


    public string Value => _value ?? string.Empty;


    /// <summary>The one source that exists once per machine, so it needs no discriminator.</summary>
    public static ModSourceId Downloads { get; } = new("downloads");

    /// <summary>
    /// The repo itself. One per surface, which is already scoped to one repo, so it needs no
    /// discriminator either.
    /// </summary>
    public static ModSourceId Repo { get; } = new("repo");

    /// <summary>
    /// One of a game's mod folders, keyed by the target the folder belongs to.
    /// </summary>
    /// <remarks>
    /// <b>Per target rather than per game.</b> A source per game would scan one folder of three and
    /// quietly report what the other two hold as missing from the machine - and since this id is what
    /// remembers <em>do not look in this folder</em>, switching off the server's folder would
    /// otherwise switch off the client's with it.
    /// </remarks>
    public static ModSourceId ForTarget(ModTargetRef target) => new($"game:{target}");

    /// <summary>
    /// Keyed by the folder itself, so the same folder added twice is the same source - and so a
    /// disabled folder stays disabled however the user reaches it.
    /// </summary>
    public static ModSourceId ForFolder(string path) => new($"folder:{FileSystemHelper.NormalizePathForComparison(path)}");

    /// <summary>
    /// Another profile in the same repo. One surface is scoped to one repo, so the profile's own id
    /// is discriminator enough.
    /// </summary>
    public static ModSourceId ForProfile(Guid profileId) => new($"profile:{profileId:N}");

    /// <summary>
    /// A remote source the repo's adapter supplies, by the key the adapter gives it. One surface is
    /// scoped to one repo and so to one adapter, so the key is discriminator enough.
    /// </summary>
    public static ModSourceId ForRemote(string key) => new($"remote:{key}");

    public static ModSourceId Parse(string s) => string.IsNullOrWhiteSpace(s)
        ? throw new FormatException("A mod source id cannot be empty.")
        : new(s);

    public override string ToString() => Value;
}

public sealed class ModSourceIdJsonConverter : JsonConverter<ModSourceId>
{
    public override ModSourceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return ModSourceId.Parse(reader.GetString()
            ?? throw new JsonException("Expected a mod source id string."));
    }

    public override void Write(Utf8JsonWriter writer, ModSourceId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
