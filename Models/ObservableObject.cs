using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TestCanvas.Models;

/// <summary>
/// Base class for every graph object that must notify (and therefore be
/// re-sent to the JavaScript canvas) as soon as one of its properties changes.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Assigns <paramref name="value"/> to <paramref name="field"/> and raises a change notification.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}