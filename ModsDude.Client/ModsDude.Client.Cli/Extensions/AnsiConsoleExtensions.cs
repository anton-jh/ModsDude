﻿using Spectre.Console;

namespace ModsDude.Client.Cli.Extensions;
internal static class AnsiConsoleExtensions
{
    public static void PressAnyKeyToDismiss(this IAnsiConsole ansiConsole)
    {
        ansiConsole.MarkupLine("[blue] <- Press any key to dismiss.[/]");
        Console.ReadKey(true);
    }

    public static void PressAnyKeyToContinue(this IAnsiConsole ansiConsole)
    {
        ansiConsole.MarkupLine("[blue] Press any key to continue. ->[/]");
        Console.ReadKey(true);
    }

    public static void NothingHere(this IAnsiConsole ansiConsole)
    {
        ansiConsole.MarkupLine("[red]Nothing here...[/]");
        ansiConsole.WriteLine();
        ansiConsole.PressAnyKeyToDismiss();
    }
}
