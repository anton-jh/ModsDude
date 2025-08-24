namespace ModsDude.Client.Cli.Components.ModListEditor;
internal interface IFocusable
{
    bool HasFocus { get; set; }

    void HandleKeyPress(ConsoleKeyInfo consoleKeyInfo);
}
