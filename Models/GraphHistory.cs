namespace TestCanvas.Models;

/// <summary>Annulation / rétablissement par instantanés du document.</summary>
public sealed class GraphHistory
{
    private const int MaxDepth = 100;

    private readonly Stack<GraphSnapshot> _undo = new();
    private readonly Stack<GraphSnapshot> _redo = new();

    private GraphDocument? _document;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    /// <summary>Lie l'historique à un document ; changer de document réinitialise la pile.</summary>
    public void Attach(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!ReferenceEquals(_document, document))
        {
            _document = document;
            _undo.Clear();
            _redo.Clear();
        }
    }

    /// <summary>Enregistre l'état courant : à appeler juste avant une modification.</summary>
    public void Track()
    {
        if (_document is null)
        {
            return;
        }

        _undo.Push(_document.Snapshot());
        _redo.Clear();

        // Stack_pop retire le plus récent : on reconstruit donc la pile en
        // conservant les MaxDepth états les plus récents.
        if (_undo.Count > MaxDepth)
        {
            var kept = _undo.Reverse().Take(MaxDepth).Reverse().ToList();
            _undo.Clear();
            foreach (var snapshot in kept)
            {
                _undo.Push(snapshot);
            }
        }
    }

    public bool Undo() => Restore(_undo, _redo);

    public bool Redo() => Restore(_redo, _undo);

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private bool Restore(Stack<GraphSnapshot> from, Stack<GraphSnapshot> to)
    {
        if (_document is null || from.Count == 0)
        {
            return false;
        }

        var snapshot = from.Pop();
        to.Push(_document.Snapshot());
        _document.Reset(snapshot.Nodes, snapshot.Edges);
        return true;
    }
}