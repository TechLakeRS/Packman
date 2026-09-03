using System.Text;

namespace Packman.ViewModels;

/// <summary>An open file with a live Monaco buffer, shown as a tab.</summary>
public sealed class OpenFile : ObservableObject
{
    private bool _isDirty;
    private bool _isActive;

    public OpenFile(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
    }

    public string Path { get; }
    public string Name { get; }
    public Encoding Encoding { get; set; } = new UTF8Encoding(false);
    public string EncodingLabel { get; set; } = "UTF-8";
    public bool Crlf { get; set; } = true;
    public bool IsPowerShell { get; set; }
    public bool IsReadOnly { get; set; }
    public bool ChangedOnDisk { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public int ErrorCount { get; set; }

    public bool IsDirty { get => _isDirty; set => Set(ref _isDirty, value); }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}
