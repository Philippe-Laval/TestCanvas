using Microsoft.JSInterop;
using TestCanvas.Models;

namespace TestCanvas.Components.Graph;

/// <summary>
/// Objet exposé au JavaScript. Les méthodes <c>[JSInvokable]</c> relient les
/// interactions du canvas au composant Blazor.
/// </summary>
public sealed class GraphCanvasBridge
{
    /// <summary>Composant propriétaire. Null après <c>DisposeAsync</c>.</summary>
    public GraphCanvas? Canvas { get; set; }

    [JSInvokable]
    public Task NotifyNodeMoved(string nodeId, double x, double y, string[] moved)
        => Canvas?.NotifyNodeMovedAsync(nodeId, x, y, moved) ?? Task.CompletedTask;

    [JSInvokable]
    public Task NotifySelectionChanged(string[] nodeIds, string? edgeId)
        => Canvas?.NotifySelectionChangedAsync(nodeIds, edgeId) ?? Task.CompletedTask;

    [JSInvokable]
    public Task NotifyLinkRequested(string sourceId, string targetId)
        => Canvas?.NotifyLinkRequestedAsync(sourceId, targetId) ?? Task.CompletedTask;

    [JSInvokable]
    public Task NotifyNodeDoubleClicked(string nodeId)
        => Canvas?.NotifyNodeDoubleClickedAsync(nodeId) ?? Task.CompletedTask;

    [JSInvokable]
    public Task NotifyCanvasDoubleClicked(double x, double y)
        => Canvas?.NotifyCanvasDoubleClickedAsync(x, y) ?? Task.CompletedTask;

    [JSInvokable]
    public Task NotifyContextMenu(string? id, string kind, double x, double y, double offsetX, double offsetY)
        => Canvas?.NotifyContextMenuAsync(id, kind, x, y, offsetX, offsetY) ?? Task.CompletedTask;

    [JSInvokable]
    public Task NotifyKeyDown(string key, bool ctrl, bool shift)
        => Canvas?.NotifyKeyDownAsync(key, ctrl, shift) ?? Task.CompletedTask;
}

/// <summary>Position et zoom courants du canvas.</summary>
public sealed class GraphCanvasView
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Zoom { get; set; }

    public override string ToString() => $"zoom {Zoom * 100:0}%";
}

/// <summary>Position d'un nœud à l'écran, en pixels absolus.</summary>
public sealed class GraphScreenPosition
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Radius { get; set; }
}

/// <summary>Point dans le repère du graphe.</summary>
public sealed class GraphPoint
{
    public GraphPoint()
    {
    }

    public GraphPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; set; }

    public double Y { get; set; }
}

/// <summary>Événement « clic droit » transmis depuis le canvas.</summary>
public sealed class GraphContextMenuEvent
{
    public GraphContextMenuEvent(string? id, string kind, double x, double y, double offsetX, double offsetY)
    {
        Id = id;
        Kind = kind;
        X = x;
        Y = y;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    /// <summary>Identifiant du nœud ou de l'arête sous le curseur, sinon null.</summary>
    public string? Id { get; }

    /// <summary><c>node</c>, <c>edge</c> ou <c>canvas</c>.</summary>
    public string Kind { get; }

    /// <summary>Position dans le repère du graphe.</summary>
    public double X { get; }

    public double Y { get; }

    /// <summary>Position relative au coin supérieur gauche du canvas (pour un menu HTML).</summary>
    public double OffsetX { get; }

    public double OffsetY { get; }
}

/// <summary>Options de rendu et d'interaction du canvas.</summary>
public sealed class GraphCanvasOptions
{
    /// <summary>Affiche la grille d'arrière-plan.</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>Taille d'une cellule de la grille, en pixels du graphe.</summary>
    public double GridSize { get; set; } = 24;

    /// <summary>Impose les coordonnées des nœuds sur la grille lors du déplacement.</summary>
    public bool SnapToGrid { get; set; }

    public double MinZoom { get; set; } = 0.15;

    public double MaxZoom { get; set; } = 5;

    /// <summary>Épaisseur du contour des nœuds, en pixels à l'écran.</summary>
    public double NodeBorderWidth { get; set; } = 2;

    /// <summary>Couleur de la sélection (halo).</summary>
    public string SelectionColor { get; set; } = "#38bdf8";

    public string FontFamily { get; set; } = "'Segoe UI', system-ui, -apple-system, sans-serif";

    public string BackgroundColor { get; set; } = "#0f172a";

    public string GridColor { get; set; } = "rgba(148, 163, 184, 0.10)";

    public string GridStrongColor { get; set; } = "rgba(148, 163, 184, 0.22)";

    public string DefaultEdgeColor { get; set; } = "#94a3b8";

    /// <summary>Projette l'objet en dictionnaire camelCase pour l'interop JS.</summary>
    public Dictionary<string, object?> ToDictionary() => new(StringComparer.Ordinal)
    {
        ["showGrid"] = ShowGrid,
        ["gridSize"] = GridSize,
        ["snapToGrid"] = SnapToGrid,
        ["minZoom"] = MinZoom,
        ["maxZoom"] = MaxZoom,
        ["nodeBorderWidth"] = NodeBorderWidth,
        ["selectionColor"] = SelectionColor,
        ["fontFamily"] = FontFamily,
        ["theme"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["background"] = BackgroundColor,
            ["grid"] = GridColor,
            ["gridStrong"] = GridStrongColor,
            ["edge"] = DefaultEdgeColor
        }
    };
}