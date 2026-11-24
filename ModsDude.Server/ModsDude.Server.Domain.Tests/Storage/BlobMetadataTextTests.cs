using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;
using System.Globalization;

namespace ModsDude.Server.Domain.Tests.Storage;

public class BlobMetadataTextTests
{
    private const int _metadataLimit = 8 * 1024;


    [Theory]
    [InlineData("Gården")]
    [InlineData("農場の拡張")]
    [InlineData("tractor 🚜")]
    public void A_value_round_trips(string value)
    {
        Assert.Equal(value, BlobMetadataText.Decode(BlobMetadataText.Encode(value)));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("農")]
    [InlineData("👨‍👩‍👧")]
    public void A_long_value_is_cut_short_at_a_whole_character(string repeated)
    {
        var value = string.Concat(Enumerable.Repeat(repeated, 1000));

        var encoded = BlobMetadataText.Encode(value);
        var decoded = BlobMetadataText.Decode(encoded);

        Assert.InRange(encoded.Length, 1, BlobMetadataText.MaximumEncodedLength);
        Assert.EndsWith("…", decoded);

        var kept = decoded[..^1];
        Assert.StartsWith(kept, value, StringComparison.Ordinal);
        Assert.True(kept.Length % repeated.Length == 0);
    }

    [Fact]
    public void A_label_at_its_longest_fits_in_a_blobs_metadata()
    {
        var huge = string.Concat(Enumerable.Repeat("農", 5000));
        var label = new ModBlobLabel(
            new RepoId(Guid.NewGuid()), huge, new AdapterIdentifier("FarmingSimulator25"), new UserId("user-1"), huge, DateTime.UnixEpoch,
            new ModId("a_mod"), new ModVersionId("1.0.0.0"), huge, huge);
        var existing = new Dictionary<string, string> { ["sha256"] = new string('a', 64) };

        var metadata = label.ApplyTo(existing);

        Assert.InRange(metadata.Sum(x => x.Key.Length + x.Value.Length), 0, _metadataLimit);
    }
}
