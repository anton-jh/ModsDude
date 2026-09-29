using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using System.Globalization;

namespace ModsDude.Client.Core.Profiles;

public enum ModListSortKind
{
    Name,
    Date,
    Attribute
}


public readonly record struct ModListSortKey(string Name, DateTimeOffset? Date, IReadOnlyList<ModAttribute> Attributes);


/// <summary>How a list names the date it sorts by: "Added 3 Mar", or "Unsaved" where there is none.</summary>
public sealed record ModListDateWording(string Verb, string Missing, string FullVerb, string FullMissing)
{
    public static ModListDateWording AddedToProfile { get; } =
        new("Added", "Unsaved", "Added to this profile", "Not saved to this profile yet.");

    public static ModListDateWording ImportedToRepo { get; } =
        new("Imported", "Not in repo", "Imported into the repo", "Not in the repo yet.");
}


/// <summary>
/// Ties fall back to the name, always ascending. A missing date sorts as the newest there is, and a
/// missing attribute sorts last in both directions.
/// </summary>
public sealed record ModListSort(ModListSortKind Kind = ModListSortKind.Name, string? Attribute = null, bool Ascending = true)
{
    public static ModListSort Default(ModListSortKind kind, string? attribute = null)
        => new(kind, kind is ModListSortKind.Attribute ? attribute : null, kind is not ModListSortKind.Date);

    public ModListSort Reversed() => this with { Ascending = Ascending is false };

    public int Compare(ModListSortKey left, ModListSortKey right)
    {
        var primary = Kind switch
        {
            ModListSortKind.Attribute when Attribute is not null
                => ModAttributeOrder.Compare(Attribute, Ascending, left.Attributes, right.Attributes),
            ModListSortKind.Date => Directed(CompareDates(left.Date, right.Date)),
            _ => Directed(NaturalOrder.Compare(left.Name, right.Name))
        };

        return primary != 0 ? primary : NaturalOrder.Compare(left.Name, right.Name);
    }

    public string? Caption(ModListSortKey key, DateTimeOffset now, ModListDateWording wording) => Kind switch
    {
        ModListSortKind.Date => key.Date is DateTimeOffset date ? $"{wording.Verb} {Day(date, now)}" : wording.Missing,
        ModListSortKind.Attribute when Attribute is not null => ModAttributeOrder.Caption(key.Attributes, Attribute),
        _ => null
    };

    public string? Describe(ModListSortKey key, ModListDateWording wording) => Kind switch
    {
        ModListSortKind.Date => key.Date is DateTimeOffset date
            ? $"{wording.FullVerb} {date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}."
            : wording.FullMissing,
        ModListSortKind.Attribute when Attribute is not null
            => $"{Attribute}: {ModAttributeOrder.Caption(key.Attributes, Attribute)}",
        _ => null
    };

    public string DirectionText => (Kind, Ascending) switch
    {
        (ModListSortKind.Date, true) => "Oldest first. Click to reverse.",
        (ModListSortKind.Date, false) => "Newest first. Click to reverse.",
        (_, true) => "A to Z. Click to reverse.",
        _ => "Z to A. Click to reverse."
    };


    private int Directed(int order) => Ascending ? order : -order;

    private static int CompareDates(DateTimeOffset? left, DateTimeOffset? right) => (left, right) switch
    {
        (null, null) => 0,
        (null, _) => 1,
        (_, null) => -1,
        var (a, b) => a!.Value.CompareTo(b!.Value)
    };

    private static string Day(DateTimeOffset value, DateTimeOffset now)
    {
        var local = value.ToLocalTime();

        return local.ToString(local.Year == now.ToLocalTime().Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
    }
}
