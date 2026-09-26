using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Tests.Services;

public class UserAccountServiceTests
{
    private static readonly byte[] _picture = [1, 2, 3, 4];
    private static readonly string _hash = ModImageHashing.Compute(_picture);


    [Fact]
    public async Task A_picture_is_uploaded_at_its_own_address_and_then_made_the_users()
    {
        var images = new FakeImagesClient();
        var users = new FakeUsersClient();
        var service = new UserAccountService(users, images, new FakeImageStore());

        await service.SetAvatar(_picture, "image/webp", CancellationToken.None);

        Assert.Equal([_hash], images.Uploaded);
        Assert.Equal(_hash, users.AvatarSetTo);
    }

    [Fact]
    public async Task A_picture_the_server_already_holds_is_not_uploaded_again()
    {
        var images = new FakeImagesClient(present: _hash);
        var users = new FakeUsersClient();
        var service = new UserAccountService(users, images, new FakeImageStore());

        await service.SetAvatar(_picture, "image/webp", CancellationToken.None);

        Assert.Empty(images.Uploaded);
        Assert.Equal(_hash, users.AvatarSetTo);
    }

    [Fact]
    public async Task The_picture_is_cached_so_the_client_that_made_it_does_not_download_it()
    {
        var store = new FakeImageStore();
        var service = new UserAccountService(new FakeUsersClient(), new FakeImagesClient(), store);

        await service.SetAvatar(_picture, "image/webp", CancellationToken.None);

        Assert.Equal(_picture, store.Put[_hash]);
    }

    [Fact]
    public async Task A_refused_picture_is_not_cached()
    {
        var store = new FakeImageStore();
        var service = new UserAccountService(new FakeUsersClient(refuseAvatar: true), new FakeImagesClient(), store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAvatar(_picture, "image/webp", CancellationToken.None));

        Assert.Empty(store.Put);
    }


    private sealed class FakeImagesClient(string? present = null) : IImagesClient
    {
        public List<string> Uploaded { get; } = [];


        public Task<FileResponse> GetImageV1Async(string hash, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CheckImagesExistResponse> CheckImagesExistV1Async(CheckImagesExistRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new CheckImagesExistResponse { Present = [.. request.Hashes.Where(x => x == present)] });

        public Task UploadImageV1Async(string hash, FileParameter? image = null, CancellationToken cancellationToken = default)
        {
            Uploaded.Add(hash);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUsersClient(bool refuseAvatar = false) : IUsersClient
    {
        public string? AvatarSetTo { get; private set; }


        public Task<ICollection<UserDto>> GetUsersV1Async(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CurrentUserDto> GetCurrentUserV1Async(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CurrentUserDto> SetDisplayNameV1Async(SetDisplayNameRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CurrentUserDto> SetAvatarV1Async(SetAvatarRequest request, CancellationToken cancellationToken = default)
        {
            if (refuseAvatar)
            {
                throw new InvalidOperationException("Refused.");
            }

            AvatarSetTo = request.Hash;
            return Task.FromResult(new CurrentUserDto { Id = "me", DisplayName = "Me", Tag = "0001", AvatarHash = request.Hash });
        }

        public Task<CurrentUserDto> RemoveAvatarV1Async(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeImageStore : IModImageStore
    {
        public Dictionary<string, byte[]> Put { get; } = [];


        public Task<byte[]> GetAsync(string hash, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task PutAsync(string hash, byte[] bytes, CancellationToken cancellationToken)
        {
            Put[hash] = bytes;
            return Task.CompletedTask;
        }
    }
}
