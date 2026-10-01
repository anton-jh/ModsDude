using ModsDude.Client.Core.Builds;

namespace ModsDude.Client.Core.Tests.Builds;

public class AppBuildTests
{
    [Fact]
    public void A_released_build_is_its_number()
    {
        Assert.Equal("b42", AppBuild.Describe(new BuildNumber(42), "2fae73c3ca349a6aaf5302b2631c17b25a54c58f"));
    }

    [Fact]
    public void A_local_build_is_dev_and_its_short_commit()
    {
        Assert.Equal("dev (2fae73c)", AppBuild.Describe(new BuildNumber(0), "2fae73c3ca349a6aaf5302b2631c17b25a54c58f"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_local_build_without_a_commit_is_just_dev(string? commit)
    {
        Assert.Equal("dev", AppBuild.Describe(new BuildNumber(0), commit));
    }

    [Fact]
    public void This_build_is_stamped()
    {
        Assert.True(BuildNumber.Current.Value >= 0);
        Assert.StartsWith(BuildNumber.Current.ToString(), AppBuild.Description);
    }
}
