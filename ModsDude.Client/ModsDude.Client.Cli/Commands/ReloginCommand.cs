using ModsDude.Client.Cli.Authentication;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands;
internal class ReloginCommand(
    AuthenticationService authenticationService)
    : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        await authenticationService.ForceRelogin(default);
        return 0;
    }
}
