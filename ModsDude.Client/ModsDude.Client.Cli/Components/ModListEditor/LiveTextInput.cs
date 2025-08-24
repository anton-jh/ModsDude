using Spectre.Console;

namespace ModsDude.Client.Cli.Components.ModListEditor;
internal class LiveTextInput
{
    public LiveTextInput()
    {
        
    }


    public string Value { get; private set; } = "";


    public void HandleKeyPress(ConsoleKeyInfo consoleKeyInfo)
    {
        if (consoleKeyInfo.Key is ConsoleKey.Backspace)
        {
            Value = Value.Length != 0 ? Value[..^1] : "";
            return;
        }

        var character = consoleKeyInfo.KeyChar;

        if (character == default)
        {
            return;
        }

        Value += character;
    }

    protected void OnFocusChanged()
    {
    }


    private static Markup Render(string value)
    {
        return new Markup(value.EscapeMarkup());
    }
}
