using System.Text.Json;
using System.Text.Json.Serialization;
using TestCanvas.Models;

namespace TestCanvas.Services;

/// <summary>Sérialise un document en JSON (et inversement) sans passer par l'interopérabilité JS.</summary>
public static class GraphSerializer
{
    private static readonly JsonSerializerOptions WriteIndented = Create(writeIndented: true);
    private static readonly JsonSerializerOptions WriteCompact = Create(writeIndented: false);

    private static JsonSerializerOptions Create(bool writeIndented) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = writeIndented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(), new JsonElementConverter() }
    };

    public static string Serialize(GraphDocument document, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(document);

        var payload = new { nodes = document.Nodes, edges = document.Edges };
        return JsonSerializer.Serialize(payload, indented ? WriteIndented : WriteCompact);
    }

    public static GraphDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new FormatException("Le JSON est vide.");
        }

        GraphFile? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GraphFile>(json, WriteIndented);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"JSON invalide : {ex.Message}", ex);
        }

        if (payload?.Nodes is null)
        {
            throw new FormatException("JSON invalide : le champ « nodes » est absent.");
        }

        var document = new GraphDocument();
        document.Reset(payload.Nodes, payload.Edges ?? []);
        return document;
    }

    private sealed class GraphFile
    {
        public List<GraphNode>? Nodes { get; set; }

        public List<GraphEdge>? Edges { get; set; }
    }
}