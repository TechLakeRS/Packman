using System.ComponentModel;

namespace Packman.Models;

public enum DetectionRuleType
{
    File,
    Registry,
    MSI,
    Script
}

public class DetectionRule : INotifyPropertyChanged
{
    private DetectionRuleType _type;
    private string _path = "";
    private string _fileOrFolderName = "";
    private bool _checkVersion;
    private string _operator = "";
    private string _detectionType = "exists";
    private string _detectionValue = "";
    private bool _check32BitOn64System;
    private string _scriptContent = "";
    private bool _enforceSignatureCheck;
    private bool _runAs32Bit;

    public DetectionRuleType Type
    {
        get => _type;
        set { _type = value; OnPropertyChanged(nameof(Type)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Description)); }
    }

    public string Path
    {
        get => _path;
        set { _path = value ?? ""; OnPropertyChanged(nameof(Path)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Description)); }
    }

    public string FileOrFolderName
    {
        get => _fileOrFolderName;
        set { _fileOrFolderName = value ?? ""; OnPropertyChanged(nameof(FileOrFolderName)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Description)); }
    }

    public bool CheckVersion
    {
        get => _checkVersion;
        set { _checkVersion = value; OnPropertyChanged(nameof(CheckVersion)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Description)); }
    }

    public string Operator
    {
        get => _operator;
        set { _operator = value ?? ""; OnPropertyChanged(nameof(Operator)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Description)); }
    }

    /// <summary>
    /// Graph detectionType.
    /// File: exists, doesNotExist, version, sizeInMB, modifiedDate, createdDate.
    /// Registry: exists, doesNotExist, string, integer, version.
    /// </summary>
    public string DetectionType
    {
        get => _detectionType;
        set { _detectionType = value ?? "exists"; OnPropertyChanged(nameof(DetectionType)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Description)); }
    }

    /// <summary>Value compared against, per DetectionType.</summary>
    public string DetectionValue
    {
        get => _detectionValue;
        set { _detectionValue = value ?? ""; OnPropertyChanged(nameof(DetectionValue)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Description)); }
    }

    public bool Check32BitOn64System
    {
        get => _check32BitOn64System;
        set { _check32BitOn64System = value; OnPropertyChanged(nameof(Check32BitOn64System)); }
    }

    public string ScriptContent
    {
        get => _scriptContent;
        set { _scriptContent = value ?? ""; OnPropertyChanged(nameof(ScriptContent)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Description)); }
    }

    public bool EnforceSignatureCheck
    {
        get => _enforceSignatureCheck;
        set { _enforceSignatureCheck = value; OnPropertyChanged(nameof(EnforceSignatureCheck)); }
    }

    public bool RunAs32Bit
    {
        get => _runAs32Bit;
        set { _runAs32Bit = value; OnPropertyChanged(nameof(RunAs32Bit)); }
    }

    public string Title => Type switch
    {
        DetectionRuleType.File => GetFileTitleText(),
        DetectionRuleType.Registry => GetRegistryTitleText(),
        DetectionRuleType.MSI => CheckVersion
            ? $"MSI: {Path} (Version {GetOperatorDisplayText()} {FileOrFolderName})"
            : $"MSI: {Path}",
        DetectionRuleType.Script => "Custom Script Detection",
        _ => "Unknown Detection Rule"
    };

    public string Description => Type switch
    {
        DetectionRuleType.File => GetFileDescriptionText(),
        DetectionRuleType.Registry => GetRegistryDescriptionText(),
        DetectionRuleType.MSI => CheckVersion
            ? $"Detects MSI product '{Path}' with version {GetOperatorDisplayText()} {FileOrFolderName}"
            : $"Detects presence of MSI product '{Path}'",
        DetectionRuleType.Script => EnforceSignatureCheck
            ? "PowerShell detection script (signature check enforced)"
            : "PowerShell detection script",
        _ => "Unknown detection rule type"
    };

    private string GetFileTitleText()
    {
        var fullPath = System.IO.Path.Combine(Path, FileOrFolderName);
        return DetectionType switch
        {
            "exists" => $"File: {fullPath} (Exists)",
            "doesNotExist" => $"File: {fullPath} (Does not exist)",
            "version" => $"File: {fullPath} (Version {GetOperatorDisplayText()} {DetectionValue})",
            "sizeInMB" => $"File: {fullPath} (Size {GetOperatorDisplayText()} {DetectionValue} MB)",
            "modifiedDate" => $"File: {fullPath} (Modified date {GetOperatorDisplayText()} {DetectionValue})",
            "createdDate" => $"File: {fullPath} (Created date {GetOperatorDisplayText()} {DetectionValue})",
            _ => CheckVersion ? $"File: {fullPath} (Check Version)" : $"File: {fullPath}"
        };
    }

    private string GetRegistryTitleText()
    {
        var regPath = string.IsNullOrEmpty(FileOrFolderName) ? Path : $"{Path}\\{FileOrFolderName}";
        return DetectionType switch
        {
            "exists" => string.IsNullOrEmpty(FileOrFolderName) ? $"Registry: {Path} (Key exists)" : $"Registry: {regPath} (Value exists)",
            "doesNotExist" => string.IsNullOrEmpty(FileOrFolderName) ? $"Registry: {Path} (Key does not exist)" : $"Registry: {regPath} (Value does not exist)",
            "string" => $"Registry: {regPath} (String {GetOperatorDisplayText()} \"{DetectionValue}\")",
            "integer" => $"Registry: {regPath} (Integer {GetOperatorDisplayText()} {DetectionValue})",
            "version" => $"Registry: {regPath} (Version {GetOperatorDisplayText()} {DetectionValue})",
            _ => string.IsNullOrEmpty(FileOrFolderName) ? $"Registry: {Path} (Key Exists)" : $"Registry: {Path}\\{FileOrFolderName}"
        };
    }

    private string GetFileDescriptionText() => DetectionType switch
    {
        "exists" => $"Detects presence of '{FileOrFolderName}' in '{Path}'",
        "doesNotExist" => $"Detects absence of '{FileOrFolderName}' in '{Path}'",
        "version" => $"Checks file version of '{FileOrFolderName}' in '{Path}' ({GetOperatorDisplayText()} {DetectionValue})",
        "sizeInMB" => $"Checks file size of '{FileOrFolderName}' in '{Path}' ({GetOperatorDisplayText()} {DetectionValue} MB)",
        "modifiedDate" => $"Checks modified date of '{FileOrFolderName}' in '{Path}' ({GetOperatorDisplayText()} {DetectionValue})",
        "createdDate" => $"Checks created date of '{FileOrFolderName}' in '{Path}' ({GetOperatorDisplayText()} {DetectionValue})",
        _ => CheckVersion
            ? $"Detects file '{FileOrFolderName}' in path '{Path}' and validates its version"
            : $"Detects presence of file '{FileOrFolderName}' in path '{Path}'"
    };

    private string GetRegistryDescriptionText() => DetectionType switch
    {
        "exists" => string.IsNullOrEmpty(FileOrFolderName) ? $"Checks if registry key '{Path}' exists" : $"Checks if registry value '{FileOrFolderName}' exists in key '{Path}'",
        "doesNotExist" => string.IsNullOrEmpty(FileOrFolderName) ? $"Checks if registry key '{Path}' does not exist" : $"Checks if registry value '{FileOrFolderName}' does not exist in key '{Path}'",
        "string" => $"Compares string in registry value '{FileOrFolderName}' in key '{Path}' ({GetOperatorDisplayText()} \"{DetectionValue}\")",
        "integer" => $"Compares integer in registry value '{FileOrFolderName}' in key '{Path}' ({GetOperatorDisplayText()} {DetectionValue})",
        "version" => $"Compares version in registry value '{FileOrFolderName}' in key '{Path}' ({GetOperatorDisplayText()} {DetectionValue})",
        _ => string.IsNullOrEmpty(FileOrFolderName) ? $"Checks if registry key '{Path}' exists" : $"Checks registry value '{FileOrFolderName}' in key '{Path}'"
    };

    private string GetOperatorDisplayText() => Operator switch
    {
        "equal" => "=",
        "notEqual" => "!=",
        "greaterThan" => ">",
        "greaterThanOrEqual" => ">=",
        "lessThan" => "<",
        "lessThanOrEqual" => "<=",
        "Greater than or equal to" => ">=",
        "Equal to" => "=",
        "Greater than" => ">",
        "Less than" => "<",
        "Less than or equal to" => "<=",
        _ => Operator
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
