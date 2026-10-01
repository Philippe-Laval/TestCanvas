using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestCanvas.Models;

/// <summary>
/// Un nœud du graphe. Les coordonnées sont exprimées dans le repère du graphe
/// (indépendant du zoom et du panoramique de la vue).
/// </summary>
public sealed class GraphNode : ObservableObject
{
    private string _label = string.Empty;
    private double _x;
    private double _y;
    private double _radius = 30;
    private double _fontSize = 13;
    private NodeShape _shape = NodeShape.Circle;
    private string _fill = "#3b82f6";
    private string _stroke = "#1d4ed8";
    private string _textColor = "#ffffff";
    private bool _selected;
    private bool _locked;
    private bool _visible = true;
    private bool _highlighted;
    private Dictionary<string, object?>? _data;

    public GraphNode()
    {
    }

    public GraphNode(string id, string label = "Nœud", double x = 0, double y = 0)
    {
        Id = id;
        _label = label;
        _x = x;
        _y = y;
    }

    public GraphNode(string id, string label, double x, double y, NodeShape shape, string fill)
        : this(id, label, x, y)
    {
        _shape = shape;
        _fill = fill;
        _stroke = Darken(fill);
    }

    /// <summary>Identifiant unique au sein du document.</summary>
    public string Id { get; set; } = string.Empty;

    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    /// <summary>Abscisse dans le repère du graphe.</summary>
    public double X
    {
        get => _x;
        set => Set(ref _x, value);
    }

    /// <summary>Ordonnée dans le repère du graphe.</summary>
    public double Y
    {
        get => _y;
        set => Set(ref _y, value);
    }

    /// <summary>Rayon (demi-dimension) du nœud en pixels du graphe.</summary>
    public double Radius
    {
        get => _radius;
        set => Set(ref _radius, Math.Max(4, value));
    }

    public double FontSize
    {
        get => _fontSize;
        set => Set(ref _fontSize, Math.Clamp(value, 6, 48));
    }

    public NodeShape Shape
    {
        get => _shape;
        set => Set(ref _shape, value);
    }

    /// <summary>Couleur de remplissage.</summary>
    public string Fill
    {
        get => _fill;
        set => Set(ref _fill, value);
    }

    /// <summary>Couleur du contour.</summary>
    public string Stroke
    {
        get => _stroke;
        set => Set(ref _stroke, value);
    }

    public string TextColor
    {
        get => _textColor;
        set => Set(ref _textColor, value);
    }

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    /// <summary>Un nœud verrouillé ne peut pas être déplacé à la souris.</summary>
    public bool Locked
    {
        get => _locked;
        set => Set(ref _locked, value);
    }

    public bool Visible
    {
        get => _visible;
        set => Set(ref _visible, value);
    }

    /// <summary>Mise en avant temporaire (résultat d'un calcul de chemin, recherche…).</summary>
    public bool Highlighted
    {
        get => _highlighted;
        set => Set(ref _highlighted, value);
    }

    /// <summary>Données métier libres, sérialisées avec le graphe.</summary>
    public Dictionary<string, object?> Data
    {
        get => _data ??= new Dictionary<string, object?>();
        set => Set(ref _data, value);
    }

    /// <summary>Position courante sous forme de tuple (non sérialisée).</summary>
    [JsonIgnore]
    public (double X, double Y) Position => (X, Y);

    /// <summary>
    /// Déplace le nœud en émettant une seule notification (les propriétés X et Y
    /// sont écrites directement pour ne pas doubler les allers-retours vers le canvas).
    /// </summary>
    public void MoveTo(double x, double y)
    {
        if (Math.Abs(_x - x) < double.Epsilon && Math.Abs(_y - y) < double.Epsilon)
        {
            return;
        }

        _x = x;
        _y = y;
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Position));
    }

    public GraphNode Clone() => new()
    {
        Id = Id,
        Label = Label,
        X = X,
        Y = Y,
        Radius = Radius,
        FontSize = FontSize,
        Shape = Shape,
        Fill = Fill,
        Stroke = Stroke,
        TextColor = TextColor,
        Selected = Selected,
        Locked = Locked,
        Visible = Visible,
        Highlighted = Highlighted,
        Data = new Dictionary<string, object?>(Data)
    };

    public override string ToString() => $"{Id}:{Label} ({X:0.#},{Y:0.#})";

    /// <summary>Assombrit une couleur hexadécimale (#rrggbb) d'un facteur donné.</summary>
    internal static string Darken(string hex, double factor = 0.25)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 7 || !hex.StartsWith('#'))
        {
            return hex;
        }

        static int Channel(string s, int index) =>
            int.Parse(s.AsSpan(index, 2), System.Globalization.NumberStyles.HexNumber);

        var r = (int)Math.Round(Channel(hex, 1) * (1 - factor));
        var g = (int)Math.Round(Channel(hex, 3) * (1 - factor));
        var b = (int)Math.Round(Channel(hex, 5) * (1 - factor));
        return $"#{Math.Clamp(r, 0, 255):x2}{Math.Clamp(g, 0, 255):x2}{Math.Clamp(b, 0, 255):x2}";
    }
}