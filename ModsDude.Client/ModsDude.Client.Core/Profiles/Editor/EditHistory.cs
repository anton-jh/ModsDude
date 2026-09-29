namespace ModsDude.Client.Core.Profiles.Editor;

/// <summary>
/// Undo and redo over immutable states. Each step keeps the words for what produced it, so an undo
/// button can say what it would take back.
/// </summary>
public sealed class EditHistory<T>
{
    private readonly int _capacity;
    private readonly LinkedList<(T State, string Description)> _undo = [];
    private readonly Stack<(T State, string Description)> _redo = [];


    public EditHistory(T initial, int capacity = 50)
    {
        _capacity = capacity;
        Current = initial;
    }


    public T Current { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public string? UndoDescription => _undo.Last?.Value.Description;
    public string? RedoDescription => _redo.TryPeek(out var next) ? next.Description : null;


    /// <summary>Records <paramref name="next"/> as a step. A new step clears what could be redone.</summary>
    public void Push(T next, string description)
    {
        _undo.AddLast((Current, description));
        _redo.Clear();

        if (_undo.Count > _capacity)
        {
            _undo.RemoveFirst();
        }

        Current = next;
    }

    public T Undo()
    {
        if (_undo.Last is not { } last)
        {
            return Current;
        }

        _undo.RemoveLast();
        _redo.Push((Current, last.Value.Description));
        Current = last.Value.State;

        return Current;
    }

    public T Redo()
    {
        if (_redo.TryPop(out var next) is false)
        {
            return Current;
        }

        _undo.AddLast((Current, next.Description));
        Current = next.State;

        return Current;
    }

    /// <summary>Starts over from <paramref name="state"/>, with nothing to undo or redo.</summary>
    public void Reset(T state)
    {
        _undo.Clear();
        _redo.Clear();
        Current = state;
    }
}
