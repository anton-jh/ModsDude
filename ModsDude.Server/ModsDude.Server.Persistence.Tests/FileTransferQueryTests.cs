using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

[Collection(nameof(DatabaseCollection))]
public class FileTransferQueryTests(DatabaseFixture fixture)
{
    private static readonly DateTime _since = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);


    [Fact]
    public async Task Transfers_since_a_time_are_totalled_per_repo_user_file_and_direction()
    {
        var (repoId, userId) = await GivenARepoAndUser();

        await GivenTransfers(
            new FileTransfer(repoId, userId, TransferredFile.Mod, TransferDirection.Download, 100, _since),
            new FileTransfer(repoId, userId, TransferredFile.Mod, TransferDirection.Download, 50, _since.AddDays(3)),
            new FileTransfer(repoId, userId, TransferredFile.Mod, TransferDirection.Upload, 7, _since.AddDays(1)),
            new FileTransfer(repoId, userId, TransferredFile.Savegame, TransferDirection.Download, 1000, _since.AddDays(2)),
            new FileTransfer(repoId, userId, TransferredFile.Mod, TransferDirection.Download, 9999, _since.AddTicks(-1)));

        using var dbContext = fixture.CreateDbContext();

        var totals = (await dbContext.FileTransfers.GetTotalsSinceAsync(_since, CancellationToken.None))
            .Where(x => x.RepoId == repoId)
            .OrderBy(x => x.File)
            .ThenBy(x => x.Direction)
            .ToList();

        Assert.Equal(
            [
                new FileTransferTotal(repoId, userId, TransferredFile.Mod, TransferDirection.Upload, 1, 7),
                new FileTransferTotal(repoId, userId, TransferredFile.Mod, TransferDirection.Download, 2, 150),
                new FileTransferTotal(repoId, userId, TransferredFile.Savegame, TransferDirection.Download, 1, 1000)
            ],
            totals);
    }

    [Fact]
    public async Task Transfers_outlive_the_repo_they_belong_to()
    {
        var (repoId, userId) = await GivenARepoAndUser();
        await GivenTransfers(new FileTransfer(repoId, userId, TransferredFile.Mod, TransferDirection.Download, 100, _since));

        using (var dbContext = fixture.CreateDbContext())
        {
            var repo = await dbContext.Repos.FindAsync([repoId], CancellationToken.None);
            dbContext.Repos.Remove(repo!);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            var totals = await dbContext.FileTransfers.GetTotalsSinceAsync(_since, CancellationToken.None);

            Assert.Single(totals, x => x.RepoId == repoId);
        }
    }


    private async Task<(RepoId RepoId, UserId UserId)> GivenARepoAndUser()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var user = new User(userId, new DisplayName(userId.Value), DateTime.UtcNow);
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, user)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(user);
        dbContext.Repos.Add(repo);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, userId);
    }

    private async Task GivenTransfers(params FileTransfer[] transfers)
    {
        using var dbContext = fixture.CreateDbContext();

        dbContext.FileTransfers.AddRange(transfers);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
