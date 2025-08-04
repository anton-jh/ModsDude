using Spectre.Console;
using Spectre.Console.Rendering;

namespace ModsDude.Client.Cli.Components.ModListEditor;
internal class ModViewModel
    : IItemViewModel
{
    public ModViewModel(ModState mod)
    {
        State = mod;
        Name = mod.Mod.DisplayName;
        Version = mod switch
        {
            ModState.Added x => x.Version.SequenceNumber.ToString(),
            ModState.ChangedVersion x => x.To.SequenceNumber.ToString(),
            ModState.Included x when !x.UpdateAvailable => x.Version.SequenceNumber.ToString(),
            ModState.Included x when x.UpdateAvailable => x.Mod.Latest.SequenceNumber.ToString(),
            _ => null
        };
        Prefix = mod switch
        {
            ModState.Added => "+",
            ModState.ChangedVersion changedVersion => changedVersion.To.SequenceNumber > changedVersion.From.SequenceNumber ? ">" : "<",
            ModState.Removed => "-",
            ModState.Included x when x.UpdateAvailable => "^",
            _ => null
        };
    }


    public ModState State { get; }
    public string Name { get; }
    public string? Prefix { get; }
    public string? Version { get; }


    public IRenderable Render(bool isSelected, bool panelHasFocus)
    {
        var color = (isSelected, panelHasFocus) switch
        {
            (true, true) => Color.Blue,
            (true, false) => Color.Grey,
            _ => Color.Default
        };

        var prefix = Prefix is not null
            ? $"[grey]{Prefix}[/] "
            : "  ";
        var suffix = Version is not null
            ? $" [grey italic]({Version})[/]"
            : "";

        var title = prefix + Name + suffix;

        return new Markup(title, color);
    }

    public ModState HandleEnter()
    {
        return State switch
        {
            ModState.Available x => x.Add(),
            ModState.Included x when x.UpdateAvailable => x.Update(),
            ModState.Removed x => x.Add(),
            var x => x
        };
    }

    public ModState HandleBackspace()
    {
        return State switch
        {
            ModState.Added x => x.Remove(),
            ModState.ChangedVersion x => x.Remove(),
            ModState.Included x => x.Remove(),
            var x => x
        };
    }

    public ModState HandleSpacebar()
    {
        return State.Reset();
    }
}


// now:
// - IAddable and so on?
// - separate view models for left and right list again (ModState.Included.UpdateAvailable needs to show current version in right list and latest in left list)
