using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

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


        var editor = new ModListEditor(
            available: ModFakers.ModDtoFaker.Generate(3).Select(x => new Mod(x)),
            active: ModFakers.ModDtoFaker.Generate(50).Select(x => new Mod(x).Latest),
            ansiConsole: _ansiConsole);

        editor.Run();
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid RepoId { get; init; }

        [CommandOption("--profile-id")]
        public Guid ProfileId { get; init; }
    }
}
