using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Components.ModListEditor;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;
using System;

namespace ModsDude.Client.Cli.Commands.Mods;
internal class ModListEditorCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    ProfileCollector profileCollector)
    : AsyncCommandBase<ModListEditorCommand.Settings>(ansiConsole)

{
    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        //var repo = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Member, cancellationToken);
        //if (repo is null)
        //{
        //    _ansiConsole.NothingHere();
        //    return;
        //}

        //var profile = await profileCollector.Collect(settings.ProfileId, repo, cancellationToken);
        //if (profile is null)
        //{
        //    _ansiConsole.NothingHere();
        //    return;
        //}

        var mods = ModFakers.ModDtoFaker.Generate(20).Select(x => new Mod(x)).ToList();

        var editor = new ModListEditor(
            mods.Take(10),
            mods.Skip(10).Select(x => PickRandom(x.Versions.ToList())),
            _ansiConsole);

        editor.Start();
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid RepoId { get; init; }

        [CommandOption("--profile-id")]
        public Guid ProfileId { get; init; }
    }


    private static readonly Random _random = new();
    public static T PickRandom<T>(List<T> list)
    {
        int index = _random.Next(list.Count);
        return list[index];
    }
}
