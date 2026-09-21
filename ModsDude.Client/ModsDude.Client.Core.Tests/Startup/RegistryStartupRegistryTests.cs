using Microsoft.Win32;
using ModsDude.Client.Core.Startup;
using System.Runtime.Versioning;

namespace ModsDude.Client.Core.Tests.Startup;

/// <summary>
/// The real registry, under a value name nothing else uses and removed afterwards - the paths and value
/// kinds are the part a fake cannot vouch for. Windows only, like the rest of the client.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryStartupRegistryTests : IDisposable
{
    private const string _approvalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string _name = $"ModsDude.Tests.{Guid.NewGuid():N}";
    private readonly RegistryStartupRegistry _registry = new();


    [Fact]
    public void A_command_round_trips_and_is_removed()
    {
        Assert.Null(_registry.GetRunCommand(_name));

        _registry.SetRunCommand(_name, "\"C:\\x y\\a.exe\" --background");

        Assert.Equal("\"C:\\x y\\a.exe\" --background", _registry.GetRunCommand(_name));

        _registry.RemoveRunCommand(_name);

        Assert.Null(_registry.GetRunCommand(_name));
    }

    [Fact]
    public void Removing_what_is_not_there_is_not_an_error()
    {
        _registry.RemoveRunCommand(_name);
        _registry.RemoveApproval(_name);
    }

    [Fact]
    public void Windows_switch_off_is_read_from_and_cleared_at_the_key_task_manager_uses()
    {
        Assert.Null(_registry.GetApproval(_name));

        using (var key = Registry.CurrentUser.CreateSubKey(_approvalKey))
        {
            key.SetValue(_name, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        var approval = _registry.GetApproval(_name);

        Assert.NotNull(approval);
        Assert.False(AutostartService.IsApproved(approval));

        _registry.RemoveApproval(_name);

        Assert.Null(_registry.GetApproval(_name));
    }

    public void Dispose()
    {
        _registry.RemoveRunCommand(_name);
        _registry.RemoveApproval(_name);
    }
}
