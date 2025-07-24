using ModsDude.Client.Cli.Commands.Profiles;
using ModsDude.Client.Cli.Commands.Repos;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Misc;
internal class MenuCommand(
    IAnsiConsole ansiConsole,
    IServiceProvider serviceProvider)
    : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var root = new SubMenuNode("Main menu", ansiConsole, [
            new CommandNode<OverviewCommand>("Overview", serviceProvider, ansiConsole),
            new CommandNode<RepoMenuCommand>("Repos...", serviceProvider, ansiConsole),
            new GroupNode("Repos", ansiConsole, [
                new CommandNode<CreateRepoCommand>("Create new repo", serviceProvider, ansiConsole),
                new CommandNode<AddMemberCommand>("Add member", serviceProvider, ansiConsole),
            ]),
            new GroupNode("Repos - Admin", ansiConsole, [
                new CommandNode<RepoDetailsCommand>("Repo details", serviceProvider, ansiConsole),
                new CommandNode<EditRepoCommand>("Edit a repo", serviceProvider, ansiConsole),
                new CommandNode<DeleteRepoCommand>("Delete a repo", serviceProvider, ansiConsole),
            ]),
            new GroupNode("Profiles", ansiConsole, [
                new CommandNode<CreateProfileCommand>("Create profile", serviceProvider, ansiConsole),
                new CommandNode<EditProfileCommand>("Edit profile", serviceProvider, ansiConsole),
                new CommandNode<DeleteProfileCommand>("Delete profile", serviceProvider, ansiConsole),
            ]),
            new GroupNode("Misc", ansiConsole, [
                new CommandNode<ReloginCommand>("Re-login / change user", serviceProvider, ansiConsole),
            ])
        ]);

        await root.Select();

        return 0;
    }
}
