using TestCanvas.Models;

namespace TestCanvas.Services;

/// <summary>Famille d'algorithmes, utilisée pour regrouper le menu.</summary>
public enum LayoutFamily
{
    /// <summary>Algorithmes force-based : répulsion + attraction, relaxation itérative.</summary>
    Force,

    /// <summary>Mises en page structurées, fondées sur la topologie du graphe.</summary>
    Structured,

    /// <summary>Dispositions géométriques, sans prise en compte des arêtes.</summary>
    Geometric
}

/// <summary>Algorithmes de disposition disponibles.</summary>
public enum LayoutAlgorithm
{
    ForceDirected,
    FruchtermanReingold,
    EadesSpringEmbedder,
    KamadaKawai,
    Hierarchical,
    Radial,
    Circular,
    Grid,
    Random
}

/// <summary>Fiche descriptive d'un algorithme, affichée dans le menu.</summary>
public sealed record LayoutDescriptor(
    LayoutAlgorithm Algorithm,
    string Name,
    string Summary,
    LayoutFamily Family)
{
    /// <summary>Applique l'algorithme au document.</summary>
    public void Apply(GraphDocument graph, LayoutOptions options)
    {
        switch (Algorithm)
        {
            case LayoutAlgorithm.ForceDirected:
                GraphLayout.ForceDirected(graph, options);
                break;
            case LayoutAlgorithm.FruchtermanReingold:
                GraphLayout.FruchtermanReingold(graph, options);
                break;
            case LayoutAlgorithm.EadesSpringEmbedder:
                GraphLayout.EadesSpringEmbedder(graph, options);
                break;
            case LayoutAlgorithm.KamadaKawai:
                GraphLayout.KamadaKawai(graph, options);
                break;
            case LayoutAlgorithm.Hierarchical:
                GraphLayout.Hierarchical(graph, options);
                break;
            case LayoutAlgorithm.Radial:
                GraphLayout.Radial(graph, options);
                break;
            case LayoutAlgorithm.Circular:
                GraphLayout.Circular(graph, options);
                break;
            case LayoutAlgorithm.Grid:
                GraphLayout.Grid(graph, options);
                break;
            case LayoutAlgorithm.Random:
                GraphLayout.Random(graph, options);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Algorithm), Algorithm, "Algorithme inconnu.");
        }
    }
}

/// <summary>Catalogue des algorithmes, dans l'ordre d'affichage du menu.</summary>
public static class LayoutAlgorithms
{
    public static readonly LayoutDescriptor ForceDirectedDescriptor = new(
        LayoutAlgorithm.ForceDirected,
        "Force dirigée",
        "Répulsion de Coulomb et ressorts de Hooke, avec refroidissement. Bon compromis qualité / coût.",
        LayoutFamily.Force);

    public static readonly LayoutDescriptor FruchtermanReingoldDescriptor = new(
        LayoutAlgorithm.FruchtermanReingold,
        "Fruchterman–Reingold",
        "Forces k²/d et d²/k, température décroissante. Très spread, faible taux de croisements.",
        LayoutFamily.Force);

    public static readonly LayoutDescriptor EadesDescriptor = new(
        LayoutAlgorithm.EadesSpringEmbedder,
        "Spring Embedder (Eades)",
        "Attraction traitée arête par arête. Plus rapide que Fruchterman–Reingold sur les graphes denses.",
        LayoutFamily.Force);

    public static readonly LayoutDescriptor KamadaKawaiDescriptor = new(
        LayoutAlgorithm.KamadaKawai,
        "Kamada–Kawai",
        "Relaxation du stress : les distances euclidiennes rejoignent celles du graphe. Très régulier, O(n²).",
        LayoutFamily.Force);

    public static readonly LayoutDescriptor HierarchicalDescriptor = new(
        LayoutAlgorithm.Hierarchical,
        "Hiérarchique",
        "Une couche par profondeur depuis les racines. Met en évidence le sens de propagation.",
        LayoutFamily.Structured);

    public static readonly LayoutDescriptor RadialDescriptor = new(
        LayoutAlgorithm.Radial,
        "Radiale",
        "Un cercle par distance au nœud le plus central. Rend visibles les sous-groupes.",
        LayoutFamily.Structured);

    public static readonly LayoutDescriptor CircularDescriptor = new(
        LayoutAlgorithm.Circular,
        "Circulaire",
        "Nœuds sur un anneau, ordonnés par parcours pour limiter les croisements.",
        LayoutFamily.Geometric);

    public static readonly LayoutDescriptor GridDescriptor = new(
        LayoutAlgorithm.Grid,
        "Grille",
        "Disposition régulière en lignes et colonnes.",
        LayoutFamily.Geometric);

    public static readonly LayoutDescriptor RandomDescriptor = new(
        LayoutAlgorithm.Random,
        "Aléatoire",
        "Positions tirées au hasard. Utile pour les tests et les jeux de données.",
        LayoutFamily.Geometric);

    /// <summary>Tous les algorithmes, dans l'ordre d'affichage.</summary>
    public static IReadOnlyList<LayoutDescriptor> All { get; } =
    [
        ForceDirectedDescriptor,
        FruchtermanReingoldDescriptor,
        EadesDescriptor,
        KamadaKawaiDescriptor,
        HierarchicalDescriptor,
        RadialDescriptor,
        CircularDescriptor,
        GridDescriptor,
        RandomDescriptor
    ];

    public static LayoutDescriptor Get(LayoutAlgorithm algorithm)
        => All.FirstOrDefault(d => d.Algorithm == algorithm)
           ?? throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Algorithme inconnu.");

    /// <summary>Libellé du groupe, tel qu'affiché dans le menu.</summary>
    public static string FamilyLabel(LayoutFamily family) => family switch
    {
        LayoutFamily.Force => "Basées sur les forces",
        LayoutFamily.Structured => "Selon la topologie",
        LayoutFamily.Geometric => "Géométriques",
        _ => "Autres"
    };

    /// <summary>Description courte de l'algorithme, pour l'infobulle du bouton.</summary>
    public static string Describe(LayoutFamily family) => family switch
    {
        LayoutFamily.Force =>
            "Système de forces appliqué entre les nœuds et les arcs : répulsion entre nœuds, "
            + "attraction le long des arêtes, puis itération jusqu'à l'équilibre.",
        LayoutFamily.Structured =>
            "La disposition découle de la structure du graphe : profondeurs, voisinages, cycles.",
        _ =>
            "La disposition ne dépend que du nombre de nœuds, pas des arêtes."
    };
}