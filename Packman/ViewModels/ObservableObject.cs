using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Packman.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    /// <summary>Sets the field and, when it changed, also raises the computed properties that read it.</summary>
    protected bool Set<T>(ref T field, T value, string[] dependents, [CallerMemberName] string? name = null)
    {
        if (!Set(ref field, value, name)) return false;
        RaiseAll(dependents);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void RaiseAll(params string[] names)
    {
        foreach (var name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
