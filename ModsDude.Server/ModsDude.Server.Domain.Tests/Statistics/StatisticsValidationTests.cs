using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Statistics;

public class StatisticsValidationTests
{
    private static readonly RepoId _repoId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly DateOnly _date = new(2026, 10, 3);


    [Fact]
    public void A_transfer_cannot_have_a_negative_size()
    {
        Assert.Throws<DomainValidationException>(() =>
            new FileTransfer(_repoId, new UserId("user"), TransferredFile.Mod, TransferDirection.Upload, -1, DateTime.UnixEpoch));
    }

    [Fact]
    public void An_empty_file_is_a_valid_transfer()
    {
        var transfer = new FileTransfer(_repoId, new UserId("user"), TransferredFile.Savegame, TransferDirection.Download, 0, DateTime.UnixEpoch);

        Assert.Equal(0, transfer.SizeBytes);
    }

    [Theory]
    [InlineData(-1, 0, 0L)]
    [InlineData(0, -1, 0L)]
    [InlineData(0, 0, -1L)]
    public void A_storage_sample_cannot_hold_negative_amounts(long storedBytes, int blobCount, long? registeredBytes)
    {
        Assert.Throws<DomainValidationException>(() =>
            new StorageUsageSample(_date, StorageContainer.Mods, _repoId, storedBytes, blobCount, registeredBytes));
    }

    [Fact]
    public void A_storage_sample_may_have_no_registered_bytes()
    {
        var sample = new StorageUsageSample(_date, StorageContainer.Images, null, 10, 1, null);

        Assert.Null(sample.RegisteredBytes);
    }
}
