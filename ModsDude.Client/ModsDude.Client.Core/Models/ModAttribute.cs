namespace ModsDude.Client.Core.Models;

/// <summary>
/// One tag an adapter attached to a mod version, for searching by. A <c>(Key, Value?)</c> pair and
/// nothing more - the same shape the server stores, which never reads one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing may depend on one.</b> Attributes exist to be searched and displayed; a fact the
/// system needs in order to behave correctly is a real property with a real column, which is what
/// <see cref="CatalogModVersion.Locked"/> and <c>ContentHash</c> are. A client that ignores
/// attributes entirely is still correct.
/// </para>
/// <para>
/// <b>A key may repeat.</b> A pack of vehicles spanning three shop categories carries three
/// <c>category</c> attributes, and a search matches it on any one of them.
/// </para>
/// </remarks>
/// <param name="Key">One of the keys the adapter declared - see <see cref="ModAttributeDefinition"/>.</param>
/// <param name="Value">What it says, raw, or null for a tag that is only present or absent.</param>
public record ModAttribute(string Key, string? Value);


/// <summary>
/// One attribute key an adapter can report, declared up front so a search box can offer it before
/// any mod carrying it has been read.
/// </summary>
/// <remarks>
/// <b>An adapter reports only what it declares.</b> A key it attaches to a mod without declaring it
/// here is an adapter bug: nothing could complete it, and a search for it would be mistaken for
/// plain text. Values are looser - see <paramref name="Values"/>.
/// </remarks>
/// <param name="Key">Stable and short enough to type. Matched ignoring case.</param>
/// <param name="Aliases">Shorter spellings a search accepts in place of <paramref name="Key"/>. Never stored.</param>
/// <param name="Values">
/// The values the game itself defines, for a key that has such a list - empty for one whose values
/// are whatever the mods say, like a brand. Not a whitelist: a mod declaring a value the list does
/// not know still reports it, and a search box offers it from the catalog the same as any other.
/// </param>
public record ModAttributeDefinition(string Key, IReadOnlyList<string> Aliases, IReadOnlyList<string> Values)
{
    /// <summary>Whether <paramref name="name"/> is this key or one of its aliases, ignoring case.</summary>
    public bool IsNamed(string name)
        => string.Equals(Key, name, StringComparison.OrdinalIgnoreCase)
        || Aliases.Contains(name, StringComparer.OrdinalIgnoreCase);
}
