﻿using ModsDude.Client.Cli.Authentication;
using ModsDude.Client.Cli.Commands.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Misc;
internal class ReloginCommand(
    IAnsiConsole ansiConsole,
    AuthenticationService authenticationService)
    : AsyncCommandBase<EmptyCommandSettings>(ansiConsole)
{
    public override async Task ExecuteAsync(EmptyCommandSettings _)
    {
        await authenticationService.ForceRelogin(default);
    }
}
