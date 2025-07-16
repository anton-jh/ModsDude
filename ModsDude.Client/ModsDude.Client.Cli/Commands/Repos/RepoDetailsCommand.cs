using ModsDude.Client.Cli.Commands.Abstractions;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Reflection;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class RepoDetailsCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    IGameAdapterIndex gameAdapterIndex)
    : AsyncCommandBase<RepoDetailsCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings, bool runFromMenu, CancellationToken cancellationToken)
    {
        var repoMembership = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Admin, cancellationToken);

        _ansiConsole.If(runFromMenu)?.Clear();

        _ansiConsole.MarkupLineInterpolated($"([yellow]{repoMembership.Repo.Id}[/]) [yellow]{repoMembership.Repo.Name}[/]");

        var gameAdapter = gameAdapterIndex.GetById(GameAdapterId.Parse(repoMembership.Repo.AdapterId));
        var gameAdapterBaseSettings = gameAdapter.DeserializeBaseSettings(repoMembership.Repo.AdapterConfiguration);

        foreach (var property in gameAdapterBaseSettings.GetType().GetProperties())
        {
            var title = property.GetCustomAttribute<TitleAttribute>()?.Text ?? property.Name;
            var value = property.GetValue(gameAdapterBaseSettings);

            _ansiConsole.MarkupLineInterpolated($"{title}: {value}");
        }

        _ansiConsole.If(runFromMenu)?.PressAnyKeyToDismiss();
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid? RepoId { get; init; }
    }
}
