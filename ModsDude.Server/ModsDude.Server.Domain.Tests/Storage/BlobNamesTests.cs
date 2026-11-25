using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Storage;

public class BlobNamesTests
{
    private const string _hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static readonly RepoId _repoId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly SavegameId _savegameId = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));


    [Fact]
    public void A_mod_name_reads_back_as_the_version_it_was_built_from()
    {
        var name = BlobNames.Mod(_repoId, new ModId("a_mod"), new ModVersionId("1.0.0"));

        Assert.Equal("11111111-1111-1111-1111-111111111111/a_mod/1.0.0", name);
        Assert.True(BlobNames.TryParseMod(name, out var address));
        Assert.Equal(new ModBlobAddress(_repoId, new ModId("a_mod"), new ModVersionId("1.0.0")), address);
    }

    [Fact]
    public void A_savegame_name_reads_back_as_the_repo_the_save_and_the_content()
    {
        var name = BlobNames.Savegame(_repoId, _savegameId, _hash);

        Assert.True(BlobNames.TryParseSavegame(name, out var address));
        Assert.Equal(new SavegameBlobAddress(_repoId, _savegameId, _hash), address);
    }

    [Fact]
    public void An_upload_name_reads_back_as_the_repo_the_uploader_and_the_upload()
    {
        var uploadId = new UploadId(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var name = BlobNames.Upload(_repoId, new UserId("user-1"), uploadId);

        Assert.Equal("uploads/11111111-1111-1111-1111-111111111111/user-1/44444444-4444-4444-4444-444444444444", name);
        Assert.True(BlobNames.TryParseUpload(name, out var address));
        Assert.Equal(new UploadBlobAddress(_repoId, new UserId("user-1"), uploadId), address);
    }

    [Theory]
    [InlineData(StorageContainer.Mods)]
    [InlineData(StorageContainer.Savegames)]
    public void An_upload_is_recognised_in_the_mods_and_savegames_containers_and_owned_by_its_repo(StorageContainer container)
    {
        var name = BlobNames.Upload(_repoId, new UserId("user-1"), new UploadId(Guid.NewGuid()));

        Assert.True(BlobNames.IsRecognised(container, name));
        Assert.Equal(_repoId, BlobNames.OwningRepo(container, name));
        Assert.False(BlobNames.TryParseMod(name, out _));
        Assert.False(BlobNames.TryParseSavegame(name, out _));
    }

    [Theory]
    [InlineData("uploads/11111111-1111-1111-1111-111111111111/user-1")]
    [InlineData("staged/11111111-1111-1111-1111-111111111111/user-1/44444444-4444-4444-4444-444444444444")]
    [InlineData("uploads/not-a-guid/user-1/44444444-4444-4444-4444-444444444444")]
    [InlineData("uploads/11111111-1111-1111-1111-111111111111/user-1/not-a-guid")]
    [InlineData("uploads/11111111-1111-1111-1111-111111111111/ user-1/44444444-4444-4444-4444-444444444444")]
    public void An_upload_name_that_is_not_the_layout_is_not_recognised(string name)
    {
        Assert.False(BlobNames.TryParseUpload(name, out _));
        Assert.False(BlobNames.IsRecognised(StorageContainer.Mods, name));
    }

    [Fact]
    public void An_upload_is_never_recognised_in_the_images_container()
    {
        Assert.False(BlobNames.IsRecognised(StorageContainer.Images, BlobNames.Upload(_repoId, new UserId("user-1"), new UploadId(Guid.NewGuid()))));
    }

    [Fact]
    public void An_image_name_is_prefixed_with_the_first_two_characters_of_its_hash()
    {
        var name = BlobNames.Image(_hash);

        Assert.Equal($"e3/{_hash}", name);
        Assert.True(BlobNames.TryParseImage(name, out var hash));
        Assert.Equal(_hash, hash);
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111/a_mod")]
    [InlineData("11111111-1111-1111-1111-111111111111/a_mod/1.0.0/extra")]
    [InlineData("not-a-guid/a_mod/1.0.0")]
    [InlineData("11111111111111111111111111111111/a_mod/1.0.0")]
    [InlineData("11111111-1111-1111-1111-11111111111A/a_mod/1.0.0")]
    [InlineData("11111111-1111-1111-1111-111111111111//1.0.0")]
    [InlineData("11111111-1111-1111-1111-111111111111/a_mod/1.0.")]
    public void A_mod_name_that_is_not_the_layout_is_not_recognised(string name)
    {
        Assert.False(BlobNames.IsRecognised(StorageContainer.Mods, name));
        Assert.Null(BlobNames.OwningRepo(StorageContainer.Mods, name));
    }

    [Theory]
    [InlineData("33333333-3333-3333-3333-333333333333/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("11111111-1111-1111-1111-111111111111/33333333-3333-3333-3333-333333333333/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/extra")]
    [InlineData("11111111-1111-1111-1111-111111111111/not-a-guid/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("11111111-1111-1111-1111-111111111111/33333333-3333-3333-3333-333333333333/E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData("11111111-1111-1111-1111-111111111111/33333333-3333-3333-3333-333333333333/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85")]
    [InlineData("11111111-1111-1111-1111-111111111111/33333333-3333-3333-3333-333333333333/")]
    public void A_savegame_name_that_is_not_the_layout_is_not_recognised(string name)
    {
        Assert.False(BlobNames.IsRecognised(StorageContainer.Savegames, name));
    }

    [Theory]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("ab/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("e3/E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData("e3/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/extra")]
    public void An_image_name_that_is_not_the_layout_is_not_recognised(string name)
    {
        Assert.False(BlobNames.IsRecognised(StorageContainer.Images, name));
    }

    [Fact]
    public void The_owning_repo_is_read_off_mod_and_savegame_names_but_never_off_images()
    {
        Assert.Equal(_repoId, BlobNames.OwningRepo(StorageContainer.Mods, BlobNames.Mod(_repoId, new ModId("a_mod"), new ModVersionId("1"))));
        Assert.Equal(_repoId, BlobNames.OwningRepo(StorageContainer.Savegames, BlobNames.Savegame(_repoId, _savegameId, _hash)));
        Assert.Null(BlobNames.OwningRepo(StorageContainer.Images, BlobNames.Image(_hash)));
    }

    [Fact]
    public void The_marker_is_never_a_recognised_blob()
    {
        Assert.All(StorageContainerNames.All, x => Assert.False(BlobNames.IsRecognised(x, StorageBinding.MarkerBlobName)));
    }
}
