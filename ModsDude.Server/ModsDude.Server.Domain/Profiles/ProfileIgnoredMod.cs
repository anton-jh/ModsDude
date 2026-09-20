using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Profiles;

/// <summary>
/// A mod somebody has actively decided this profile is not going to have - the noise, as opposed to
/// what is merely not there yet.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not part of a revision.</b> What a revision records is what the profile pins, and ignoring a
/// mod changes nothing about what the profile applies: it only hides a row from the list somebody
/// picks mods out of. Versioning it would put a revision in the history for every triaged mod, and
/// make restoring an old one silently un-ignore whatever had been ignored since. It is written on its own, and is shared with
/// everyone who edits the profile, which is the reason it lives on the server at all.
/// </para>
/// <para>
/// <b>A mod, not a version, and not a foreign key.</b> The left list is keyed by mod, and it also
/// holds mods that exist only in somebody's folder - the ones most worth ignoring, and the ones the
/// repo has never registered. So the mod id is just carried; nothing is required to know it.
/// </para>
/// <para>
/// <b>Removed with the mod.</b> Deleting a mod from the repo takes it off every profile's list, so a mod that is
/// imported again later does not come back already hidden.
/// </para>
/// <para>
/// <b>A pinned mod cannot also be ignored.</b> Held from both sides: writing the list refuses what the
/// profile's head pins, and every write of a revision releases whatever it pins. See
/// <c>ProfileIgnoredModExtensions</c>.
/// </para>
/// </remarks>
public class ProfileIgnoredMod(RepoId repoId, ProfileId profileId, ModId modId)
{
    public RepoId RepoId { get; } = repoId;
    public ProfileId ProfileId { get; } = profileId;
    public ModId ModId { get; } = modId;
}
