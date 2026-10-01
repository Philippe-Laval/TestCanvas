using TestCanvas.Models;

namespace TestCanvas.Services;

/// <summary>Algorithmes de graphe dont le résultat pilote directement l'affichage du canvas.</summary>
public static class GraphAlgorithms
{
    /// <summary>Plus court chemin (BFS sur graphe non pondéré, dans les deux sens).</summary>
    public static IReadOnlyList<GraphNode>? ShortestPath(GraphDocument graph, string fromId, string toId)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.FindNode(fromId) is null || graph.FindNode(toId) is null)
        {
            return null;
        }

        if (string.Equals(fromId, toId, StringComparison.Ordinal))
        {
            return [graph.FindNode(fromId)!];
        }

        var queue = new Queue<string>();
        var previous = new Dictionary<string, string>(StringComparer.Ordinal);
        queue.Enqueue(fromId);
        var seen = new HashSet<string>(StringComparer.Ordinal) { fromId };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in graph.EdgesOf(current))
            {
                var next = edge.SourceId == current ? edge.TargetId : edge.SourceId;
                if (!seen.Add(next))
                {
                    continue;
                }

                previous[next] = current;
                if (next == toId)
                {
                    return Reconstruct(graph, previous, fromId, toId);
                }

                queue.Enqueue(next);
            }
        }

        return null;
    }

    /// <summary>Parcours en profondeur : nœuds atteignables depuis un point de départ.</summary>
    public static IReadOnlyList<GraphNode> ReachableFrom(GraphDocument graph, string startId)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var result = new List<GraphNode>();
        if (graph.FindNode(startId) is null)
        {
            return result;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { startId };
        var queue = new Queue<string>();
        queue.Enqueue(startId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in graph.EdgesOf(current))
            {
                var next = edge.SourceId == current ? edge.TargetId : edge.SourceId;
                if (visited.Add(next) && graph.FindNode(next) is { } node)
                {
                    result.Add(node);
                    queue.Enqueue(next);
                }
            }
        }

        return result;
    }

    /// <summary>Sous-graphe Accessible depuis les nœuds racines (ceux sans arête entrante).</summary>
    public static GraphDocument ExtractRootedSubgraph(GraphDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var roots = source.Nodes
            .Where(n => !source.Edges.Any(e => e.TargetId == n.Id && e.SourceId != n.Id))
            .Select(n => n.Id)
            .ToList();

        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            kept.Add(root);
            foreach (var node in ReachableFrom(source, root))
            {
                kept.Add(node.Id);
            }
        }

        var clone = source.Clone();
        clone.BeginBatch();
        try
        {
            foreach (var id in clone.Nodes.Select(n => n.Id).Where(id => !kept.Contains(id)).ToList())
            {
                clone.RemoveNode(id);
            }

            foreach (var edge in clone.Edges
                         .Where(e => !kept.Contains(e.SourceId) || !kept.Contains(e.TargetId))
                         .Select(e => e.Id)
                         .ToList())
            {
                clone.RemoveEdge(edge);
            }

            clone.EndBatch();
        }
        catch
        {
            clone.EndBatch();
            throw;
        }

        return clone;
    }

    private static List<GraphNode> Reconstruct(GraphDocument graph, Dictionary<string, string> previous, string from, string to)
    {
        var path = new List<string> { to };
        var current = to;
        while (current != from)
        {
            current = previous[current];
            path.Add(current);
        }

        path.Reverse();
        return [.. path.Select(id => graph.FindNode(id)!)];
    }
}