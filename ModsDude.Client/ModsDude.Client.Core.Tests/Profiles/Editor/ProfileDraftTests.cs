using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Profiles.Editor;
using static ModsDude.Client.Core.Tests.Keys;
using static ModsDude.Client.Core.Tests.Profiles.Editor.EditorFixtures;

namespace ModsDude.Client.Core.Tests.Profiles.Editor;

public class ProfileDraftTests
{
    [Fact]
    public void Reverting_an_added_mod_takes_it_out()
    {
        var draft = Draft().Pin(Pin("a", "1.0")).Revert([Mod("a")]);

        Assert.Empty(draft.Pins);
        Assert.False(draft.HasChanges);
    }

    [Fact]
    public void Reverting_a_taken_out_mod_puts_it_back_as_it_was()
    {
        var draft = Draft(Pin("a", "1.0", locked: true)).Remove([Mod("a")]).Revert([Mod("a")]);

        Assert.Equal(Pin("a", "1.0", locked: true), draft.Pins[Mod("a")]);
        Assert.False(draft.HasChanges);
    }

    [Fact]
    public void Reverting_a_changed_mod_restores_its_version_and_lock()
    {
        var draft = Draft(Pin("a", "1.0"))
            .SetVersion(Mod("a"), V("2.0"))
            .SetLocked([Mod("a")], true)
            .Revert([Mod("a")]);

        Assert.Equal(Pin("a", "1.0"), draft.Pins[Mod("a")]);
    }

    [Fact]
    public void Changing_a_mod_and_changing_it_back_is_no_change()
    {
        var draft = Draft(Pin("a", "1.0")).SetVersion(Mod("a"), V("2.0")).SetVersion(Mod("a"), V("1.0"));

        Assert.Equal(ProfileModTouch.None, draft.TouchOf(Mod("a")));
        Assert.False(draft.HasChanges);
    }

    [Fact]
    public void A_pinned_mod_is_never_written_as_ignored_and_the_ignore_comes_back_with_the_pin()
    {
        var ignored = Draft().SetIgnored([Mod("a")], true);
        var pinned = ignored.Pin(Pin("a", "1.0"));

        Assert.Empty(pinned.IgnoredToWrite);
        Assert.Equal([Mod("a")], pinned.Remove([Mod("a")]).IgnoredToWrite);
    }

    [Fact]
    public void Only_ignoring_is_an_ignore_change_and_not_a_pin_change()
    {
        var draft = Draft(Pin("a", "1.0")).SetIgnored([Mod("b")], true);

        Assert.True(draft.HasIgnoreChanges);
        Assert.False(draft.HasPinChanges);
    }

    [Fact]
    public void Reverting_everything_restores_the_saved_profile()
    {
        var draft = new ProfileDraft([Pin("a", "1.0")], [Mod("b")])
            .Remove([Mod("a")])
            .Pin(Pin("c", "1.0"))
            .SetIgnored([Mod("b")], false)
            .RevertAll();

        Assert.False(draft.HasChanges);
    }

    [Fact]
    public void The_adapters_lock_is_never_held_by_the_draft()
    {
        var draft = Draft().Pin(new ProfileModPin(Mod("a"), V("1.0"), new ProfileModLock(true, false)));

        Assert.False(draft.Pins[Mod("a")].Lock.ByAdapter);
    }
}


public class EditHistoryTests
{
    [Fact]
    public void Undo_and_redo_walk_the_steps_and_say_what_they_are()
    {
        var history = new EditHistory<int>(0);

        history.Push(1, "one");
        history.Push(2, "two");

        Assert.Equal("two", history.UndoDescription);
        Assert.Equal(1, history.Undo());
        Assert.Equal("two", history.RedoDescription);
        Assert.Equal(0, history.Undo());
        Assert.False(history.CanUndo);
        Assert.Equal(1, history.Redo());
        Assert.Equal(2, history.Redo());
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void A_new_step_clears_what_could_be_redone()
    {
        var history = new EditHistory<int>(0);

        history.Push(1, "one");
        history.Undo();
        history.Push(5, "five");

        Assert.False(history.CanRedo);
        Assert.Equal(0, history.Undo());
    }

    [Fact]
    public void Only_the_newest_steps_are_kept()
    {
        var history = new EditHistory<int>(0, capacity: 2);

        history.Push(1, "one");
        history.Push(2, "two");
        history.Push(3, "three");

        Assert.Equal(2, history.Undo());
        Assert.Equal(1, history.Undo());
        Assert.False(history.CanUndo);
    }
}
