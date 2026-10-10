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


/// <param name="UnsupportedReason">Why the repo's game has no such page, or null where it has.</param>
/// <param name="RestrictedReason">Why this membership level cannot open it, or null where it can.</param>
public sealed record RepoSectionAccess(RepoSection Section, string Title, string Icon, string? UnsupportedReason, string? RestrictedReason)
{
    public bool IsSupported => UnsupportedReason is null;

    public bool IsAvailable => IsSupported && RestrictedReason is null;

    public string ToolTip => UnsupportedReason ?? RestrictedReason ?? Title;
}


/// <summary>
/// Every one of a repo's own pages, whether its game supports it and whether this membership level may
/// open it, in menu order. The repo's menu and Home both read it, so the two cannot disagree.
/// </summary>
/// <remarks>
/// Home shows every section, greyed with the reason where it cannot be opened, so every repo's row
/// has the same icons. The repo's menu leaves out what the game does not support, since that never
/// changes, and greys what the membership level closes, which an admin can change.
/// Mods stays open to guests, who can read the catalog; only the actions on it are refused.
/// </remarks>
public static class RepoSections
{
    public static IReadOnlyList<RepoSectionAccess> Of(Repo repo)
    {
        var isGuest = repo.MembershipLevel < RepoMembershipLevel.Member;
        var isNotAdmin = repo.MembershipLevel < RepoMembershipLevel.Admin;

        var game = repo.Adapter.GameDisplayName;

        return
        [
            new(RepoSection.Overview, "Overview", MenuIcons.Overview, null, null),
            new(RepoSection.Admin, "Admin", MenuIcons.Admin, null,
                isNotAdmin ? "Only an admin can rename this repo, change its game settings or delete it." : null),
            new(RepoSection.Members, "Members", MenuIcons.Members, null,
                isGuest ? "Guests cannot see who else is in a repo, or invite anybody to it. Ask an admin for a higher membership level." : null),
            new(RepoSection.Mods, "Mods", MenuIcons.Mods,
                repo.Adapter.CanSupportMods ? null : $"No mod support for {game}.", null),
            new(RepoSection.Saves, "Saves", MenuIcons.Saves,
                repo.Adapter.CanSupportSavegames ? null : $"No savegame support for {game}.", null),
            new(RepoSection.Archive, "Archive", MenuIcons.Archive, null, null)
        ];
    }
}
