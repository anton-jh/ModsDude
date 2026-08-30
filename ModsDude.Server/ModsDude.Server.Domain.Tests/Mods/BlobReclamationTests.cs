using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Tests.Mods;

public class BlobReclamationTests
{
    private static readonly Guid _repoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset _cutoff = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private const string _hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string _otherHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";


    [Fact]
    public void A_registered_mod_blob_is_never_reclaimed()
    {
        var plan = BlobReclamation.PlanModSweep(
            [Old($"{_repoId}/a_mod/1.0.0")],
            Registered(("a_mod", "1.0.0")),
            _cutoff);

        Assert.Empty(plan.Reclaimable);
        Assert.Empty(plan.Retained);
    }

    [Fact]
    public void An_unregistered_mod_blob_past_the_cutoff_is_reclaimed()
    {
        // The case the sweep exists for: an import that uploaded, failed before registering, and was
        // never retried, or a repo deleted back when nothing reclaimed anything.
        var plan = BlobReclamation.PlanModSweep(
            [Old($"{_repoId}/a_mod/1.0.0")],
            Registered(("a_mod", "2.0.0")),
            _cutoff);

        Assert.Equal([$"{_repoId}/a_mod/1.0.0"], plan.Reclaimable.Select(x => x.Name));
    }

    /// <summary>
    /// The hazard the grace period exists for. An import uploads before it registers, so a live
    /// operation always has a window in which its blob is referenced by nothing at all — and deleting
    /// it leaves a registration whose file can never be restored.
    /// </summary>
    [Fact]
    public void An_unregistered_mod_blob_newer_than_the_cutoff_is_retained()
    {
        var plan = BlobReclamation.PlanModSweep(
            [New($"{_repoId}/a_mod/1.0.0")],
            Registered(),
            _cutoff);

        Assert.Empty(plan.Reclaimable);
        Assert.Equal([$"{_repoId}/a_mod/1.0.0"], plan.Retained.Select(x => x.Name));
    }

    [Fact]
    public void A_blob_written_exactly_at_the_cutoff_is_old_enough()
    {
        var plan = BlobReclamation.PlanModSweep(
            [new StoredBlob($"{_repoId}/a_mod/1.0.0", _cutoff)],
            Registered(),
            _cutoff);

        Assert.Single(plan.Reclaimable);
    }

    [Theory]
    [InlineData("a_mod/1.0.0")]
    [InlineData("not-a-guid/a_mod/1.0.0")]
    [InlineData("11111111-1111-1111-1111-111111111111/a_mod")]
    [InlineData("11111111-1111-1111-1111-111111111111/a_mod/1.0.0/extra")]
    [InlineData("11111111-1111-1111-1111-111111111111//1.0.0")]
    public void A_mod_blob_name_the_sweep_cannot_read_is_reported_rather_than_deleted(string name)
    {
        var plan = BlobReclamation.PlanModSweep([Old(name)], Registered(), _cutoff);

        Assert.Empty(plan.Reclaimable);
        Assert.Equal([name], plan.Unrecognised);
    }

    [Fact]
    public void Two_versions_of_one_mod_are_judged_separately()
    {
        var plan = BlobReclamation.PlanModSweep(
            [Old($"{_repoId}/a_mod/1.0.0"), Old($"{_repoId}/a_mod/2.0.0")],
            Registered(("a_mod", "2.0.0")),
            _cutoff);

        Assert.Equal([$"{_repoId}/a_mod/1.0.0"], plan.Reclaimable.Select(x => x.Name));
    }

    /// <summary>
    /// The blob path is the only thing separating two repos' copies of the same mod, so a sweep that
    /// compared only the mod and version would delete one repo's file because another repo has it.
    /// </summary>
    [Fact]
    public void A_blob_under_another_repo_does_not_keep_this_one_alive()
    {
        var otherRepoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var plan = BlobReclamation.PlanModSweep(
            [Old($"{otherRepoId}/a_mod/1.0.0")],
            Registered(("a_mod", "1.0.0")),
            _cutoff);

        Assert.Equal([$"{otherRepoId}/a_mod/1.0.0"], plan.Reclaimable.Select(x => x.Name));
    }

    [Fact]
    public void A_referenced_image_is_never_reclaimed()
    {
        var plan = BlobReclamation.PlanImageSweep([Old($"{_hash[..2]}/{_hash}")], Referenced(_hash), _cutoff);

        Assert.Empty(plan.Reclaimable);
    }

    [Fact]
    public void An_unreferenced_image_past_the_cutoff_is_reclaimed()
    {
        var plan = BlobReclamation.PlanImageSweep([Old($"{_hash[..2]}/{_hash}")], Referenced(_otherHash), _cutoff);

        Assert.Equal([$"{_hash[..2]}/{_hash}"], plan.Reclaimable.Select(x => x.Name));
    }

    [Fact]
    public void An_unreferenced_image_newer_than_the_cutoff_is_retained()
    {
        // Images are uploaded before the version they belong to references them, exactly as mod files
        // are, so they need the same window.
        var plan = BlobReclamation.PlanImageSweep([New($"{_hash[..2]}/{_hash}")], Referenced(), _cutoff);

        Assert.Empty(plan.Reclaimable);
        Assert.Single(plan.Retained);
    }

    [Theory]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("ff/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("e3/not-a-hash")]
    [InlineData("e3/E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    public void An_image_blob_name_that_is_not_this_systems_layout_is_reported_rather_than_deleted(string name)
    {
        var plan = BlobReclamation.PlanImageSweep([Old(name)], Referenced(), _cutoff);

        Assert.Empty(plan.Reclaimable);
        Assert.Equal([name], plan.Unrecognised);
    }

    [Fact]
    public void One_image_referenced_by_several_versions_needs_only_one_reference_to_survive()
    {
        // Content addressing is what makes an image shared across versions, mods and repos, so the
        // referenced set is a set and a single mention is enough.
        var plan = BlobReclamation.PlanImageSweep(
            [Old($"{_hash[..2]}/{_hash}"), Old($"{_otherHash[..2]}/{_otherHash}")],
            Referenced(_hash),
            _cutoff);

        Assert.Equal([$"{_otherHash[..2]}/{_otherHash}"], plan.Reclaimable.Select(x => x.Name));
    }


    private static StoredBlob Old(string name) => new(name, _cutoff.AddMinutes(-1));
    private static StoredBlob New(string name) => new(name, _cutoff.AddMinutes(1));

    private static HashSet<string> Referenced(params string[] hashes)
    {
        return hashes.ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<ModBlobAddress> Registered(params (string ModId, string VersionId)[] versions)
    {
        return versions
            .Select(x => new ModBlobAddress(new RepoId(_repoId), new ModId(x.ModId), new ModVersionId(x.VersionId)))
            .ToHashSet();
    }
}
