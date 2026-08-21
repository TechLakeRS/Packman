using Packman.Models;

namespace Packman.ViewModels;

/// <summary>One editable return-code row (exit code + outcome) used by Settings and the upload step.</summary>
public sealed class ReturnCodeRow : ObservableObject
{
    private string _code;
    public string Code { get => _code; set => Set(ref _code, value); }

    private string _description;
    /// <summary>Note kept alongside the code; not sent to Intune.</summary>
    public string Description { get => _description; set => Set(ref _description, value); }

    private ReturnCodeType _type;
    public ReturnCodeType Type
    {
        get => _type;
        set
        {
            if (Set(ref _type, value))
            {
                OnPropertyChanged(nameof(IsSuccess));
                OnPropertyChanged(nameof(IsSoftReboot));
                OnPropertyChanged(nameof(IsHardReboot));
                OnPropertyChanged(nameof(IsRetry));
                OnPropertyChanged(nameof(IsFailed));
            }
        }
    }

    public bool IsSuccess { get => _type == ReturnCodeType.Success; set { if (value) Type = ReturnCodeType.Success; } }
    public bool IsSoftReboot { get => _type == ReturnCodeType.SoftReboot; set { if (value) Type = ReturnCodeType.SoftReboot; } }
    public bool IsHardReboot { get => _type == ReturnCodeType.HardReboot; set { if (value) Type = ReturnCodeType.HardReboot; } }
    public bool IsRetry { get => _type == ReturnCodeType.Retry; set { if (value) Type = ReturnCodeType.Retry; } }
    public bool IsFailed { get => _type == ReturnCodeType.Failed; set { if (value) Type = ReturnCodeType.Failed; } }

    public RelayCommand RemoveCommand { get; }

    public ReturnCodeRow(int code, ReturnCodeType type, string description, Action<ReturnCodeRow> remove)
    {
        _code = code.ToString();
        _type = type;
        _description = description;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public ReturnCodeInfo? ToInfo() =>
        int.TryParse(Code, out var value)
            ? new ReturnCodeInfo { Code = value, Type = Type, Description = Description.Trim() }
            : null;
}
