using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using System.Globalization;

namespace ModsDude.Client.Core.Profiles;

/// <summary>What the editor's right list is ordered by.</summary>
public enum ProfileModSort
{
    /// <summary>By the mod's name. The order that does not move under the pointer as the draft is edited.</summary>
    Name,

    /// <summary>
    /// By when the mod entered the profile or last had its version changed - the last time something
    /// happened to it that the game would notice. See <c>ModDependency.Added</c> on the server.
    /// </summary>
    DateAdded,

    /// <summary>By one attribute key's values, in <see cref="ModAttributeOrder"/>. Which key is said beside it.</summary>
    Attribute
}


/// <summary>What a row is ordered by, apart from the row itself.</summary>
/// <param name="Added">When the mod arrived in the profile at its pinned version.</param>
/// <param name="Attributes">What the version the row shows is tagged with.</param>
public readonly record struct ProfileModSortKey(string Name, DateTime? Added, IReadOnlyList<ModAttribute>? Attributes = null);


/// <summary>
/// The right list's orderings, and how a row says what it is ordered by.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ties fall back to the name, always ascending.</b> Two mods added by one save share an instant,
/// and a list whose order within them changed with the direction would read as shuffling rather than
/// as reversing. The same goes for the hundred mods sharing a category.
/// </para>
/// <para>
/// <b>A missing date sorts as the newest there is.</b> Every pinned row has one, so this is only a
/// guard - but if one were missing it would be the draft's own doing, which is the most recent event
/// in the list, and newest-first is where somebody looking for what they just did will look.
/// </para>
/// <para>
/// <b>A missing attribute sorts last either way</b> - see <see cref="ModAttributeOrder"/>.
/// </para>
/// </remarks>
public static class ProfileModSorting
{
    /// <summary>
    /// The direction a sort opens in, which is what somebody reaching for it wants: names and
    /// attributes A to Z, and for a date the most recent first.
    /// </summary>
    public static bool DefaultAscending(ProfileModSort sort) => sort is not ProfileModSort.DateAdded;

    /// <param name="ascending">
    /// Whether the comparison runs in its natural direction - A to Z, or oldest first - rather than
    /// against it.
    /// </param>
    /// <param name="attribute">The key an <see cref="ProfileModSort.Attribute"/> sort is by.</param>
    public static int Compare(ProfileModSort sort, bool ascending, ProfileModSortKey left, ProfileModSortKey right, string? attribute = null)
    {
        var primary = sort switch
        {
            // Already directed, because the mods without the key stay at the bottom both ways.
            ProfileModSort.Attribute when attribute is not null
                => ModAttributeOrder.Compare(attribute, ascending, left.Attributes ?? [], right.Attributes ?? []),
            ProfileModSort.DateAdded => Directed(CompareDates(left.Added, right.Added), ascending),
            _ => Directed(NaturalOrder.Compare(left.Name, right.Name), ascending)
        };

        return primary != 0 ? primary : NaturalOrder.Compare(left.Name, right.Name);
    }

    private static int Directed(int order, bool ascending) => ascending ? order : -order;

    private static int CompareDates(DateTime? left, DateTime? right) => (left, right) switch
    {
        (null, null) => 0,
        (null, _) => 1,
        (_, null) => -1,
        var (a, b) => DateTime.Compare(a!.Value.ToUniversalTime(), b!.Value.ToUniversalTime())
    };


    /// <summary>
    /// What a row shows under the sort it is in, or null under the name sort where the row has
    /// nothing more to say. For a date the day and month, and the year only where it is not this
    /// one - it is read as a list, and a year on every row is the same number over and over. For an
    /// attribute its values.
    /// </summary>
    public static string? Caption(ProfileModSort sort, ProfileModSortKey key, DateTime now, string? attribute = null) => sort switch
    {
        ProfileModSort.DateAdded => key.Added is DateTime added ? $"Added {Day(added, now)}" : null,
        ProfileModSort.Attribute when attribute is not null => ModAttributeOrder.Caption(key.Attributes ?? [], attribute),
        _ => null
    };

    /// <summary>What the row's caption says in full, for its tooltip.</summary>
    public static string Describe(ProfileModSort sort, ProfileModSortKey key, string? attribute = null)
    {
        if (sort is ProfileModSort.Attribute && attribute is not null)
        {
            return $"{attribute}: {ModAttributeOrder.Caption(key.Attributes ?? [], attribute)}";
        }

        return key.Added is DateTime added
            ? $"Added to this profile {Full(added)}."
            : "Not yet added to this profile.";
    }

    private static string Day(DateTime value, DateTime now)
    {
        var local = value.ToLocalTime();

        return local.ToString(local.Year == now.ToLocalTime().Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
    }

    private static string Full(DateTime value) => value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
