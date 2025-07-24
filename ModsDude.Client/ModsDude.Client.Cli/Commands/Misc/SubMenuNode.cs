using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Misc;

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
