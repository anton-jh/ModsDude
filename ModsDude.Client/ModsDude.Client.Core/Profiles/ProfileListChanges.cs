using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Profiles;

public static class ProfileListChanges
{
    /// <summary>
    /// What would change about <paramref name="local"/> if it were refreshed to
    /// <paramref name="remote"/>, or null where nothing would.
    /// </summary>
    /// <remarks>
    /// The head revision counts although the sidebar does not draw it: it is part of the copy, and
    /// until it is brought in the drift notice and the profile's overview are both quoting a
    /// revision that is no longer the latest.
    /// </remarks>
    public static RemoteChanges? Between(IReadOnlyList<ProfileDto> local, IEnumerable<ProfileDto> remote)
    {
        var lines = new List<string>();
        var localById = local.ToDictionary(x => x.Id);
        var remoteIds = new HashSet<Guid>();

        foreach (var dto in remote)
        {
            remoteIds.Add(dto.Id);

            if (localById.TryGetValue(dto.Id, out var existing) is false)
            {
                lines.Add($"'{dto.Name}' was added.");

                continue;
            }

            if (existing.Name != dto.Name)
            {
                lines.Add($"'{existing.Name}' was renamed to '{dto.Name}'.");
            }

            var newRevisions = dto.HeadRevision - existing.HeadRevision;

            if (newRevisions == 1)
            {
                lines.Add($"'{dto.Name}' has a new revision.");
            }
            else if (newRevisions > 1)
            {
                lines.Add($"'{dto.Name}' has {newRevisions} new revisions.");
            }
            else if (newRevisions < 0)
            {
                // Not something the server does - the head only ever moves forward - but a different
                // number is a different number, and the refresh would bring it in.
                lines.Add($"'{dto.Name}' is now at revision {dto.HeadRevision}.");
            }
        }

        // Archived or deleted. Either way it leaves the sidebar, which is all this is describing.
        foreach (var gone in local.Where(x => remoteIds.Contains(x.Id) is false))
        {
            lines.Add($"'{gone.Name}' was removed.");
        }

        return lines.Count == 0 ? null : new RemoteChanges(lines);
    }
}
