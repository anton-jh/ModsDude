using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Tests.Profiles;

namespace ModsDude.Client.Core.Tests.Services;

public class ProfileServiceTests
{
    private static readonly Guid _repoId = Guid.NewGuid();


    [Fact]
    public async Task Finding_a_profile_asks_the_server_and_includes_archived_ones()
    {
        var server = new FakeProfilesServer();
        var archived = new ProfileDto
        {
            Id = Guid.NewGuid(),
            RepoId = _repoId,
            Name = "Season 3",
            HeadRevision = 2,
            ArchivedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        server.Profiles.Add(archived);

        var service = new ProfileService(server, null!, null!);
        var found = await service.FindProfile(_repoId, archived.Id, CancellationToken.None);

        Assert.Equal("Season 3", found?.Name);
        Assert.NotNull(found?.ArchivedAt);
    }

    [Fact]
    public async Task Finding_a_deleted_profile_answers_null()
    {
        var service = new ProfileService(new FakeProfilesServer(), null!, null!);

        Assert.Null(await service.FindProfile(_repoId, Guid.NewGuid(), CancellationToken.None));
    }
}
