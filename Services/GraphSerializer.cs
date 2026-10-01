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

/// <summary>Convertit un <see cref="JsonElement"/> en valeur CLR simple pour le dictionnaire <c>Data</c>.</summary>
internal sealed class JsonElementConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadValue(ref reader);

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, value.GetType(), options);

    private static object? ReadValue(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => reader.TryGetInt64(out var l) ? l : reader.GetDouble(),
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Null => null,
        JsonTokenType.StartArray => ReadArray(ref reader),
        JsonTokenType.StartObject => ReadObject(ref reader),
        _ => reader.GetString()
    };

    private static List<object?> ReadArray(ref Utf8JsonReader reader)
    {
        var items = new List<object?>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            items.Add(ReadValue(ref reader));
        }

        return items;
    }

    private static Dictionary<string, object?> ReadObject(ref Utf8JsonReader reader)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString() ?? string.Empty;
            reader.Read();
            map[key] = ReadValue(ref reader);
        }

        return map;
    }
}