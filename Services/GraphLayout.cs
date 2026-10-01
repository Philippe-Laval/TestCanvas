using TestCanvas.Models;

namespace TestCanvas.Services;

/// <summary>Paramètres communs aux mises en page.</summary>
public sealed class LayoutOptions
{
    /// <summary>Nombre d'itérations de relaxation.</summary>
    public int Iterations { get; set; } = 300;

    /// <summary>
    /// Longueur de repos : distance cible entre deux nœuds voisins, ou
    /// espacement de base pour les mises en page géométriques.
    /// </summary>
    public double Spacing { get; set; } = 150;

    /// <summary>Graine du générateur aléatoire, pour des résultats reproductibles.</summary>
    public int Seed { get; set; } = 42;

    /// <summary>Nombre de colonnes pour la mise en page en grille.</summary>
    public int Columns { get; set; } = 5;

    public LayoutOptions Clone() => new()
    {
        Iterations = Iterations,
        Spacing = Spacing,
        Seed = Seed,
        Columns = Columns
    };
}

/// <summary>Contexte interne commun aux algorithmes : index, positions, voisinage.</summary>
internal sealed class LayoutContext
{
    public LayoutContext(GraphDocument graph, List<GraphNode> nodes, List<GraphEdge> edges)
    {
        Graph = graph;
        Nodes = nodes;
        Edges = edges;
        Count = nodes.Count;

        Px = new double[Count];
        Py = new double[Count];
        Fx = new double[Count];
        Fy = new double[Count];
        Neighbours = new List<int>[Count];

        for (var i = 0; i < Count; i++)
        {
            Neighbours[i] = [];
            Index[nodes[i].Id] = i;
        }

        foreach (var edge in edges)
        {
            var a = Index[edge.SourceId];
            var b = Index[edge.TargetId];
            Neighbours[a].Add(b);
            Neighbours[b].Add(a);
        }

        InitializePositions();
    }

    public GraphDocument Graph { get; }

    /// <summary>Nœuds à déplacer (les nœuds verrouillés sont exclus).</summary>
    public List<GraphNode> Nodes { get; }

    /// <summary>Arêtes dont les deux extrémités sont déplaçables.</summary>
    public List<GraphEdge> Edges { get; }

    public int Count { get; }

    public Dictionary<string, int> Index { get; } = new(StringComparer.Ordinal);

    public double[] Px { get; }

    public double[] Py { get; }

    public double[] Fx { get; }

    public double[] Fy { get; }

    /// <summary>Voisins de chaque nœud, pour les plus courts chemins.</summary>
    public List<int>[] Neighbours { get; }

    private void InitializePositions()
    {
        var random = new Random(42);

        for (var i = 0; i < Count; i++)
        {
            Px[i] = Nodes[i].X;
            Py[i] = Nodes[i].Y;

            // Superposition : on disperse autour d'un cercle pour ne pas partir
            // d'un état dégénéré où toutes les forces s'annulent.
            if (Px[i] == 0 && Py[i] == 0)
            {
                var angle = random.NextDouble() * Math.PI * 2;
                var radius = 60 + random.NextDouble() * 240;
                Px[i] = Math.Cos(angle) * radius;
                Py[i] = Math.Sin(angle) * radius;
            }
        }
    }

    /// <summary>Applique les positions calculées au document, en un seul envoi.</summary>
    public void CommitToGraph()
    {
        Graph.BeginBatch();
        try
        {
            for (var i = 0; i < Count; i++)
            {
                Nodes[i].MoveTo(Math.Round(Px[i], 2), Math.Round(Py[i], 2));
            }
        }
        finally
        {
            Graph.EndBatch();
        }
    }

    /// <summary>
    /// Borne la disposition : la répulsion a une longue portée et peut faire
    /// dériver les nœuds isolés très loin si le graphe est peu connecté.
    /// </summary>
    public void ClampToLayout(double spacing)
        => ClampTo(spacing * Math.Sqrt(Math.Max(Count, 1)) * 6);

    /// <summary>Recentre la disposition sur l'origine.</summary>
    public void CenterOnOrigin()
    {
        if (Count == 0)
        {
            return;
        }

        var cx = Px.Average();
        var cy = Py.Average();

        for (var i = 0; i < Count; i++)
        {
            Px[i] -= cx;
            Py[i] -= cy;
        }
    }

    public void ClampTo(double bounds)
    {
        for (var i = 0; i < Count; i++)
        {
            Px[i] = Math.Clamp(Px[i], -bounds, bounds);
            Py[i] = Math.Clamp(Py[i], -bounds, bounds);
        }
    }
}

/// <summary>
/// Mises en page automatiques du graphe, toutes calculées en C#.
/// Les algorithmes « force-based » combinent une force de répulsion entre tous
/// les nœuds et une force d'attraction le long des arêtes, puis itèrent jusqu'à
/// un équilibre ou un refroidissement.
/// </summary>
public static class GraphLayout
{
    /// <summary>
    /// Force dirigée : répulsion de Coulomb et ressorts de Hooke, avec
    /// refroidissement. Bon compromis qualité / coût, stable sur les graphes moyens.
    /// </summary>
    public static void ForceDirected(GraphDocument graph, LayoutOptions? options = null)
    {
        var ctx = Create(graph);
        if (ctx is null)
        {
            return;
        }

        options ??= new LayoutOptions();
        var spacing = options.Spacing;

        for (var step = 0; step < options.Iterations; step++)
        {
            Array.Clear(ctx.Fx);
            Array.Clear(ctx.Fy);

            // Répulsion : force inversement proportionnelle au carré de la distance.
            var repulsion = spacing * spacing * 2.7;
            for (var i = 0; i < ctx.Count; i++)
            {
                for (var j = i + 1; j < ctx.Count; j++)
                {
                    var dx = ctx.Px[i] - ctx.Px[j];
                    var dy = ctx.Py[i] - ctx.Py[j];
                    var dist = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1.0);
                    var force = repulsion / (dist * dist);
                    var ux = dx / dist * force;
                    var uy = dy / dist * force;
                    ctx.Fx[i] += ux; ctx.Fy[i] += uy;
                    ctx.Fx[j] -= ux; ctx.Fy[j] -= uy;
                }
            }

            // Attraction : ressort dont la longueur de repos est l'espacement.
            foreach (var edge in ctx.Edges)
            {
                var a = ctx.Index[edge.SourceId];
                var b = ctx.Index[edge.TargetId];

                var dx = ctx.Px[b] - ctx.Px[a];
                var dy = ctx.Py[b] - ctx.Py[a];
                var dist = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.001);
                var force = (dist - spacing) * 0.02;
                var ux = dx / dist * force;
                var uy = dy / dist * force;
                ctx.Fx[a] += ux; ctx.Fy[a] += uy;
                ctx.Fx[b] -= ux; ctx.Fy[b] -= uy;
            }

            // Rappel au centre : empêche la dérive du nuage.
            for (var i = 0; i < ctx.Count; i++)
            {
                ctx.Fx[i] -= ctx.Px[i] * 0.01;
                ctx.Fy[i] -= ctx.Py[i] * 0.01;
            }

            // Refroidissement linéaire + déplacement borné.
            var cooling = 1.0 - step / (double)options.Iterations;
            Integrate(ctx, cooling: cooling, maxDisplacement: spacing / 3.5);
        }

        ctx.ClampToLayout(options.Spacing);
        ctx.CommitToGraph();
    }

    /// <summary>
    /// Fruchterman–Reingold (1996) : répulsion k²/d, attraction d²/k, et
    /// température décroissante qui borne le déplacement à chaque itération.
    /// </summary>
    public static void FruchtermanReingold(GraphDocument graph, LayoutOptions? options = null)
    {
        var ctx = Create(graph);
        if (ctx is null)
        {
            return;
        }

        options ??= new LayoutOptions();
        var n = ctx.Count;
        var k = options.Spacing;

        // Température initiale : un dixième de la largeur utile.
        var temperature = k * 1.2;
        var coolingRate = temperature / (options.Iterations + 1);

        for (var step = 0; step < options.Iterations; step++)
        {
            Array.Clear(ctx.Fx);
            Array.Clear(ctx.Fy);

            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    var dx = ctx.Px[i] - ctx.Px[j];
                    var dy = ctx.Py[i] - ctx.Py[j];
                    var dist = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.01);

                    // Fr = k²/d
                    var repulsive = (k * k) / dist;
                    var rx = dx / dist * repulsive;
                    var ry = dy / dist * repulsive;
                    ctx.Fx[i] += rx; ctx.Fy[i] += ry;
                    ctx.Fx[j] -= rx; ctx.Fy[j] -= ry;
                }
            }

            foreach (var edge in ctx.Edges)
            {
                var a = ctx.Index[edge.SourceId];
                var b = ctx.Index[edge.TargetId];

                // dx pointe de b vers a : l'attraction doit donc retrancher.
                var dx = ctx.Px[a] - ctx.Px[b];
                var dy = ctx.Py[a] - ctx.Py[b];
                var dist = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.01);

                // Fa = d²/k
                var attractive = (dist * dist) / k;
                var ax = dx / dist * attractive;
                var ay = dy / dist * attractive;
                ctx.Fx[a] -= ax; ctx.Fy[a] -= ay;
                ctx.Fx[b] += ax; ctx.Fy[b] += ay;
            }

            // Déplacement borné par la température.
            for (var i = 0; i < n; i++)
            {
                var length = Math.Sqrt(ctx.Fx[i] * ctx.Fx[i] + ctx.Fy[i] * ctx.Fy[i]);
                if (length < 1e-9)
                {
                    continue;
                }

                var limited = Math.Min(length, temperature);
                ctx.Px[i] += ctx.Fx[i] / length * limited;
                ctx.Py[i] += ctx.Fy[i] / length * limited;
            }

            temperature -= coolingRate;
        }

        ctx.ClampToLayout(options.Spacing);
        ctx.CenterOnOrigin();
        ctx.CommitToGraph();
    }

    /// <summary>
    /// Eades, « Spring Embedder » (1996) : la répulsion est calculée entre
    /// toutes les paires, puis chaque arête est traitée individuellement pour
    /// supprimer les attractions parasites. Bien plus rapide que
    /// Fruchterman–Reingold sur les graphes denses.
    /// </summary>
    public static void EadesSpringEmbedder(GraphDocument graph, LayoutOptions? options = null)
    {
        var ctx = Create(graph);
        if (ctx is null)
        {
            return;
        }

        options ??= new LayoutOptions();
        var n = ctx.Count;
        var ideal = options.Spacing;

        for (var step = 0; step < options.Iterations; step++)
        {
            Array.Clear(ctx.Fx);
            Array.Clear(ctx.Fy);

            // 1. Forces répulsives entre toutes les paires de nœuds.
            for (var i = 0; i < n; i++)
            {
                for (var j = 0; j < n; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    var dx = ctx.Px[i] - ctx.Px[j];
                    var dy = ctx.Py[i] - ctx.Py[j];
                    var dist2 = dx * dx + dy * dy;

                    if (dist2 < 0.01)
                    {
                        // Nœuds superposés : petite poussée déterministe.
                        ctx.Fx[i] += ((i % 7) - 3) * 0.5;
                        ctx.Fy[i] += ((j % 5) - 2) * 0.5;
                        continue;
                    }

                    // k²/d
                    var dist = Math.Sqrt(dist2);
                    var force = (ideal * ideal) / (dist * dist);
                    ctx.Fx[i] += dx * force;
                    ctx.Fy[i] += dy * force;
                }
            }

            // 2. Forces attractives, arête par arête (attraction linéaire).
            foreach (var edge in ctx.Edges)
            {
                var a = ctx.Index[edge.SourceId];
                var b = ctx.Index[edge.TargetId];

                var dx = ctx.Px[a] - ctx.Px[b];
                var dy = ctx.Py[a] - ctx.Py[b];
                var dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist < 0.01)
                {
                    continue;
                }

                var force = dist / ideal;
                ctx.Fx[a] -= dx * force;
                ctx.Fy[a] -= dy * force;
                ctx.Fx[b] += dx * force;
                ctx.Fy[b] += dy * force;
            }

            // 3. Limitation par une température décroissante.
            var temperature = ideal * 0.6 * (1.0 - step / (double)options.Iterations);

            for (var i = 0; i < n; i++)
            {
                var length = Math.Sqrt(ctx.Fx[i] * ctx.Fx[i] + ctx.Fy[i] * ctx.Fy[i]);
                if (length < 1e-9)
                {
                    continue;
                }

                var limited = Math.Min(length, Math.Max(temperature, 0.01));
                ctx.Px[i] += ctx.Fx[i] / length * limited;
                ctx.Py[i] += ctx.Fy[i] / length * limited;
            }
        }
        ctx.ClampToLayout(options.Spacing);

        ctx.CenterOnOrigin();
        ctx.CommitToGraph();
    }

    /// <summary>
    /// Kamada–Kawai (1989) : relaxation de la « contrainte de stress ». Les
    /// distances euclidiennes sont rapprochées des distances du graphe, par
    /// majorisation. Résultat très régulier, mais coût en O(n²) pour le calcul
    /// des plus courts chemins.
    /// </summary>
    public static void KamadaKawai(GraphDocument graph, LayoutOptions? options = null)
    {
        var ctx = Create(graph);
        if (ctx is null)
        {
            return;
        }

        options ??= new LayoutOptions();
        var n = ctx.Count;

        // Au-delà de ce seuil, le calcul des distances devient prohibitif :
        // on rebascule sur l'algorithme à forces, plus rapide.
        const int MaxNodesForStressMajorization = 220;

        if (n > MaxNodesForStressMajorization)
        {
            ForceDirected(graph, options);
            return;
        }

        var distances = AllPairsShortestPaths(ctx);

        // Poids de chaque paire : dsmnt la distance en nombre d'arêtes.
        var weights = new double[n, n];
        var target = new double[n, n];

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                if (i == j)
                {
                    continue;
                }

                // Les paires non connectées reçoivent un poids nul.
                var d = distances[i, j];
                if (d <= 0)
                {
                    continue;
                }

                weights[i, j] = 1.0 / (d * d);
                target[i, j] = d * options.Spacing;
            }
        }

        for (var step = 0; step < options.Iterations; step++)
        {
            var maxMove = 0.0;

            for (var i = 0; i < n; i++)
            {
                var denominator = 0.0;
                var numeratorX = 0.0;
                var numeratorY = 0.0;

                for (var j = 0; j < n; j++)
                {
                    var w = weights[i, j];
                    if (w == 0)
                    {
                        continue;
                    }

                    var dx = ctx.Px[i] - ctx.Px[j];
                    var dy = ctx.Py[i] - ctx.Py[j];
                    var dist = Math.Sqrt(dx * dx + dy * dy);

                    // (L/d − 1) est négatif quand d > L : le pas rapproche donc
                    // les deux nœuds. On applique −grad(S), pas +grad(S).
                    var scale = dist < 1e-6 ? 0 : w * (target[i, j] / dist - 1.0);
                    numeratorX += scale * dx;
                    numeratorY += scale * dy;
                    denominator += w;
                }

                if (denominator <= 0)
                {
                    continue;
                }

                // Pas de Newton : 1 / Σw borné par la distance minimale.
                var stepSize = 1.0 / denominator;
                var moveX = numeratorX * stepSize;
                var moveY = numeratorY * stepSize;

                var length = Math.Sqrt(moveX * moveX + moveY * moveY);
                var limit = options.Spacing / 4.0;

                if (length > limit)
                {
                    moveX = moveX / length * limit;
                    moveY = moveY / length * limit;
                    length = limit;
                }

                ctx.Px[i] += moveX;
                ctx.Py[i] += moveY;
                maxMove = Math.Max(maxMove, length);
            }

            // Convergence : plus rien ne bouge de façon significative.
            if (maxMove < 0.05)
            {
                break;
            }
        ctx.ClampToLayout(options.Spacing);
        }

        ctx.CenterOnOrigin();
        ctx.CommitToGraph();
    }

    /// <summary>
    /// Disposition en couches : les nœuds sont répartis par profondeur depuis
    /// les racines, ce qui met en évidence le sens de propagation.
    /// </summary>
    public static void Hierarchical(GraphDocument graph, LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new LayoutOptions();

        var nodes = graph.Nodes.ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        var layerGap = options.Spacing * 1.45;
        var nodeGap = options.Spacing;

        graph.BeginBatch();
        try
        {
            var incoming = nodes.ToDictionary(
                n => n.Id,
                n => graph.Edges.Count(e => e.TargetId == n.Id && e.SourceId != n.Id));

            var outgoing = nodes.ToDictionary(
                n => n.Id,
                n => graph.Edges.Where(e => e.SourceId == n.Id && e.TargetId != n.Id)
                    .Select(e => e.TargetId).ToList());

            var layerOf = nodes.ToDictionary(n => n.Id, _ => 0);
            var queue = new Queue<string>();

            foreach (var n in nodes.Where(n => incoming[n.Id] == 0
                                              || outgoing[n.Id].Count >= incoming[n.Id]))
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

            // Les nœuds restants forment un cycle : on les empile à la suite.
            var offset = layerOf.Values.DefaultIfEmpty(0).Max() + 1;
            foreach (var n in nodes.Where(n => !visited.Contains(n.Id)))
            {
                layerOf[n.Id] = offset++;
            }

            foreach (var group in nodes.GroupBy(n => layerOf[n.Id]).OrderBy(g => g.Key))
            {
                var ordered = group
                    .OrderByDescending(n => incoming[n.Id])
                    .ThenBy(n => n.Id, StringComparer.Ordinal)
                    .ToList();

                // Centrage de chaque couche sur l'axe vertical.
                var x = -ordered.Count * nodeGap / 2.0 + nodeGap / 2.0;
                foreach (var n in ordered)
                {
                    n.MoveTo(Math.Round(x, 2), Math.Round(group.Key * layerGap, 2));
                    x += nodeGap;
                }
            }

            // Recentrage global pour éviter une dérive vers la droite.
            var offsetX = nodes.Count > 0 ? nodes.Max(n => n.X) / 2.0 : 0;
            foreach (var n in nodes)
            {
                n.MoveTo(n.X - offsetX, n.Y);
            }
        }
        finally
        {
            graph.EndBatch();
        }
    }

    /// <summary>Nœuds sur un cercle, ordonnés pour limiter les croisements.</summary>
    public static void Circular(GraphDocument graph, LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new LayoutOptions();

        var nodes = graph.Nodes.ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        var radius = Math.Max(options.Spacing * Math.Max(nodes.Count, 3) / (2 * Math.PI), options.Spacing);

        graph.BeginBatch();
        try
        {
            foreach (var (node, index) in OrderForCircle(graph).Select((id, i) => (id, i)))
            {
                var angle = 2 * Math.PI * index / nodes.Count - Math.PI / 2;
                node.MoveTo(Math.Round(Math.Cos(angle) * radius, 2), Math.Round(Math.Sin(angle) * radius, 2));
            }
        }
        finally
        {
            graph.EndBatch();
        }
    }

    /// <summary>
    /// Disposition radiale : une couche par distance au nœud le plus central,
    /// chaque couche formant un cercle. Rend visibles les sous-groupes.
    /// </summary>
    public static void Radial(GraphDocument graph, LayoutOptions? options = null)
    {
        var ctx = Create(graph);
        if (ctx is null)
        {
            return;
        }

        options ??= new LayoutOptions();
        var n = ctx.Count;
        var ringGap = options.Spacing * 1.1;

        var distances = AllPairsShortestPaths(ctx);

        // Centre = nœud le plus proche de tous les autres (moyenne la plus basse).
        var center = 0;
        var best = double.MaxValue;

        for (var i = 0; i < n; i++)
        {
            var sum = 0.0;
            var reachable = 0;

            for (var j = 0; j < n; j++)
            {
                var d = distances[i, j];
                if (d > 0)
                {
                    sum += d;
                    reachable++;
                }
            }

            if (reachable == n - 1 && sum < best)
            {
                best = sum;
                center = i;
            }
        }

        var layers = new Dictionary<int, List<int>>();
        for (var i = 0; i < n; i++)
        {
            var depth = Math.Max(distances[center, i], 0);
            if (!layers.TryGetValue(depth, out var list))
            {
                list = [];
                layers[depth] = list;
            }

            list.Add(i);
        }

        for (var i = 0; i < n; i++)
        {
            var depth = Math.Max(distances[center, i], 0);
            var ring = layers[depth];
            var position = ring.IndexOf(i);
            var radius = depth * ringGap;

            if (ring.Count == 1 || radius < 1)
            {
                ctx.Px[i] = 0;
                ctx.Py[i] = 0;
                continue;
            }

            var angle = 2 * Math.PI * position / ring.Count - Math.PI / 2;
            ctx.Px[i] = Math.Cos(angle) * radius;
            ctx.Py[i] = Math.Sin(angle) * radius;
        }

        ctx.CommitToGraph();
    }

    /// <summary>Grille régulière, utile pour générer de grands lots de nœuds.</summary>
    public static void Grid(GraphDocument graph, LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new LayoutOptions();

        var nodes = graph.Nodes.ToList();
        var columns = Math.Max(options.Columns, 1);

        graph.BeginBatch();
        try
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                nodes[i].MoveTo(
                    i % columns * options.Spacing * 1.2,
                    i / columns * options.Spacing);
            }
        }
        finally
        {
            graph.EndBatch();
        }
    }

    /// <summary>Positions aléatoires, mainly pour les tests et le jeu de données.</summary>
    public static void Random(GraphDocument graph, LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new LayoutOptions();

        var random = new Random(options.Seed);
        var extent = options.Spacing * 4;

        graph.BeginBatch();
        try
        {
            foreach (var node in graph.Nodes.Where(n => !n.Locked))
            {
                node.MoveTo(
                    Math.Round(random.NextDouble() * 2 * extent - extent, 2),
                    Math.Round(random.NextDouble() * 2 * extent - extent, 2));
            }
        }
        finally
        {
            graph.EndBatch();
        }
    }

    // ------------------------------------------------------------------ interne

    private static LayoutContext? Create(GraphDocument graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodes = graph.Nodes.Where(n => !n.Locked).ToList();
        if (nodes.Count <= 1)
        {
            return null;
        }

        var ids = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        // Les boucles sont exclues : leur « ressort » relierait un nœud à
        // lui-même, donc de distance nulle, et produirait une force infinie.
        var edges = graph.Edges
            .Where(e => ids.Contains(e.SourceId) && ids.Contains(e.TargetId) && e.SourceId != e.TargetId)
            .ToList();

        return new LayoutContext(graph, nodes, edges);
    }

    private static void Integrate(LayoutContext ctx, double cooling, double maxDisplacement)
    {
        for (var i = 0; i < ctx.Count; i++)
        {
            var dx = ctx.Fx[i] * cooling;
            var dy = ctx.Fy[i] * cooling;

            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length > maxDisplacement)
            {
                var scale = maxDisplacement / length;
                dx *= scale;
                dy *= scale;
            }

            ctx.Px[i] += dx;
            ctx.Py[i] += dy;
        }
    }

    /// <summary>
    /// Plus courts chemins en nombre d'arêtes, par BFS depuis chaque nœud.
    /// 0 signifie « non connecté ».
    /// </summary>
    private static int[,] AllPairsShortestPaths(LayoutContext ctx)
    {
        var n = ctx.Count;
        var result = new int[n, n];

        for (var source = 0; source < n; source++)
        {
            var queue = new Queue<int>();
            queue.Enqueue(source);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var next in ctx.Neighbours[current])
                {
                    if (result[source, next] != 0)
                    {
                        continue;
                    }

                    result[source, next] = result[source, current] + 1;
                    queue.Enqueue(next);
                }
            }

            result[source, source] = -1;
        }

        return result;
    }

    /// <summary>
    /// Ordre de parcours circulaire : on part du nœud le plus connecté et on
    /// avance en profondeur, ce qui rapproche les voisins sur l'anneau.
    /// </summary>
    private static List<GraphNode> OrderForCircle(GraphDocument graph)
    {
        var nodes = graph.Nodes.ToList();
        var ordered = new List<GraphNode>(nodes.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var roots = nodes
            .OrderByDescending(n => graph.EdgesOf(n.Id).Count)
            .ThenBy(n => n.Id, StringComparer.Ordinal);

        foreach (var root in roots)
        {
            if (!seen.Add(root.Id))
            {
                continue;
            }

            ordered.Add(root);

            var queue = new Queue<string>();
            queue.Enqueue(root.Id);

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();

                foreach (var neighbour in graph.EdgesOf(id))
                {
                    var next = neighbour.SourceId == id ? neighbour.TargetId : neighbour.SourceId;

                    if (seen.Add(next) && graph.FindNode(next) is { } node)
                    {
                        ordered.Add(node);
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return ordered;
    }
}
