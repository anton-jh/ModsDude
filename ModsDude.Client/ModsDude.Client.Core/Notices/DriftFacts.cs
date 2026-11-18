using ModsDude.Client.Core.Sync;
using System.Globalization;

namespace ModsDude.Client.Core.Notices;

/// <summary>What a drifted mod folder differs by, as label/value rows. Rows with nothing in them are left out.</summary>
public static class DriftFacts
{
    public static IReadOnlyList<NoticeFact> For(DriftReport report)
    {
        var facts = new List<NoticeFact>();

        Count("Missing", report.Removed.Count);
        Count("Wrong version", report.Changed.Count);
        Count("Added", report.Added.Count);
        Count("Changed in profile", report.ProfileChangedMods.Count);

        if (report.ProfileHasMoved)
        {
            facts.Add(new("Revision", $"{report.AppliedRevision} → {report.CurrentRevision}"));
        }

        Count("Game files edited", report.EditedGameFiles.Count);

        return facts;

        void Count(string label, int count)
        {
            if (count > 0)
            {
                facts.Add(new(label, count.ToString(CultureInfo.CurrentCulture)));
            }
        }
    }
}
