namespace ModsDude.Server.Api.Maintenance;

public class BlobReclamationOptions
{
    public const string SectionName = "BlobReclamation";


    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the sweep runs, and how long it waits before the first one. Storage only grows by
    /// what a failed import or a delete left behind, so there is nothing to gain from running it
    /// often — and the first sweep deliberately waits a whole interval so that a crash loop cannot
    /// become a delete loop.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How long an unreferenced blob must have sat untouched before the sweep may delete it. The
    /// hazard this exists for is the import that has uploaded but not yet registered — see
    /// <see cref="Domain.Mods.BlobReclamation"/>. An upload SAS lives 30 minutes and registration
    /// follows the upload immediately, so a day is three orders of magnitude of headroom, bought with
    /// nothing but a delay in reclaiming bytes nobody is paying attention to.
    /// </summary>
    public TimeSpan MinimumBlobAge { get; set; } = TimeSpan.FromDays(1);
}
