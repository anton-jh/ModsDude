using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles.Editor;

namespace ModsDude.Client.Wpf.Profiles.Editor;

/// <summary>What the profile mod editor opens looking at, for a page reached from somewhere that asked a question about it.</summary>
public abstract record EditorFocus
{
    private EditorFocus()
    {
    }


    /// <summary>The game's mod folders are the only sources, and the pinned list shows <paramref name="Filter"/>.</summary>
    public sealed record DriftReview(PinnedModFilter Filter) : EditorFocus;

    /// <summary>
    /// A savegame on this machine is the only source, every mod found of it is added to the draft as
    /// one step, and the pinned list shows what the save does not list.
    /// </summary>
    public sealed record Savegame(GameIdentity Game, SavegameSlotRef Slot) : EditorFocus;
}
