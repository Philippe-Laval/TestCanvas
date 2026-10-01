using System.ComponentModel;

namespace TestCanvas.Models;

/// <summary>
/// Document de graphe : source de vérité côté C#. Toute mutation effectuée depuis
/// Blazor est automatiquement reflétée sur le canvas JavaScript, et inversement
/// chaque interaction souris est réinjectée dans le document.
/// </summary>
public sealed class GraphDocument
{
    private readonly List<GraphNode> _nodes = [];
    private readonly Dictionary<string, GraphNode> _nodesById = new(StringComparer.Ordinal);
    private readonly List<GraphEdge> _edges = [];
    private readonly Dictionary<string, GraphEdge> _edgesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<GraphEdge>> _incidentEdges = new(StringComparer.Ordinal);
    // Liste ordonnée plutôt qu'un HashSet : le premier élément est le nœud
// « primaire », celui_cliqué en dernier, que le canvas met en avant.
    private readonly List<string> _selectedNodeIds = [];

    private string? _selectedEdgeId;
    private int _nodeCounter;
    private int _edgeCounter;
    private int _batchDepth;
    private bool _resetPending;

    /// <summary>Levé à chaque modification, pour un envoi incrémental au canvas.</summary>
    public event EventHandler<GraphChange>? Changed;

    /// <summary>Levé lorsque la sélection change (sélection faite en JS ou en C#).</summary>
    public event EventHandler<GraphSelectionChangedEventArgs>? SelectionChanged;

    public IReadOnlyList<GraphNode> Nodes => _nodes;

    public IReadOnlyList<GraphEdge> Edges => _edges;

    public int NodeCount => _nodes.Count;

    public int EdgeCount => _edges.Count;

    /// <summary>Nœud actuellement mis en avant (ex. résultat d'un calcul de chemin).</summary>
    public IReadOnlyCollection<string> HighlightedNodeIds { get; private set; } = [];

    public IReadOnlyCollection<string> HighlightedEdgeIds { get; private set; } = [];

    public GraphNode? FindNode(string? id)
        => id is not null && _nodesById.TryGetValue(id, out var node) ? node : null;

    public GraphEdge? FindEdge(string? id)
        => id is not null && _edgesById.TryGetValue(id, out var edge) ? edge : null;

    /// <summary>Arêtes incidentes à un nœud.</summary>
    public IReadOnlyList<GraphEdge> EdgesOf(string nodeId)
        => _incidentEdges.TryGetValue(nodeId, out var list) ? list : [];

    public GraphNode AddNode(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (string.IsNullOrWhiteSpace(node.Id))
        {
            node.Id = NextNodeId();
        }

        if (_nodesById.ContainsKey(node.Id))
        {
            throw new InvalidOperationException($"Un nœud avec l'identifiant « {node.Id} » existe déjà.");
        }

        // Les coordonnées fournies par l'appelant sont respectées : les
        // surcharger ici revenait à décaler le tout premier nœud ajouté.
        _nodes.Add(node);
        _nodesById[node.Id] = node;
        _incidentEdges.TryAdd(node.Id, []);
        node.PropertyChanged += OnNodePropertyChanged;

        Raise(GraphChange.NodeAdded(node));
        return node;
    }

    /// <summary>Crée et ajoute un nœud avec un identifiant généré automatiquement.</summary>
    public GraphNode AddNode(string label = "Nœud", double x = 0, double y = 0)
        => AddNode(new GraphNode(NextNodeId(), label, x, y));

    /// <summary>Supprime un nœud ainsi que, par défaut, toutes ses arêtes incidentes.</summary>
    public bool RemoveNode(string id, bool removeConnectedEdges = true)
    {
        var node = FindNode(id);
        if (node is null)
        {
            return false;
        }

        if (removeConnectedEdges)
        {
            // Copie : RemoveEdge modifie la collection incidents du nœud.
            foreach (var edge in EdgesOf(id).ToList())
            {
                RemoveEdge(edge.Id);
            }
        }

        node.PropertyChanged -= OnNodePropertyChanged;
        _nodes.Remove(node);
        _nodesById.Remove(id);
        _incidentEdges.Remove(id);
        HighlightedNodeIds = [.. HighlightedNodeIds.Where(x => x != id)];

        var wasSelected = _selectedNodeIds.Remove(id);
        if (wasSelected)
        {
            RaiseSelectionChanged();
        }

        Raise(GraphChange.NodeRemoved(id));
        return true;
    }

    public GraphEdge AddEdge(GraphEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        if (FindNode(edge.SourceId) is null)
        {
            throw new InvalidOperationException($"Nœud source introuvable : « {edge.SourceId} ».");
        }

        if (FindNode(edge.TargetId) is null)
        {
            throw new InvalidOperationException($"Nœud cible introuvable : « {edge.TargetId} ».");
        }

        if (string.IsNullOrWhiteSpace(edge.Id))
        {
            edge.Id = NextEdgeId();
        }

        if (_edgesById.ContainsKey(edge.Id))
        {
            throw new InvalidOperationException($"Une arête avec l'identifiant « {edge.Id} » existe déjà.");
        }

        if (string.Equals(edge.SourceId, edge.TargetId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Une arête ne peut pas relier un nœud à lui-même.");
        }

        _edges.Add(edge);
        _edgesById[edge.Id] = edge;
        AddIncident(edge.SourceId, edge);
        AddIncident(edge.TargetId, edge);
        edge.PropertyChanged += OnEdgePropertyChanged;

        Raise(GraphChange.EdgeAdded(edge));
        return edge;
    }

    /// <summary>Crée une arête entre deux nœuds existants.</summary>
    public GraphEdge AddEdge(string sourceId, string targetId, string label = "")
        => AddEdge(new GraphEdge(NextEdgeId(), sourceId, targetId, label));

    /// <summary>Crée une arête en fournissant l'identifiant, le sens et le libellé.</summary>
    public GraphEdge AddEdge(string id, string sourceId, string targetId, string label = "")
        => AddEdge(new GraphEdge(id, sourceId, targetId, label));

    public bool RemoveEdge(string id)
    {
        var edge = FindEdge(id);
        if (edge is null)
        {
            return false;
        }

        edge.PropertyChanged -= OnEdgePropertyChanged;
        _edges.Remove(edge);
        _edgesById.Remove(id);
        RemoveIncident(edge.SourceId, edge);
        RemoveIncident(edge.TargetId, edge);
        HighlightedEdgeIds = [.. HighlightedEdgeIds.Where(x => x != id)];

        var wasSelected = string.Equals(_selectedEdgeId, id, StringComparison.Ordinal);
        if (wasSelected)
        {
            _selectedEdgeId = null;
            RaiseSelectionChanged();
        }

        Raise(GraphChange.EdgeRemoved(id));
        return true;
    }

    /// <summary>Supprime tous les nœuds et arêtes.</summary>
    public void Clear()
    {
        if (_nodes.Count == 0 && _edges.Count == 0)
        {
            return;
        }

        _nodes.Clear();
        _nodesById.Clear();
        _edges.Clear();
        _edgesById.Clear();
        _incidentEdges.Clear();
        _selectedNodeIds.Clear();
        _selectedEdgeId = null;
        RaiseSelectionChanged();
        Raise(GraphChange.Reset(Snapshot()));
    }

    /// <summary>Remplace intégralement le contenu du document.</summary>
    public void Reset(IEnumerable<GraphNode>? nodes = null, IEnumerable<GraphEdge>? edges = null)
    {
        foreach (var node in _nodes)
        {
            node.PropertyChanged -= OnNodePropertyChanged;
        }

        foreach (var edge in _edges)
        {
            edge.PropertyChanged -= OnEdgePropertyChanged;
        }

        _nodes.Clear();
        _nodesById.Clear();
        _edges.Clear();
        _edgesById.Clear();
        _incidentEdges.Clear();
        _selectedNodeIds.Clear();
        _selectedEdgeId = null;
        HighlightedNodeIds = [];
        HighlightedEdgeIds = [];

        foreach (var node in nodes ?? [])
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                node.Id = NextNodeId();
            }

            if (_nodesById.ContainsKey(node.Id))
            {
                continue;
            }

            _nodes.Add(node);
            _nodesById[node.Id] = node;
            _incidentEdges.TryAdd(node.Id, []);
            node.PropertyChanged += OnNodePropertyChanged;
        }

        foreach (var edge in edges ?? [])
        {
            if (string.IsNullOrWhiteSpace(edge.Id))
            {
                edge.Id = NextEdgeId();
            }

            if (FindNode(edge.SourceId) is null || FindNode(edge.TargetId) is null || _edgesById.ContainsKey(edge.Id))
            {
                continue;
            }

            _edges.Add(edge);
            _edgesById[edge.Id] = edge;
            AddIncident(edge.SourceId, edge);
            AddIncident(edge.TargetId, edge);
            edge.PropertyChanged += OnEdgePropertyChanged;
        }

        _nodeCounter = Math.Max(_nodeCounter, _nodes.Count);
        RaiseSelectionChanged();
        Raise(GraphChange.Reset(Snapshot()));
    }

    /// <summary>
    /// Regroupe plusieurs modifications en une seule notification afin d'éviter un
    /// aller-retour JS par élément (utile pour les algorithmes de mise en page).
    /// </summary>
    public void BeginBatch() => _batchDepth++;

    public void EndBatch()
    {
        if (_batchDepth == 0)
        {
            return;
        }

        _batchDepth--;
        if (_batchDepth == 0 && _resetPending)
        {
            _resetPending = false;
            Raise(GraphChange.Reset(Snapshot()));
        }
    }

    public void MoveNode(string id, double x, double y) => FindNode(id)?.MoveTo(x, y);

    // ---------------------------------------------------------------- sélection

    /// <summary>Nœuds sélectionnés, dans l'ordre de sélection (le premier est le primaire).</summary>
    public IReadOnlyList<GraphNode> SelectedNodes
        => [.. _selectedNodeIds.Select(FindNode).OfType<GraphNode>()];

    /// <summary>Dernier nœud sélectionné : celui que le canvas met en avant.</summary>
    public GraphNode? PrimarySelectedNode => SelectedNodes.Count > 0 ? SelectedNodes[0] : null;

    public GraphEdge? SelectedEdge => FindEdge(_selectedEdgeId);

    /// <summary>
    /// Nombre d'arêtes reliant le même couple de nœuds que <paramref name="edge"/>,
    /// celle-ci comprise. C'est ce qui distingue un multigraphe d'un graphe simple.
    /// </summary>
    public int ParallelCount(GraphEdge edge)
    {
        if (edge is null)
        {
            return 0;
        }

        return _edges.Count(e =>
            (e.SourceId == edge.SourceId && e.TargetId == edge.TargetId)
            || (e.SourceId == edge.TargetId && e.TargetId == edge.SourceId));
    }

    /// <summary>Arêtes partageant le même couple de nœuds que l'arête donnée.</summary>
    public IReadOnlyList<GraphEdge> ParallelEdges(GraphEdge edge)
    {
        if (edge is null)
        {
            return [];
        }

        return
        [
            .. _edges.Where(e =>
                (e.SourceId == edge.SourceId && e.TargetId == edge.TargetId)
                || (e.SourceId == edge.TargetId && e.TargetId == edge.SourceId))
        ];
    }

    /// <summary>Sélectionne un nœud (ou rien si <paramref name="id"/> est null).</summary>
    public void SelectNode(string? id, bool additive = false)
    {
        if (!additive)
        {
            foreach (var selected in SelectedNodes)
            {
                selected.Selected = false;
            }

            _selectedNodeIds.Clear();
        }

        if (!string.IsNullOrEmpty(id) && FindNode(id) is { } node)
        {
            if (additive && node.Selected)
            {
                _selectedNodeIds.Remove(id);
                node.Selected = false;
            }
            else
            {
                // Le nœud promu passe en tête de liste.
                _selectedNodeIds.Remove(id);
                _selectedNodeIds.Insert(0, id);
                node.Selected = true;
            }
        }

        if (!additive)
        {
            SelectEdge(null, raiseEvent: false);
        }

        RaiseSelectionChanged();
    }

    public void SelectEdge(string? id, bool raiseEvent = true)
    {
        if (SelectedEdge is { } previous && previous.Id != id)
        {
            previous.Selected = false;
        }

        _selectedEdgeId = id is not null && FindEdge(id) is { } edge ? edge.Id : null;
        if (SelectedEdge is { } current)
        {
            current.Selected = true;
        }

        if (raiseEvent)
        {
            RaiseSelectionChanged();
        }
    }

    /// <summary>
    /// Applique une sélection provenant de l'extérieur (canvas JavaScript) : le
    /// document aligne ses indicateurs internes puis notifie les abonnés.
    /// </summary>
    public void SetSelection(IEnumerable<string> nodeIds, string? edgeId)
    {
        var wanted = new HashSet<string>(nodeIds ?? [], StringComparer.Ordinal);

        foreach (var node in _nodes)
        {
            node.Selected = wanted.Contains(node.Id);
        }

        // L'ordre reçu fait foi : le canvas place en tête le nœud actif.
        _selectedNodeIds.Clear();
        foreach (var id in nodeIds ?? [])
        {
            if (_nodesById.ContainsKey(id) && !_selectedNodeIds.Contains(id))
            {
                _selectedNodeIds.Add(id);
            }
        }

        if (SelectedEdge is { } previous && previous.Id != edgeId)
        {
            previous.Selected = false;
        }

        _selectedEdgeId = edgeId is not null && FindEdge(edgeId) is { } target ? target.Id : null;

        if (SelectedEdge is { } current)
        {
            current.Selected = true;
        }

        RaiseSelectionChanged();
    }

    public void ClearSelection()
    {
        var changed = _selectedNodeIds.Count > 0 || _selectedEdgeId is not null;
        foreach (var node in SelectedNodes)
        {
            node.Selected = false;
        }

        _selectedNodeIds.Clear();
        _selectedEdgeId = null;
        if (changed)
        {
            RaiseSelectionChanged();
        }
    }

    // ------------------------------------------------------------- mise en avant

    /// <summary>
    /// Met en avant un ensemble de nœuds et d'arêtes (chemin le plus court, résultats
    /// de recherche…). Seuls les objets concernés sont notifiés.
    /// </summary>
    public void SetHighlight(IEnumerable<string>? nodeIds, IEnumerable<string>? edgeIds)
    {
        var wantedNodes = new HashSet<string>(nodeIds ?? [], StringComparer.Ordinal);
        var wantedEdges = new HashSet<string>(edgeIds ?? [], StringComparer.Ordinal);

        foreach (var node in _nodes.Where(n => n.Highlighted != wantedNodes.Contains(n.Id)))
        {
            node.Highlighted = wantedNodes.Contains(node.Id);
        }

        foreach (var edge in _edges.Where(e => e.Highlighted != wantedEdges.Contains(e.Id)))
        {
            edge.Highlighted = wantedEdges.Contains(edge.Id);
        }

        HighlightedNodeIds = [.. wantedNodes];
        HighlightedEdgeIds = [.. wantedEdges];
    }

    public void ClearHighlight() => SetHighlight(null, null);

    // --------------------------------------------------------------- snapshots

    public GraphSnapshot Snapshot()
        => new()
        {
            Nodes = [.. _nodes.Select(n => n.Clone())],
            Edges = [.. _edges.Select(e => e.Clone())]
        };

    public GraphDocument Clone()
    {
        var copy = new GraphDocument();
        copy.Reset(Snapshot().Nodes, Snapshot().Edges);
        return copy;
    }

    /// <summary>Vérifie la cohérence du document et retourne la liste des anomalies.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        foreach (var edge in _edges)
        {
            if (FindNode(edge.SourceId) is null)
            {
                problems.Add($"L'arête « {edge.Id} » référence un nœud source inexistant ({edge.SourceId}).");
            }

            if (FindNode(edge.TargetId) is null)
            {
                problems.Add($"L'arête « {edge.Id} » référence un nœud cible inexistant ({edge.TargetId}).");
            }
        }

        var duplicates = _nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1).Select(g => g.Key);
        problems.AddRange(duplicates.Select(id => $"Identifiant de nœud dupliqué : « {id} »."));

        return problems;
    }

    // ------------------------------------------------------------------ interne

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not GraphNode node)
        {
            return;
        }

        Raise(GraphChange.NodeUpdated(node));
    }

    private void OnEdgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not GraphEdge edge)
        {
            return;
        }

        Raise(GraphChange.EdgeUpdated(edge));
    }

    private void Raise(GraphChange change)
    {
        if (_batchDepth > 0)
        {
            _resetPending = true;
            return;
        }

        Changed?.Invoke(this, change);
    }

    private void RaiseSelectionChanged()
        => SelectionChanged?.Invoke(this, new GraphSelectionChangedEventArgs(SelectedNodes, SelectedEdge));

    /// <summary>
    /// Notifie une nouvelle sélection sans passer par <see cref="SelectNode"/>, lorsque
    /// l'état a déjà été appliqué manuellement.
    /// </summary>
    public void RaiseSelectionChangedPublic() => RaiseSelectionChanged();

    private void AddIncident(string nodeId, GraphEdge edge)
    {
        if (!_incidentEdges.TryGetValue(nodeId, out var list))
        {
            list = [];
            _incidentEdges[nodeId] = list;
        }

        list.Add(edge);
    }

    private void RemoveIncident(string nodeId, GraphEdge edge)
    {
        if (_incidentEdges.TryGetValue(nodeId, out var list))
        {
            list.Remove(edge);
        }
    }

    private string NextNodeId()
    {
        do
        {
            _nodeCounter++;
        }
        while (_nodesById.ContainsKey($"n{_nodeCounter}"));

        return $"n{_nodeCounter}";
    }

    private string NextEdgeId()
    {
        do
        {
            _edgeCounter++;
        }
        while (_edgesById.ContainsKey($"e{_edgeCounter}"));

        return $"e{_edgeCounter}";
    }
}