namespace ModsDude.Server.Api.Maintenance;

public class BlobReclamationOptions
{
    public const string SectionName = "BlobReclamation";


    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When the sweep runs, in UTC. Storage only grows by what a failed import or a delete left behind,
    /// so there is nothing to gain from running it often.
    /// </summary>
    public string Cron { get; set; } = "0 4 * * *";

    /// <summary>
    /// How long an unreferenced blob must have sat untouched before the sweep may delete it. The
    /// hazard this exists for is the import that has uploaded but not yet registered — see
    /// <see cref="Domain.Mods.BlobReclamation"/>. An upload SAS lives 30 minutes and registration
    /// follows the upload immediately, so a day is three orders of magnitude of headroom, bought with
    /// nothing but a delay in reclaiming bytes nobody is paying attention to.
    /// </summary>
    public TimeSpan MinimumBlobAge { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// The largest share of a container one sweep may reclaim. Past it (and past
    /// <see cref="Domain.Mods.BlobReclamation.ImplausibleFloor"/> blobs) the sweep deletes nothing in
    /// any container and fails, because the likelier story is a database that is not the one storage
    /// belongs to. Raise it for one run to let a genuine large clean-up through, such as the residue of
    /// a deleted repo; 1 turns the check off.
    /// </summary>
    public double MaxReclaimableShare { get; set; } = 0.5;
}
