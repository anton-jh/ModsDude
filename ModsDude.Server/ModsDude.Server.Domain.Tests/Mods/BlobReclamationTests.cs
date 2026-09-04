using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Domain.Tests.Mods;

public class BlobReclamationTests
{
    private static readonly Guid _repoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _savegameId = Guid.Parse("33333333-3333-3333-3333-333333333333");
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


    [Fact]
    public void A_checked_in_savegame_blob_is_never_reclaimed()
    {
        var plan = BlobReclamation.PlanSavegameSweep(
            [Old($"{_repoId}/{_savegameId}/{_hash}")],
            RegisteredSaves(_hash),
            _cutoff);

        Assert.Empty(plan.Reclaimable);
        Assert.Empty(plan.Retained);
    }

    [Fact]
    public void An_unreferenced_savegame_blob_past_the_cutoff_is_reclaimed()
    {
        // A check-out that uploaded and then failed before checking in, or a savegame whose old
        // versions the retention policy has since dropped.
        var plan = BlobReclamation.PlanSavegameSweep(
            [Old($"{_repoId}/{_savegameId}/{_hash}")],
            RegisteredSaves(_otherHash),
            _cutoff);

        Assert.Equal([$"{_repoId}/{_savegameId}/{_hash}"], plan.Reclaimable.Select(x => x.Name));
    }

    /// <summary>
    /// The hazard is identical to the mod one and so is the guard: a client mints an upload link,
    /// writes the packed save, and only then checks it in, so a live check-in always has a window in
    /// which its blob is referred to by nothing at all.
    /// </summary>
    [Fact]
    public void An_unreferenced_savegame_blob_newer_than_the_cutoff_is_retained()
    {
        var plan = BlobReclamation.PlanSavegameSweep(
            [New($"{_repoId}/{_savegameId}/{_hash}")],
            RegisteredSaves(),
            _cutoff);

        Assert.Empty(plan.Reclaimable);
        Assert.Equal([$"{_repoId}/{_savegameId}/{_hash}"], plan.Retained.Select(x => x.Name));
    }

    /// <summary>
    /// A savegame's bytes are addressed by content rather than by version number, so a restore is a
    /// metadata operation and several versions legitimately point at one blob. The registered set is
    /// therefore a set of addresses rather than one entry per version, and a single mention has to be
    /// enough - otherwise restoring an old version and then pruning the original would delete the
    /// bytes the restore is made of.
    /// </summary>
    [Fact]
    public void One_savegame_blob_shared_by_two_versions_survives_on_one_reference()
    {
        var registered = RegisteredSaves(_hash, _hash);

        Assert.Single(registered);

        var plan = BlobReclamation.PlanSavegameSweep(
            [Old($"{_repoId}/{_savegameId}/{_hash}"), Old($"{_repoId}/{_savegameId}/{_otherHash}")],
            registered,
            _cutoff);

        Assert.Equal([$"{_repoId}/{_savegameId}/{_otherHash}"], plan.Reclaimable.Select(x => x.Name));
    }

    /// <summary>
    /// The path is the only thing separating two savegames' identical bytes, so a sweep that compared
    /// only the hash would delete one save's blob because another save happens to hold the same
    /// content - two check-outs of the same version played by nobody, for instance.
    /// </summary>
    [Fact]
    public void A_blob_under_another_savegame_does_not_keep_this_one_alive()
    {
        var otherSavegameId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var plan = BlobReclamation.PlanSavegameSweep(
            [Old($"{_repoId}/{otherSavegameId}/{_hash}")],
            RegisteredSaves(_hash),
            _cutoff);

        Assert.Equal([$"{_repoId}/{otherSavegameId}/{_hash}"], plan.Reclaimable.Select(x => x.Name));
    }

    [Fact]
    public void A_savegame_blob_name_reads_as_the_repo_the_save_and_the_content()
    {
        Assert.True(BlobReclamation.TryParseSavegameBlobName($"{_repoId}/{_savegameId}/{_hash}", out var address));

        Assert.Equal(new SavegameBlobAddress(new RepoId(_repoId), new SavegameId(_savegameId), _hash), address);
    }

    [Theory]
    [InlineData("33333333-3333-3333-3333-333333333333/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("11111111-1111-1111-1111-111111111111/33333333-3333-3333-3333-333333333333/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/extra")]
    [InlineData("not-a-guid/33333333-3333-3333-3333-333333333333/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("11111111-1111-1111-1111-111111111111/not-a-guid/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("11111111-1111-1111-1111-111111111111/33333333-3333-3333-3333-333333333333/E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData("11111111-1111-1111-1111-111111111111/33333333-3333-3333-3333-333333333333/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85")]
    [InlineData("11111111-1111-1111-1111-111111111111//e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("11111111-1111-1111-1111-111111111111/33333333-3333-3333-3333-333333333333/")]
    public void A_savegame_blob_name_the_sweep_cannot_read_is_reported_rather_than_deleted(string name)
    {
        Assert.False(BlobReclamation.TryParseSavegameBlobName(name, out _));

        var plan = BlobReclamation.PlanSavegameSweep([Old(name)], RegisteredSaves(), _cutoff);

        Assert.Empty(plan.Reclaimable);
        Assert.Equal([name], plan.Unrecognised);
    }


    private static StoredBlob Old(string name) => new(name, _cutoff.AddMinutes(-1));
    private static StoredBlob New(string name) => new(name, _cutoff.AddMinutes(1));

    private static HashSet<string> Referenced(params string[] hashes)
    {
        return hashes.ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<SavegameBlobAddress> RegisteredSaves(params string[] contentHashes)
    {
        return contentHashes
            .Select(x => new SavegameBlobAddress(new RepoId(_repoId), new SavegameId(_savegameId), x))
            .ToHashSet();
    }

    private static HashSet<ModBlobAddress> Registered(params (string ModId, string VersionId)[] versions)
    {
        return versions
            .Select(x => new ModBlobAddress(new RepoId(_repoId), new ModId(x.ModId), new ModVersionId(x.VersionId)))
            .ToHashSet();
    }
}
