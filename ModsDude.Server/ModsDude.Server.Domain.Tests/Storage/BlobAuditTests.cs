using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Domain.Tests.Storage;

public class BlobAuditTests
{
    private const string _repo = "11111111-1111-1111-1111-111111111111";
    private const string _hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";


    [Fact]
    public void A_referenced_blob_is_not_a_finding()
    {
        var result = BlobAudit.Classify(StorageContainer.Mods, [Labelled($"{_repo}/a_mod/1")], Referenced($"{_repo}/a_mod/1"));

        Assert.Empty(result.Unreferenced);
        Assert.Empty(result.Unrecognised);
        Assert.Empty(result.Missing);
        Assert.Empty(result.Unlabelled);
    }

    [Fact]
    public void A_recognised_blob_nothing_refers_to_is_unreferenced()
    {
        var result = BlobAudit.Classify(StorageContainer.Mods, [Labelled($"{_repo}/a_mod/1")], Referenced());

        Assert.Equal([$"{_repo}/a_mod/1"], result.Unreferenced.Select(x => x.Name));
    }

    [Fact]
    public void A_name_that_does_not_parse_is_unrecognised_and_never_unreferenced()
    {
        var result = BlobAudit.Classify(StorageContainer.Mods, [Labelled("stray.txt")], Referenced());

        Assert.Empty(result.Unreferenced);
        Assert.Equal(["stray.txt"], result.Unrecognised.Select(x => x.Name));
    }

    [Fact]
    public void A_referenced_name_the_listing_did_not_hold_is_missing()
    {
        var result = BlobAudit.Classify(StorageContainer.Mods, [], Referenced($"{_repo}/a_mod/1"));

        Assert.Equal([$"{_repo}/a_mod/1"], result.Missing);
    }

    [Fact]
    public void A_referenced_blob_with_no_label_is_listed_for_labelling()
    {
        var result = BlobAudit.Classify(StorageContainer.Savegames, [Unlabelled($"{_repo}/33333333-3333-3333-3333-333333333333/{_hash}")], Referenced($"{_repo}/33333333-3333-3333-3333-333333333333/{_hash}"));

        Assert.Single(result.Unlabelled);
    }

    [Fact]
    public void Images_are_never_listed_for_labelling()
    {
        var result = BlobAudit.Classify(StorageContainer.Images, [Unlabelled($"e3/{_hash}")], Referenced($"e3/{_hash}"));

        Assert.Empty(result.Unlabelled);
    }

    [Fact]
    public void Findings_come_out_in_name_order_whatever_order_the_listing_had()
    {
        var result = BlobAudit.Classify(
            StorageContainer.Mods,
            [Labelled($"{_repo}/c/1"), Labelled($"{_repo}/a/1"), Labelled($"{_repo}/b/1")],
            Referenced($"{_repo}/z/1", $"{_repo}/y/1"));

        Assert.Equal([$"{_repo}/a/1", $"{_repo}/b/1", $"{_repo}/c/1"], result.Unreferenced.Select(x => x.Name));
        Assert.Equal([$"{_repo}/y/1", $"{_repo}/z/1"], result.Missing);
    }


    private static StoredBlob Labelled(string name)
    {
        return new StoredBlob(name, DateTimeOffset.UnixEpoch, 1, "v", new Dictionary<string, string> { [BlobLabel.RepoIdKey] = _repo });
    }

    private static StoredBlob Unlabelled(string name)
    {
        return new StoredBlob(name, DateTimeOffset.UnixEpoch, 1, "v", new Dictionary<string, string>());
    }

    private static HashSet<string> Referenced(params string[] names)
    {
        return names.ToHashSet(StringComparer.Ordinal);
    }
}
