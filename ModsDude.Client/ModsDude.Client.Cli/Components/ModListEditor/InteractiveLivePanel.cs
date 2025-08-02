using Spectre.Console;
using Spectre.Console.Rendering;

namespace ModsDude.Client.Cli.Components.ModListEditor;

public abstract class InteractiveLivePanel
{
    private IRenderable _renderable;
    private Panel _panel;


    protected InteractiveLivePanel(IRenderable renderable)
    {
        _renderable = new Markup("");
        _panel = new(renderable);
        Layout = new(_panel);
        Update();
    }


    public bool HasFocus { get; private set; }
    public Layout Layout { get; protected set; }


    public void SetFocus(bool focus = true)
    {
        HasFocus = focus;
        Update();
        OnFocusChanged();
    }


    protected void Update(IRenderable? renderable = null)
    {
        if (renderable is not null && renderable != _renderable)
        {
            _renderable = renderable;
            _panel = new(_renderable);
            Layout.Update(_panel);
        }

        _panel.BorderColor(HasFocus
            ? Color.Blue
            : Color.Grey);
    }

    public abstract void HandleKeyPress(ConsoleKeyInfo consoleKeyInfo);
    protected abstract void OnFocusChanged();
}
