using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Tests;

public class AppIdentityTests
{
    [Theory]
    [InlineData("Production", "ModsDude")]
    [InlineData("production", "ModsDude")]
    [InlineData("", "ModsDude")]
    [InlineData("  ", "ModsDude")]
    public void Production_is_the_bare_name_so_existing_installs_keep_their_files(string environment, string expected)
    {
        Assert.Equal(expected, AppIdentity.NameFor(environment));
    }

    [Theory]
    [InlineData("Development", "ModsDude.Development")]
    [InlineData("Staging", "ModsDude.Staging")]
    [InlineData(" Staging ", "ModsDude.Staging")]
    public void Any_other_environment_gets_a_folder_of_its_own(string environment, string expected)
    {
        Assert.Equal(expected, AppIdentity.NameFor(environment));
    }

    [Fact]
    public void Two_environments_never_share_a_name()
    {
        Assert.NotEqual(AppIdentity.NameFor("Production"), AppIdentity.NameFor("Development"));
    }

    [Fact]
    public void An_unconfigured_process_is_production_and_keeps_the_names_it_always_had()
    {
        Assert.True(AppIdentity.IsProduction);
        Assert.Equal("ModsDude", AppIdentity.Name);
        Assert.EndsWith(Path.Combine("ModsDude", "store"), ContentStoreSettings.GetDefaultPath(@"D:\"));
    }
}
