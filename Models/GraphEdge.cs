using System.Text.Json.Serialization;

namespace TestCanvas.Models;

/// <summary>Une arête orientée (ou non) reliant deux nœuds du document.</summary>
public sealed class GraphEdge : ObservableObject
{
    private string _label = string.Empty;
    private string _type = "flow";
    private string _color = "#64748b";
    private double _width = 2;
    private bool _dashed;
    private bool _directed = true;
    private bool _selected;
    private bool _visible = true;
    private bool _highlighted;
    private double _curvature = 0.25;
    private EdgeStyle _style = EdgeStyle.Straight;
    private Dictionary<string, object?>? _data;

    public GraphEdge()
    {
    }

    public GraphEdge(string id, string sourceId, string targetId, string label = "")
    {
        Id = id;
        SourceId = sourceId;
        TargetId = targetId;
        _label = label;
    }

    public string Id { get; set; } = string.Empty;

    /// <summary>Identifiant du nœud de départ.</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Identifiant du nœud d'arrivée.</summary>
    public string TargetId { get; set; } = string.Empty;

    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    /// <summary>
    /// Type métier de l'arête (chaîne libre) : « flux », « dépendance », « retour »…
    /// Utilisé pour le filtrage et la mise en forme, sérialisé avec le graphe.
    /// </summary>
    public string Type
    {
        get => _type;
        set => Set(ref _type, value ?? string.Empty);
    }

    public string Color
    {
        get => _color;
        set => Set(ref _color, value);
    }

    public double Width
    {
        get => _width;
        set => Set(ref _width, Math.Max(0.5, value));
    }

    public bool Dashed
    {
        get => _dashed;
        set => Set(ref _dashed, value);
    }

    public bool Directed
    {
        get => _directed;
        set => Set(ref _directed, value);
    }

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public bool Visible
    {
        get => _visible;
        set => Set(ref _visible, value);
    }

    /// <summary>Mise en avant temporaire (inclus dans un chemin calculé côté C#).</summary>
    public bool Highlighted
    {
        get => _highlighted;
        set => Set(ref _highlighted, value);
    }

    /// <summary>Courbure utilisée par <see cref="EdgeStyle.Curved"/> (0 = droite).</summary>
    public double Curvature
    {
        get => _curvature;
        set => Set(ref _curvature, value);
    }

    public EdgeStyle Style
    {
        get => _style;
        set => Set(ref _style, value);
    }

    /// <summary>Données métier libres, sérialisées avec le graphe.</summary>
    public Dictionary<string, object?> Data
    {
        get => _data ??= new Dictionary<string, object?>();
        set => Set(ref _data, value);
    }

    public bool Involves(string nodeId)
        => string.Equals(SourceId, nodeId, StringComparison.Ordinal)
           || string.Equals(TargetId, nodeId, StringComparison.Ordinal);

    public GraphEdge Clone() => new()
    {
        Id = Id,
        SourceId = SourceId,
        TargetId = TargetId,
        Label = Label,
        Type = Type,
        Color = Color,
        Width = Width,
        Dashed = Dashed,
        Directed = Directed,
        Selected = Selected,
        Visible = Visible,
        Highlighted = Highlighted,
        Curvature = Curvature,
        Style = Style,
        Data = new Dictionary<string, object?>(Data)
    };

    public override string ToString() => $"{SourceId} -> {TargetId}";
}