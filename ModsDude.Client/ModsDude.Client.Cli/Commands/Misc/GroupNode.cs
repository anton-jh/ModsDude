using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Misc;

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
