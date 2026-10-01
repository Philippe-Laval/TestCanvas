namespace TestCanvas.Models;

/// <summary>Copie complète du graphe à un instant donné (utilisée par l'annulation, la restauration et la sérialisation).</summary>
public sealed class GraphSnapshot
{
    public List<GraphNode> Nodes { get; init; } = [];

    public List<GraphEdge> Edges { get; init; } = [];
}

/// <summary>Une modification du document, transmise telle quelle au canvas JavaScript.</summary>
public sealed class GraphChange
{
    public GraphChangeKind Kind { get; init; }

    /// <summary>Identifiant de l'objet concerné (nœud ou arête).</summary>
    public string? Id { get; init; }

    public GraphNode? Node { get; init; }

    public GraphEdge? Edge { get; init; }

    /// <summary>Contenu complet du document, présent uniquement pour <see cref="GraphChangeKind.Reset"/>.</summary>
    public GraphSnapshot? Snapshot { get; init; }

    public static GraphChange Reset(GraphSnapshot snapshot) =>
        new() { Kind = GraphChangeKind.Reset, Snapshot = snapshot };

    public static GraphChange NodeAdded(GraphNode node) =>
        new() { Kind = GraphChangeKind.NodeAdded, Id = node.Id, Node = node };

    public static GraphChange NodeUpdated(GraphNode node) =>
        new() { Kind = GraphChangeKind.NodeUpdated, Id = node.Id, Node = node };

    public static GraphChange NodeRemoved(string id) =>
        new() { Kind = GraphChangeKind.NodeRemoved, Id = id };

    public static GraphChange EdgeAdded(GraphEdge edge) =>
        new() { Kind = GraphChangeKind.EdgeAdded, Id = edge.Id, Edge = edge };

    public static GraphChange EdgeUpdated(GraphEdge edge) =>
        new() { Kind = GraphChangeKind.EdgeUpdated, Id = edge.Id, Edge = edge };

    public static GraphChange EdgeRemoved(string id) =>
        new() { Kind = GraphChangeKind.EdgeRemoved, Id = id };
}

/// <summary>Événement levé lorsque la sélection du document change.</summary>
public sealed class GraphSelectionChangedEventArgs : EventArgs
{
    public GraphSelectionChangedEventArgs(IReadOnlyList<GraphNode> nodes, GraphEdge? edge)
    {
        Nodes = nodes;
        Edge = edge;
    }

    /// <summary>Nœuds sélectionnés, dans l'ordre d'ajout au document.</summary>
    public IReadOnlyList<GraphNode> Nodes { get; }

    /// <summary>Arête sélectionnée, ou <see langword="null"/>.</summary>
    public GraphEdge? Edge { get; }
}