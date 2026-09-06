using NaturalSort.Extension;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// How the app orders anything a person named. "Mod 9" comes before "Mod 10", because the digits in
/// a name are a number and an ordinal sort is the only reason they were ever read as text.
/// </summary>
/// <remarks>
/// <para>
/// <b>One comparer, used everywhere a display name is sorted</b> - mods, versions, profiles, repos,
/// savegames, members, instances. A list that sorts differently from the list beside it is a list
/// somebody has to learn, and the whole value of natural ordering is that nobody has to.
/// </para>
/// <para>
/// Culture-aware and case-insensitive on the non-digit runs, which is what puts 'Ä' where a Swedish
/// reader expects it rather than after 'Z'. Not for anything the machine reads: file names, hashes,
/// volume roots and zip entries stay ordinal, because those orderings are compared against stored
/// values and must not move when the thread's culture does.
/// </para>
/// </remarks>
public static class NaturalOrder
{
    /// <summary>The one comparer. Thread-safe, and cheap enough to hand to every <c>OrderBy</c>.</summary>
    public static IComparer<string> Comparer { get; } =
        StringComparer.CurrentCultureIgnoreCase.WithNaturalSort();


    /// <inheritdoc cref="Comparer"/>
    public static int Compare(string left, string right) => Comparer.Compare(left, right);
}
