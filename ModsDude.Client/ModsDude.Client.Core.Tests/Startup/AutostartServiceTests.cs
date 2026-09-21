using ModsDude.Client.Core.Startup;

namespace ModsDude.Client.Core.Tests.Startup;

public class AutostartServiceTests
{
    private const string _exe = @"C:\Program Files\ModsDude\ModsDude.exe";
    private const string _name = "ModsDude";

    private static string Expected => $"\"{_exe}\" --background";

    private static byte[] Approval(byte first) => [first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];


    [Fact]
    public void An_install_that_is_not_production_can_neither_report_nor_register()
    {
        var registry = new FakeStartupRegistry();
        var service = new AutostartService(registry, _name, _exe, isAvailable: false);

        Assert.Equal(AutostartState.NotAvailable, service.State);
        Assert.Throws<InvalidOperationException>(service.Enable);
        Assert.Throws<InvalidOperationException>(service.Disable);
        Assert.False(service.Reconcile());
        Assert.False(registry.Run.ContainsKey(_name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    public void There_is_nothing_stable_to_register_when_running_under_the_dotnet_host(string? path)
    {
        var service = new AutostartService(new FakeStartupRegistry(), _name, path, isAvailable: true);

        Assert.Equal(AutostartState.NotAvailable, service.State);
    }

    [Fact]
    public void Enabling_registers_a_quoted_command_that_starts_in_the_background()
    {
        var registry = new FakeStartupRegistry();
        var service = new AutostartService(registry, _name, _exe, isAvailable: true);

        service.Enable();

        Assert.Equal(Expected, registry.Run[_name]);
        Assert.Equal(AutostartState.On, service.State);
    }

    [Fact]
    public void Disabling_removes_the_entry()
    {
        var registry = new FakeStartupRegistry();
        var service = new AutostartService(registry, _name, _exe, isAvailable: true);
        service.Enable();

        service.Disable();

        Assert.Equal(AutostartState.Off, service.State);
        Assert.False(registry.Run.ContainsKey(_name));
    }

    [Theory]
    [InlineData((byte)3, AutostartState.DisabledInWindows)]
    [InlineData((byte)7, AutostartState.DisabledInWindows)]
    [InlineData((byte)2, AutostartState.On)]
    [InlineData((byte)6, AutostartState.On)]
    public void Windows_own_switch_wins_over_the_registration(byte first, AutostartState expected)
    {
        var registry = new FakeStartupRegistry();
        registry.Run[_name] = Expected;
        registry.Approvals[_name] = Approval(first);

        Assert.Equal(expected, new AutostartService(registry, _name, _exe, isAvailable: true).State);
    }

    [Fact]
    public void An_entry_nobody_ever_switched_off_is_on()
    {
        var registry = new FakeStartupRegistry();
        registry.Run[_name] = Expected;

        Assert.Equal(AutostartState.On, new AutostartService(registry, _name, _exe, isAvailable: true).State);
    }

    [Fact]
    public void Ticking_the_box_again_clears_a_switch_off_in_windows_or_it_would_do_nothing()
    {
        var registry = new FakeStartupRegistry();
        registry.Run[_name] = Expected;
        registry.Approvals[_name] = Approval(3);
        var service = new AutostartService(registry, _name, _exe, isAvailable: true);

        service.Enable();

        Assert.Equal(AutostartState.On, service.State);
    }

    [Fact]
    public void A_stale_path_is_repaired()
    {
        var registry = new FakeStartupRegistry();
        registry.Run[_name] = "\"C:\\Old\\ModsDude.exe\" --background";
        var service = new AutostartService(registry, _name, _exe, isAvailable: true);

        Assert.True(service.Reconcile());
        Assert.Equal(Expected, registry.Run[_name]);
        Assert.False(service.Reconcile());
    }

    [Fact]
    public void An_absent_entry_is_the_users_choice_and_is_not_put_back()
    {
        var registry = new FakeStartupRegistry();
        var service = new AutostartService(registry, _name, _exe, isAvailable: true);

        Assert.False(service.Reconcile());
        Assert.False(registry.Run.ContainsKey(_name));
    }

    [Fact]
    public void An_entry_windows_has_disabled_is_repaired_but_not_re_enabled()
    {
        var registry = new FakeStartupRegistry();
        registry.Run[_name] = "\"C:\\Old\\ModsDude.exe\" --background";
        registry.Approvals[_name] = Approval(3);
        var service = new AutostartService(registry, _name, _exe, isAvailable: true);

        service.Reconcile();

        Assert.Equal(AutostartState.DisabledInWindows, service.State);
    }


    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        public Dictionary<string, string> Run { get; } = [];
        public Dictionary<string, byte[]> Approvals { get; } = [];

        public string? GetRunCommand(string name) => Run.GetValueOrDefault(name);
        public void SetRunCommand(string name, string command) => Run[name] = command;
        public void RemoveRunCommand(string name) => Run.Remove(name);
        public byte[]? GetApproval(string name) => Approvals.GetValueOrDefault(name);
        public void RemoveApproval(string name) => Approvals.Remove(name);
    }
}
