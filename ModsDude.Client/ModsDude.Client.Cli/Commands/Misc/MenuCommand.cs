using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Cli.Commands.Profiles;
using ModsDude.Client.Cli.Commands.Repos;
using ModsDude.Client.Cli.Commands.Shared;
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
            new GroupNode("Repos", ansiConsole, [
                new CommandNode<CreateRepoCommand>("Create new repo", serviceProvider, ansiConsole),
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


internal abstract class MenuNode(string label, IAnsiConsole ansiConsole)
{
    protected readonly IAnsiConsole _ansiConsole = ansiConsole;

    public string Label { get; } = label;

    public abstract Task Select();
}


internal class CommandNode<TCommand>(
    string label,
    IServiceProvider serviceProvider,
    IAnsiConsole ansiConsole)
    : MenuNode(label, ansiConsole)
    where TCommand : class, IInteractiveCommand
{
    public override async Task Select()
    {
        using var cts = new CancellationTokenSource();

        void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            cts.Cancel();
            e.Cancel = true;
        }

        var commandInstance = serviceProvider.GetRequiredService<TCommand>();

        try
        {
            Console.CancelKeyPress += OnCancel;
            await commandInstance.ExecuteAsync(cts.Token);
        }
        catch (OperationCanceledException)
        { }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }
    }
}


internal class SubMenuNode(string label, IAnsiConsole ansiConsole, IEnumerable<MenuNode> children)
    : MenuNode(label, ansiConsole)
{
    public IEnumerable<MenuNode> Children { get; } = children;

    public override async Task Select()
    {
        while (true)
        {
            _ansiConsole.Clear();

            var prompt = new SelectionPrompt<MenuNode>()
                .Title(Label)
                .PageSize(10)
                .UseConverter(x => x.Label)
                .EnableSearch()
                .WrapAround(true);

            var backNode = new MiscNode("<- Back", _ansiConsole);
            prompt.AddChoice(backNode);

            foreach (var node in Children)
            {
                if (node is GroupNode groupNode)
                {
                    prompt.AddChoiceGroup(groupNode, groupNode.Children);
                }
                else
                {
                    prompt.AddChoice(node);
                }
            }

            var selection = _ansiConsole.Prompt(prompt);

            if (selection == backNode)
            {
                return;
            }

            await selection.Select();
        }
    }
}


internal class GroupNode : MenuNode
{
    public GroupNode(string label, IAnsiConsole ansiConsole, IEnumerable<MenuNode> children)
        : base(label, ansiConsole)
    {
        if (children.Any(x => x is GroupNode))
        {
            throw new InvalidOperationException("Group nodes cannot directly contain other group nodes!");
        }

        Children = children;
    }


    public IEnumerable<MenuNode> Children { get; }


    public override Task Select()
    {
        return Task.CompletedTask;
    }
}


internal class MiscNode(string label, IAnsiConsole ansiConsole)
    : MenuNode(label, ansiConsole)
{
    public override Task Select()
    {
        return Task.CompletedTask;
    }
}
