namespace Packman.ViewModels;

/// <summary>One row in the "Publishing…" progress overlay (validate / upload / create / assign).</summary>
public sealed class PublishStepViewModel : ObservableObject
{
    public int Number { get; }
    public string Title { get; }

    public PublishStepViewModel(int number, string title)
    {
        Number = number;
        Title = title;
    }

    private string _state = "pending"; // pending | working | done | error
    public string State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsWorking));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(IsError));
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    public bool IsPending => _state == "pending";
    public bool IsWorking => _state == "working";
    public bool IsDone => _state == "done";
    public bool IsError => _state == "error";

    public string StatusLabel => _state switch
    {
        "working" => "working…",
        "done" => "done",
        "error" => "failed",
        _ => "",
    };
}
