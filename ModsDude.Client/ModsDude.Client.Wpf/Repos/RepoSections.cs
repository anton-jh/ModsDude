using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Repos;

public enum RepoSection
{
    Overview,
    Admin,
    Members,
    Mods,
    Saves,
    Archive
}


/// <param name="RestrictedReason">Why this membership level cannot open it, or null where it can.</param>
public sealed record RepoSectionAccess(RepoSection Section, string Title, string Icon, string? RestrictedReason)
{
    public bool IsAvailable => RestrictedReason is null;

    public string ToolTip => RestrictedReason ?? Title;
}


/// <summary>
/// Which of a repo's own pages exist and which this membership level may open, in menu order. The
/// repo's menu and Home both read it, so the two cannot disagree.
/// </summary>
/// <remarks>
/// A section the adapter has no capability for is absent, while one closed to the membership level
/// is listed with its reason: a level is something to ask an admin for, and a capability is not.
/// Mods stays open to guests, who can read the catalog; only the actions on it are refused.
/// </remarks>
public static class RepoSections
{
    public static IReadOnlyList<RepoSectionAccess> Of(Repo repo)
    {
        var isGuest = repo.MembershipLevel < RepoMembershipLevel.Member;
        var isNotAdmin = repo.MembershipLevel < RepoMembershipLevel.Admin;

        List<RepoSectionAccess> sections =
        [
            new(RepoSection.Overview, "Overview", MenuIcons.Overview, null),
            new(RepoSection.Admin, "Admin", MenuIcons.Admin,
                isNotAdmin ? "Only an admin can rename this repo, change its game settings or delete it." : null),
            new(RepoSection.Members, "Members", MenuIcons.Members,
                isGuest ? "Guests cannot see who else is in a repo, or invite anybody to it. Ask an admin for a higher membership level." : null),
            new(RepoSection.Mods, "Mods", MenuIcons.Mods, null)
        ];

        if (repo.Adapter.CanSupportSavegames)
        {
            sections.Add(new(RepoSection.Saves, "Saves", MenuIcons.Saves, null));
        }

        sections.Add(new(RepoSection.Archive, "Archive", MenuIcons.Archive, null));

        return sections;
    }
}
