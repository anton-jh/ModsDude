using Spectre.Console;
using Spectre.Console.Rendering;

namespace ModsDude.Client.Cli.Components.ModListEditor;
internal class ModViewModel
    : IItemViewModel
{
    public ModViewModel(ModStateWrapper mod, ModViewModelVariant variant)
    {
        Mod = mod;
        Variant = variant;
        Name = mod.State.Mod.DisplayName;
        Version = mod.State switch
        {
            ModState.Added x => x.Version.SequenceNumber.ToString(),
            ModState.ChangedVersion x => x.To.SequenceNumber.ToString(),
            ModState.Included x when !x.UpdateAvailable => x.Version.SequenceNumber.ToString(),
            ModState.Included x when x.UpdateAvailable && Variant is ModViewModelVariant.Left
                => x.Mod.Latest.SequenceNumber.ToString(),
            ModState.Included x when x.UpdateAvailable && Variant is ModViewModelVariant.Right
                => x.Version.SequenceNumber.ToString(),
            _ => null
        };
        Prefix = mod.State switch
        {
            ModState.Added => "+",
            ModState.ChangedVersion changedVersion => changedVersion.To.SequenceNumber > changedVersion.From.SequenceNumber ? ">" : "<",
            ModState.Removed => "-",
            ModState.Included x when x.UpdateAvailable => "^",
            _ => null
        };
    }


    public ModStateWrapper Mod { get; }
    public ModViewModelVariant Variant { get; }
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

    public void HandleEnter()
    {
        Mod.State = Mod.State switch
        {
            ModState.Available x => x.Add(),
            ModState.Included x when x.UpdateAvailable => x.Update(),
            ModState.Removed x => x.Add(),
            var x => x
        };
    }

    public void HandleBackspace()
    {
        Mod.State = Mod.State switch
        {
            ModState.Added x => x.Remove(),
            ModState.ChangedVersion x => x.Remove(),
            ModState.Included x => x.Remove(),
            var x => x
        };
    }

    public void HandleSpacebar()
    {
        Mod.State = Mod.State.Reset();
    }
}

internal enum ModViewModelVariant
{
    Left,
    Right
}

