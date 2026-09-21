using ModsDude.Client.Core.Helpers;
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
    DateAdded
}


/// <summary>What a row is ordered by, apart from the row itself.</summary>
/// <param name="Added">When the mod arrived in the profile at its pinned version.</param>
public readonly record struct ProfileModSortKey(string Name, DateTime? Added);


/// <summary>
/// The right list's orderings, and how a row says what it is ordered by.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ties fall back to the name, always ascending.</b> Two mods added by one save share an instant,
/// and a list whose order within them changed with the direction would read as shuffling rather than
/// as reversing.
/// </para>
/// <para>
/// <b>A missing date sorts as the newest there is.</b> Every pinned row has one, so this is only a
/// guard - but if one were missing it would be the draft's own doing, which is the most recent event
/// in the list, and newest-first is where somebody looking for what they just did will look.
/// </para>
/// </remarks>
public static class ProfileModSorting
{
    /// <summary>
    /// The direction a sort opens in, which is what somebody reaching for it wants: names A to Z, and
    /// for a date the most recent first.
    /// </summary>
    public static bool DefaultAscending(ProfileModSort sort) => sort is ProfileModSort.Name;

    /// <param name="ascending">
    /// Whether the comparison runs in its natural direction - A to Z, or oldest first - rather than
    /// against it.
    /// </param>
    public static int Compare(ProfileModSort sort, bool ascending, ProfileModSortKey left, ProfileModSortKey right)
    {
        var primary = sort switch
        {
            ProfileModSort.DateAdded => CompareDates(left.Added, right.Added),
            _ => NaturalOrder.Compare(left.Name, right.Name)
        };

        if (ascending is false)
        {
            primary = -primary;
        }

        return primary != 0 ? primary : NaturalOrder.Compare(left.Name, right.Name);
    }

    private static int CompareDates(DateTime? left, DateTime? right) => (left, right) switch
    {
        (null, null) => 0,
        (null, _) => 1,
        (_, null) => -1,
        var (a, b) => DateTime.Compare(a!.Value.ToUniversalTime(), b!.Value.ToUniversalTime())
    };


    /// <summary>
    /// What a row shows under the date sort, or null under the name sort where the row has nothing more
    /// to say. The day and month, and the year only where it is not this one - it is read as a list,
    /// and a year on every row is the same number over and over.
    /// </summary>
    public static string? Caption(ProfileModSort sort, ProfileModSortKey key, DateTime now) => sort switch
    {
        ProfileModSort.DateAdded => key.Added is DateTime added ? $"Added {Day(added, now)}" : null,
        _ => null
    };

    /// <summary>The date in full, for the tooltip a caption carries.</summary>
    public static string Describe(ProfileModSortKey key) => key.Added is DateTime added
        ? $"Added to this profile {Full(added)}."
        : "Not yet added to this profile.";

    private static string Day(DateTime value, DateTime now)
    {
        var local = value.ToLocalTime();

        return local.ToString(local.Year == now.ToLocalTime().Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
    }

    private static string Full(DateTime value) => value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
