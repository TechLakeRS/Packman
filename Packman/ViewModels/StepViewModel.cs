namespace Packman.ViewModels;

public sealed class StepViewModel : ObservableObject
{
    public int Index { get; }
    public string Title { get; }
    public string Number => (Index + 1).ToString();

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { if (Set(ref _isCurrent, value)) OnPropertyChanged(nameof(IsActive)); }
    }

    private bool _isDone;
    public bool IsDone
    {
        get => _isDone;
        set { if (Set(ref _isDone, value)) OnPropertyChanged(nameof(IsActive)); }
    }

    public bool IsActive => IsCurrent || IsDone;

    public StepViewModel(int index, string title)
    {
        Index = index;
        Title = title;
    }
}
