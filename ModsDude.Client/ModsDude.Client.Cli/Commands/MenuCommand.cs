using ModsDude.Cli.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands;
internal class MenuCommand(IAnsiConsole ansiConsole)
    : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var root = new SubMenuNode(ansiConsole, "Menu", [
            new CommandNode<GreetingCommand>(ansiConsole, "Say hi"),
            new SubMenuNode(ansiConsole, "Sub-menu", [
                new CommandNode<ReloginCommand>(ansiConsole, "Re-login / change user"),
                new GroupNode(ansiConsole, "Group", [
                    new CommandNode<GreetingCommand>(ansiConsole, "Say hi again"),
                    new CommandNode<ReloginCommand>(ansiConsole, "Re-login / change user"),
                ]),
                new CommandNode<ReloginCommand>(ansiConsole, "Re-login / change user"),
            ])
        ]);

        await root.Select();

        return 0;
    }
}

internal abstract class MenuNode(IAnsiConsole ansiConsole, string label)
{
    protected readonly IAnsiConsole _ansiConsole = ansiConsole;

    public string Label { get; } = label;

    public abstract Task Select();
}

internal class CommandNode<TCommand>(IAnsiConsole ansiConsole, string label)
    : MenuNode(ansiConsole, label)
    where TCommand : class, ICommand
{
    public override Task Select()
    {
        _ansiConsole.MarkupLine($"Selected command node '{Label}'");
        return Task.CompletedTask;
    }
}

internal class SubMenuNode(IAnsiConsole ansiConsole, string label, IEnumerable<MenuNode> children)
    : MenuNode(ansiConsole, label)
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

            var backNode = new MiscNode(_ansiConsole, "<- Back");
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
    public GroupNode(IAnsiConsole ansiConsole, string label, IEnumerable<MenuNode> children)
        : base(ansiConsole, label)
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

internal class MiscNode(IAnsiConsole ansiConsole, string label)
    : MenuNode(ansiConsole, label)
{
    public override Task Select()
    {
        return Task.CompletedTask;
    }
}
