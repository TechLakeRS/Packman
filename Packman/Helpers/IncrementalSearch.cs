using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Packman.Helpers;

/// <summary>
/// A type-ahead search box: one query in flight wins, a slower older one is dropped, and
/// the results land in an ObservableCollection the view binds to.
/// </summary>
public sealed class IncrementalSearch<T>
{
    private readonly Func<string, Task<List<T>>> _search;
    private readonly Action? _changed;
    private int _seq;

    public ObservableCollection<T> Results { get; } = new();
    public bool HasResults => Results.Count > 0;

    /// <summary>Queries shorter than this clear the results instead of searching.</summary>
    public int MinimumLength { get; init; } = 2;

    /// <param name="search">Runs the query against the directory.</param>
    /// <param name="changed">Called after the results changed, for the owner's HasXxx notification.</param>
    public IncrementalSearch(Func<string, Task<List<T>>> search, Action? changed = null)
    {
        _search = search;
        _changed = changed;
    }

    /// <summary>Drops any pending query and empties the results.</summary>
    public void Clear()
    {
        _seq++;
        Results.Clear();
        _changed?.Invoke();
    }

    public async Task RunAsync(string query)
    {
        var seq = ++_seq;
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < MinimumLength)
        {
            Results.Clear();
            _changed?.Invoke();
            return;
        }

        List<T> results;
        try
        {
            results = await _search(query);
        }
        catch (Exception ex)
        {
            if (seq != _seq) return;
            Debug.WriteLine($"Search failed: {ex.Message}");
            results = new List<T>();
        }

        if (seq != _seq) return;   // a newer query owns the box
        Results.Clear();
        foreach (var r in results) Results.Add(r);
        _changed?.Invoke();
    }
}
