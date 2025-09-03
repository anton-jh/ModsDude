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
            ModState.Included { UpdateAvailable: false } x => x.Version.SequenceNumber.ToString(),
            ModState.Included { UpdateAvailable: true } x when Variant is ModViewModelVariant.Left
                => x.Mod.Latest.SequenceNumber.ToString(),
            ModState.Included { UpdateAvailable: true } x when Variant is ModViewModelVariant.Right
                => $"{x.Version.SequenceNumber} > {x.Mod.Latest.SequenceNumber}",
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

    public bool HandleKeyPress(ConsoleKeyInfo consoleKeyInfo)
    {
        var action = GetPossibleActions().FirstOrDefault(x => x.Key == consoleKeyInfo.Key);

        if (action is not null)
        {
            Mod.State = action.Func.Invoke();
            return true;
        }

        return false;
    }

    public IEnumerable<KeyAction> GetPossibleActions()
    {
        return Mod.State switch
        {
            ModState.Added x => [
                new(ConsoleKey.Backspace, "Remove", x.Remove),
                new(ConsoleKey.Spacebar, "Reset", x.Reset),
            ],

            ModState.Available x => [
                new(ConsoleKey.Enter, "Add", x.Add),
            ],

            ModState.ChangedVersion x => [
                new(ConsoleKey.Backspace, "Remove", x.Remove),
                new(ConsoleKey.Spacebar, "Reset", x.Reset),
            ],

            ModState.Included x => new List<KeyAction?>
            {
                x.UpdateAvailable ? new(ConsoleKey.Enter, "Update", x.Update) : null,
                Variant is ModViewModelVariant.Right ? new(ConsoleKey.Backspace, "Remove", x.Remove) : null,
            }.OfType<KeyAction>(),

            ModState.Removed x => [
                new(ConsoleKey.Enter, "Add", x.Add),
                new(ConsoleKey.Spacebar, "Reset", x.Reset),
            ],

            _ => []
        };
    }


    public record KeyAction(ConsoleKey Key, string Label, Func<ModState> Func);
}

internal enum ModViewModelVariant
{
    Left,
    Right
}
