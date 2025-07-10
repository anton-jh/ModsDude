using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Cli.Commands.Abstractions;
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
        var root = new SubMenuNode("Menu", ansiConsole, [
            new SubMenuNode("Repos", ansiConsole, [
                new CommandNode<ListReposCommand>("List all repos", serviceProvider, ansiConsole),
                new CommandNode<CreateRepoCommand>("Create new repo", serviceProvider, ansiConsole)
            ]),
            new GroupNode("Misc", ansiConsole, [
                new CommandNode<ReloginCommand>("Re-login / change user", serviceProvider, ansiConsole)
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
        var commandInstance = serviceProvider.GetRequiredService<TCommand>();
        await commandInstance.ExecuteAsync();
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
