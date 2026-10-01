using System.Text.Json.Serialization;

namespace TestCanvas.Models;

/// <summary>Forme d'un nœud telle que comprise par le moteur de rendu JavaScript.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NodeShape>))]
public enum NodeShape
{
    Circle,
    Square,
    RoundedRectangle,
    Diamond,
    Hexagon,
    Triangle
}

/// <summary>Tracé d'une arête.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EdgeStyle>))]
public enum EdgeStyle
{
    Straight,
    Curved,
    Orthogonal
}

/// <summary>Nature d'une modification du document, transmise au canvas.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GraphChangeKind>))]
public enum GraphChangeKind
{
    /// <summary>Le document a été entièrement remplacé.</summary>
    Reset,

    NodeAdded,
    NodeUpdated,
    NodeRemoved,
    EdgeAdded,
    EdgeUpdated,
    EdgeRemoved
}