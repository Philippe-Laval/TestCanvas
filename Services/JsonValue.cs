using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestCanvas.Services;

/// <summary>
/// Conversion entre JSON et valeurs CLR simples, pour le dictionnaire
/// <c>Data</c> des nœuds et des arêtes.
/// <para>
/// Les types produits sont volontairement restreints : <see cref="string"/>,
/// <see cref="long"/>, <see cref="double"/>, <see cref="bool"/>, <see langword="null"/>,
/// <c>List&lt;object?&gt;</c> et <c>Dictionary&lt;string, object?&gt;</c>. Ce sont les
/// seuls types que l'éditeur de données sait réafficher, et ils survivent au
/// voyage JSON aller-retour sans conversion ni perte.
/// </para>
/// </summary>
public static class JsonValue
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Lit une valeur JSON et la convertit en valeur CLR simple.</summary>
    public static object? ReadValue(ref Utf8JsonReader reader) => reader.TokenType switch
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

    /// <summary>Convertit une valeur CLR en JSON compact.</summary>
    public static string ToJson(object? value)
        => JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object), Options);

    /// <summary>Interprète un fragment JSON comme une valeur de dictionnaire.</summary>
    /// <exception cref="FormatException">Le fragment n'est pas un JSON valide.</exception>
    public static object? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new FormatException("Le fragment JSON est vide.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            // La valeur retournée est intégralement matérialisée (aucun JsonElement
            // ne survit), le document peut donc être libéré aussitôt.
            return FromElement(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
    }

    /// <summary>Variante non levante de <see cref="Parse"/>.</summary>
    public static bool TryParse(string? json, out object? value)
    {
        value = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            value = Parse(json);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static object? FromElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                return element.TryGetInt64(out var l) ? l : element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
                return null;

            case JsonValueKind.Array:
                return element.EnumerateArray().Select(FromElement).ToList();

            case JsonValueKind.Object:
                return FromObject(element);

            default:
                return element.GetRawText();
        }
    }

    private static Dictionary<string, object?> FromObject(JsonElement element)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            map[property.Name] = FromElement(property.Value);
        }

        return map;
    }

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

/// <summary>Convertit un <see cref="JsonElement"/> en valeur CLR simple pour le dictionnaire <c>Data</c>.</summary>
internal sealed class JsonElementConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonValue.ReadValue(ref reader);

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, value.GetType(), options);
}