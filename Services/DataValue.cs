using System.Globalization;

namespace TestCanvas.Services;

/// <summary>Nature d'une valeur du dictionnaire <c>Data</c>, telle que présentée à l'utilisateur.</summary>
public enum DataValueKind
{
    /// <summary>Chaîne de caractères.</summary>
    Text,

    /// <summary>Nombre entier ou décimal (<see cref="long"/> ou <see cref="double"/>).</summary>
    Number,

    /// <summary>Booléen, édité par une case à cocher.</summary>
    Boolean,

    /// <summary>Aucune valeur (<see langword="null"/>).</summary>
    Null,

    /// <summary>Tableau ou objet, édité sous forme de fragment JSON.</summary>
    Json
}

/// <summary>
/// Adaptateur entre les valeurs CLR du dictionnaire <c>Data</c> et ce que l'éditeur
/// sait afficher : un texte, un nombre, un booléen, du JSON, ou rien.
/// </summary>
public static class DataValue
{
    /// <summary>Types proposés dans la liste déroulante de l'éditeur.</summary>
    public static IReadOnlyList<DataValueKind> Kinds { get; } =
        [DataValueKind.Text, DataValueKind.Number, DataValueKind.Boolean, DataValueKind.Null, DataValueKind.Json];

    /// <summary>Détermine le mode d'édition naturel d'une valeur existante.</summary>
    public static DataValueKind Detect(object? value) => value switch
    {
        null => DataValueKind.Null,
        bool => DataValueKind.Boolean,
        string => DataValueKind.Text,
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            => DataValueKind.Number,
        _ => DataValueKind.Json
    };

    /// <summary>Libellé lisible d'un mode d'édition.</summary>
    public static string Label(DataValueKind kind) => kind switch
    {
        DataValueKind.Text => "Texte",
        DataValueKind.Number => "Nombre",
        DataValueKind.Boolean => "Booléen",
        DataValueKind.Null => "Nul",
        _ => "JSON"
    };

    /// <summary>
    /// Convertit une valeur en texte saisissable. Le format est invariant : le HTML
    /// et le champ de saisie attendent un point décimal quelle que soit la culture.
    /// </summary>
    public static string Format(object? value, DataValueKind? kind = null)
    {
        var effective = kind ?? Detect(value);

        return effective switch
        {
            DataValueKind.Null => string.Empty,
            DataValueKind.Boolean => value is true ? "true" : value is null ? string.Empty : "false",
            DataValueKind.Number => FormatNumber(value),
            DataValueKind.Text => value as string ?? JsonValue.ToJson(value),
            _ => value is null ? string.Empty : JsonValue.ToJson(value)
        };
    }

    /// <summary>
    /// Interprète un texte selon un mode d'édition.
    /// </summary>
    /// <param name="error">Message expliquant le refus, en français.</param>
    public static bool TryParse(string? text, DataValueKind kind, out object? value, out string? error)
    {
        value = null;
        error = null;

        switch (kind)
        {
            case DataValueKind.Null:
                value = null;
                return true;

            case DataValueKind.Text:
                value = text ?? string.Empty;
                return true;

            case DataValueKind.Boolean:
                if (TryParseBoolean(text, out var flag))
                {
                    value = flag;
                    return true;
                }

                error = "Valeur booléenne attendue (true/false).";
                return false;

            case DataValueKind.Number:
                if (TryParseNumber(text, out var number))
                {
                    value = number;
                    return true;
                }

                error = "Nombre attendu.";
                return false;

            default:
                if (JsonValue.TryParse(text, out var json))
                {
                    value = json;
                    return true;
                }

                error = "JSON invalide (tableau ou objet attendu).";
                return false;
        }
    }

    /// <summary>
    /// Analyse un nombre en privilégiant l'entier long : c'est le type que produit
    /// déjà l'import JSON, ce qui évite qu'une saisie « 3 » devienne « 3.0 » au
    /// premier aller-retour de sérialisation.
    /// </summary>
    public static bool TryParseNumber(string? text, out object? value)
    {
        value = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        // Un séparateur décimal ou un exposant impose un flottant : sans cela,
        // « 1.5 » serait refusé et « 3 » deviendrait 3.0.
        if (trimmed.Contains('.') || trimmed.Contains(',') || trimmed.Contains('e') || trimmed.Contains('E'))
        {
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
                || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out real))
            {
                value = real;
                return true;
            }

            return false;
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
            || long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.CurrentCulture, out integer))
        {
            value = integer;
            return true;
        }

        return false;
    }

    /// <summary>Accepte les écritures booléennes courantes, y compris en français.</summary>
    public static bool TryParseBoolean(string? text, out bool value)
    {
        value = false;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "oui" or "yes" or "y":
                value = true;
                return true;
            case "false" or "0" or "non" or "no" or "n":
                value = false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Aperçu d'une valeur pour l'historique et le journal : toujours sur une ligne,
    /// et abrégé pour ne pas_noyer le panneau latéral.
    /// </summary>
    public static string Describe(object? value, int maxLength = 40)
    {
        string text;

        try
        {
            text = JsonValue.ToJson(value);
        }
        catch (NotSupportedException)
        {
            text = value?.ToString() ?? string.Empty;
        }

        text = text.Replace('\r', ' ').Replace('\n', ' ');

        return text.Length <= maxLength ? text : $"{text[..maxLength]}…";
    }

    private static string FormatNumber(object? value) => value switch
    {
        null => string.Empty,
        double real => real.ToString("R", CultureInfo.InvariantCulture),
        float single => single.ToString("R", CultureInfo.InvariantCulture),
        decimal money => money.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}