using TestCanvas.Models;

namespace TestCanvas.Services;

public sealed class ForceDirectedOptions
{
    public int Iterations { get; set; } = 300;

    /// <summary>Force de répulsion entre nœuds.</summary>
    public double Repulsion { get; set; } = 6000;

    /// <summary>Longueur de repos d'un ressort sur une arête.</summary>
    public double SpringLength { get; set; } = 140;

    public double SpringStrength { get; set; } = 0.02;

    /// <summary>Force de rappel vers le centre.</summary>
    public double Centering { get; set; } = 0.01;

    public double Damping { get; set; } = 0.85;

    public double MaxDisplacement { get; set; } = 40;

    public double Bounds { get; set; } = 1200;
}

/// <summary>Mise en page automatique du graphe, entièrement calculée en C#.</summary>
public static class GraphLayout
{
    public static void ForceDirected(GraphDocument graph, ForceDirectedOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new ForceDirectedOptions();

        var nodes = graph.Nodes.Where(n => !n.Locked).ToList();
        if (nodes.Count <= 1)
        {
            return;
        }

        var edges = graph.Edges
            .Where(e => graph.FindNode(e.SourceId) is not null && graph.FindNode(e.TargetId) is not null)
            .ToList();

        var index = nodes.Select((node, i) => (node, i)).ToDictionary(x => x.node.Id, x => x.i);
        var count = nodes.Count;
        var px = nodes.Select(n => n.X).ToArray();
        var py = nodes.Select(n => n.Y).ToArray();
        var fx = new double[count];
        var fy = new double[count];

        // Le document est mis en mode « batch » : un seul aller-retour JS à la fin.
        graph.BeginBatch();
        try
        {
            var random = new Random(42);

            // Disposition initiale déterministe si les nœuds sont superposés.
            for (var i = 0; i < count; i++)
            {
                if (px[i] == 0 && py[i] == 0)
                {
                    var angle = random.NextDouble() * Math.PI * 2;
                    px[i] = Math.Cos(angle) * 200;
                    py[i] = Math.Sin(angle) * 200;
                }
            }

            for (var step = 0; step < options.Iterations; step++)
            {
                Array.Clear(fx);
                Array.Clear(fy);

                // Répulsion (loi de Coulomb).
                for (var i = 0; i < count; i++)
                {
                    for (var j = i + 1; j < count; j++)
                    {
                        var dx = px[i] - px[j];
                        var dy = py[i] - py[j];
                        var dist2 = Math.Max(dx * dx + dy * dy, 1.0);
                        var dist = Math.Sqrt(dist2);
                        var force = options.Repulsion / dist2;
                        var ux = dx / dist * force;
                        var uy = dy / dist * force;
                        fx[i] += ux; fy[i] += uy;
                        fx[j] -= ux; fy[j] -= uy;
                    }
                }

                // Ressorts sur les arêtes (loi de Hooke).
                foreach (var edge in edges)
                {
                    if (!index.TryGetValue(edge.SourceId, out var a) || !index.TryGetValue(edge.TargetId, out var b))
                    {
                        continue;
                    }

                    var dx = px[b] - px[a];
                    var dy = py[b] - py[a];
                    var dist = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.001);
                    var force = (dist - options.SpringLength) * options.SpringStrength;
                    var ux = dx / dist * force;
                    var uy = dy / dist * force;
                    fx[a] += ux; fy[a] += uy;
                    fx[b] -= ux; fy[b] -= uy;
                }

                // Rappel au centre.
                for (var i = 0; i < count; i++)
                {
                    fx[i] -= px[i] * options.Centering;
                    fy[i] -= py[i] * options.Centering;
                }

                // Intégration avec refroidissement et bornes.
                var cooling = 1.0 - step / (double)options.Iterations;
                for (var i = 0; i < count; i++)
                {
                    var dx = fx[i] * options.Damping * cooling;
                    var dy = fy[i] * options.Damping * cooling;
                    var length = Math.Sqrt(dx * dx + dy * dy);
                    if (length > options.MaxDisplacement)
                    {
                        var scale = options.MaxDisplacement / length;
                        dx *= scale;
                        dy *= scale;
                    }

                    px[i] = Math.Clamp(px[i] + dx, -options.Bounds, options.Bounds);
                    py[i] = Math.Clamp(py[i] + dy, -options.Bounds, options.Bounds);
                }
            }

            for (var i = 0; i < count; i++)
            {
                nodes[i].MoveTo(Math.Round(px[i], 2), Math.Round(py[i], 2));
            }
        }
        finally
        {
            graph.EndBatch();
        }
    }

    /// <summary>Dispose les nœuds en couches (droite vers gauche).</summary>
    public static void Hierarchical(GraphDocument graph, double layerGap = 220, double nodeGap = 140)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodes = graph.Nodes.ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        graph.BeginBatch();
        try
        {
            // Degree pondéré :igraph "layout with graphopt".
            var layerOf = nodes.ToDictionary(n => n.Id, _ => 0);
            var incoming = nodes.ToDictionary(
                n => n.Id,
                n => graph.Edges.Where(e => e.TargetId == n.Id && e.SourceId != n.Id).Select(e => e.SourceId).ToList());
            var outgoing = nodes.ToDictionary(
                n => n.Id,
                n => graph.Edges.Where(e => e.SourceId == n.Id && e.TargetId != n.Id).Select(e => e.TargetId).ToList());

            var weight = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var n in nodes)
            {
                weight[n.Id] = outgoing[n.Id].Count - incoming[n.Id].Count;
            }

            var queue = new Queue<string>();
            foreach (var n in nodes.Where(n => incoming[n.Id].Count == 0 || weight[n.Id] >= 0))
            {
                queue.Enqueue(n.Id);
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!visited.Add(id))
                {
                    continue;
                }

                foreach (var child in outgoing[id])
                {
                    layerOf[child] = Math.Max(layerOf[child], layerOf[id] + 1);
                    if (!visited.Contains(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            // Les cycles restants (non visités) sont placés à la fin.
            var remaining = nodes.Where(n => !visited.Contains(n.Id)).ToList();
            var offset = layerOf.Values.DefaultIfEmpty(0).Max() + 1;
            foreach (var n in remaining)
            {
                layerOf[n.Id] = offset++;
            }

            var byLayer = nodes.GroupBy(n => layerOf[n.Id]).OrderBy(g => g.Key).ToList();
            foreach (var group in byLayer)
            {
                var y = group.Key * layerGap;
                var ordered = group
                    .OrderByDescending(n => incoming[n.Id].Count)
                    .ThenBy(n => n.Id, StringComparer.Ordinal)
                    .ToList();

                var total = ordered.Count * nodeGap;
                var x = -total / 2.0 + nodeGap / 2.0;
                foreach (var n in ordered)
                {
                    n.MoveTo(Math.Round(x, 2), Math.Round(y, 2));
                    x += nodeGap;
                }
            }
        }
        finally
        {
            graph.EndBatch();
        }
    }

    /// <summary>Grille régulière, pratique pour générer des lots de nœuds.</summary>
    public static void Grid(GraphDocument graph, int columns = 5, double gapX = 180, double gapY = 120)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodes = graph.Nodes.ToList();
        graph.BeginBatch();
        try
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var row = i / Math.Max(columns, 1);
                var col = i % Math.Max(columns, 1);
                nodes[i].MoveTo(col * gapX, row * gapY);
            }
        }
        finally
        {
            graph.EndBatch();
        }
    }
}