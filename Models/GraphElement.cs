using System.Text.Json.Serialization;

namespace TestCanvas.Models;

/// <summary>
/// Base commune aux objets du graphe porteurs de données métier libres.
/// <para>
/// Le dictionnaire est traité en copie sur écriture : <see cref="Dictionary{TKey,TValue}"/>
/// est un type référence, et le muter en place ne déclencherait ni
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> ni l'envoi incrémental vers le
/// canvas. Chaque méthode de mutation reconstruit donc le dictionnaire puis notifie.
/// </para>
/// <para>
/// L'ordre des entrées est mémorisé à part (<c>Dictionary</c> ne garantit pas d'ordre
/// d'énumération) afin que l'éditeur affiche les clés dans leur ordre d'insertion,
/// de façon stable même après une suppression.
/// </para>
/// </summary>
public abstract class GraphElement : ObservableObject
{
    private Dictionary<string, object?>? _data;
    private List<string>? _dataOrder;

    /// <summary>
    /// Données métier libres, sérialisées avec le graphe.
    /// <para>
    /// La lecture directe reste possible (<c>node.Data["clé"]</c>), mais elle ne
    /// notifie pas : passez par <see cref="SetDataValue"/>, <see cref="RemoveDataValue"/>,
    /// <see cref="RenameDataKey"/> ou <see cref="ReplaceData"/> pour que la modification
    /// soit reflétée sur le canvas et dans l'interface.
    /// </para>
    /// </summary>
    public Dictionary<string, object?> Data
    {
        get => _data ??= [];
        set
        {
            var copy = value is null
                ? []
                : new Dictionary<string, object?>(value, StringComparer.Ordinal);

            _data = copy;
            _dataOrder = [.. copy.Keys];
            NotifyDataChanged();
        }
    }

    /// <summary>Nombre d'entrées du dictionnaire.</summary>
    [JsonIgnore]
    public int DataCount => _data?.Count ?? 0;

    /// <summary>Entrées dans l'ordre d'insertion, prêtes pour l'affichage.</summary>
    [JsonIgnore]
    public IReadOnlyList<KeyValuePair<string, object?>> DataEntries
    {
        get
        {
            if (_data is null || _data.Count == 0)
            {
                return [];
            }

            // Une écriture directe dans le dictionnaire peut désaligner l'ordre
            // mémorisé : on le reconstruit plutôt que de renvoyer des paires fausses.
            if (_dataOrder is null || !_dataOrder.SequenceEqual(_data.Keys))
            {
                _dataOrder = [.. _data.Keys];
            }

            return [.. _dataOrder.Select(key => new KeyValuePair<string, object?>(key, _data[key]))];
        }
    }

    /// <summary>Teste la présence d'une clé.</summary>
    public bool HasData(string? key)
        => key is not null && _data is not null && _data.ContainsKey(key);

    /// <summary>Lecture d'une entrée, ou <see langword="null"/> si la clé est absente.</summary>
    public object? GetDataValue(string key)
        => _data is not null && _data.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Ajoute ou remplace une entrée. Une clé déjà présente conserve sa position.
    /// </summary>
    public void SetDataValue(string key, object? value)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var entries = DataEntries.ToList();
        var index = entries.FindIndex(e => string.Equals(e.Key, key, StringComparison.Ordinal));

        if (index >= 0)
        {
            entries[index] = new KeyValuePair<string, object?>(key, value);
        }
        else
        {
            entries.Add(new KeyValuePair<string, object?>(key, value));
        }

        Commit(entries);
    }

    /// <summary>Supprime une entrée. Ne fait rien si la clé est absente.</summary>
    public void RemoveDataValue(string key)
    {
        var entries = DataEntries
            .Where(e => !string.Equals(e.Key, key, StringComparison.Ordinal))
            .ToList();

        if (entries.Count == DataCount)
        {
            return;
        }

        Commit(entries);
    }

    /// <summary>
    /// Renomme une clé en conservant sa position. Refusé si la nouvelle clé est vide
    /// ou si elle entre en collision avec une entrée existante.
    /// </summary>
    /// <returns><see langword="true"/> si le renommage a eu lieu.</returns>
    public bool RenameDataKey(string oldKey, string newKey)
    {
        if (string.IsNullOrEmpty(newKey) || string.Equals(oldKey, newKey, StringComparison.Ordinal))
        {
            return false;
        }

        var entries = DataEntries.ToList();
        var index = entries.FindIndex(e => string.Equals(e.Key, oldKey, StringComparison.Ordinal));

        if (index < 0 || entries.Any(e => string.Equals(e.Key, newKey, StringComparison.Ordinal)))
        {
            return false;
        }

        entries[index] = new KeyValuePair<string, object?>(newKey, entries[index].Value);
        Commit(entries);
        return true;
    }

    /// <summary>Remplace l'ensemble du contenu en une seule notification.</summary>
    public void ReplaceData(IEnumerable<KeyValuePair<string, object?>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var ordered = new List<KeyValuePair<string, object?>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Key) || !seen.Add(entry.Key))
            {
                continue;
            }

            ordered.Add(entry);
        }

        Commit(ordered);
    }

    /// <summary>Vide le dictionnaire en une seule notification.</summary>
    public void ClearData()
    {
        if (DataCount == 0)
        {
            return;
        }

        Commit([]);
    }

    // ------------------------------------------------------------------ interne

    private void Commit(List<KeyValuePair<string, object?>> entries)
    {
        var data = new Dictionary<string, object?>(entries.Count, StringComparer.Ordinal);
        var order = new List<string>(entries.Count);

        foreach (var entry in entries)
        {
            data[entry.Key] = entry.Value;
            order.Add(entry.Key);
        }

        _data = data;
        _dataOrder = order;
        NotifyDataChanged();
    }

    private void NotifyDataChanged()
    {
        OnPropertyChanged(nameof(Data));
        OnPropertyChanged(nameof(DataCount));
        OnPropertyChanged(nameof(DataEntries));
    }
}