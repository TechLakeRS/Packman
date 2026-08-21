using Microsoft.Web.WebView2.Core;
using Packman.Helpers;
using Packman.Services;
using Packman.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace Packman.Views;

public partial class StepEdit : UserControl
{
    private static readonly IReadOnlySet<string> TextExtensions = PackageFileSearch.TextExtensions;
    private static readonly IReadOnlySet<string> PowerShellExtensions = PackageFileSearch.PowerShellExtensions;

    /// <summary>Loaded once per process; the CSV doesn't change at runtime.</summary>
    private static List<PSADTFunction>? _catalog;

    private readonly ObservableCollection<OpenFile> _openFiles = new();
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private bool _editorReady;
    private bool _editorFailed;
    private OpenFile? _active;
    private string? _pendingOpenPath;   // file waiting for the editor to become ready
    private string? _loadedPackagePath; // package the tree currently shows
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _searchCts;
    private bool _suppressTreeSelection;

    public StepEdit()
    {
        InitializeComponent();
        FileTabs.ItemsSource = _openFiles;
        _watchTimer.Tick += (_, _) => { _watchTimer.Stop(); ErrorReporter.FireAndForget(OnPackageChangedOnDiskAsync); };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ErrorReporter.FireAndForget(() => RunSearchAsync(SearchBox.Text)); };
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    private string ApplicationFolder =>
        Path.Combine(VM?.CreatePackage.CurrentPackagePath ?? "", "Application");

    /// <summary>True while any open file has unsaved edits.</summary>
    public bool HasUnsavedChanges => _openFiles.Any(f => f.IsDirty);

    // ═══════════ Lifetime ═══════════

    private void StepEdit_Loaded(object sender, RoutedEventArgs e)
    {
        // Warm WebView2 up front, or the step stalls the first time it is shown.
        ErrorReporter.FireAndForget(InitializeEditorAsync);
    }

    private void StepEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) return;
        ErrorReporter.FireAndForget(async () =>
        {
            await InitializeEditorAsync();
            await LoadPackageAsync();
        });
    }

    // ═══════════ WebView2 / Monaco host ═══════════

    private async Task InitializeEditorAsync()
    {
        if (_editorReady || _editorFailed || EditorWebView.CoreWebView2 != null) return;

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packman", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await EditorWebView.EnsureCoreWebView2Async(env);

            var core = EditorWebView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 initialised without a CoreWebView2.");

            var assetsFolder = Path.Combine(AppContext.BaseDirectory, "MonacoEditor");
            core.SetVirtualHostNameToFolderMapping(
                "packman-editor", assetsFolder, CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.WebMessageReceived += Editor_WebMessageReceived;
            ApplyEditorTheme();
            core.Navigate("https://packman-editor/index.html");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView2 init failed: {ex.Message}");
            _editorFailed = true;
            EditorWebView.Visibility = Visibility.Collapsed;
            EditorFallbackText.Visibility = Visibility.Visible;
        }
    }

    private void Editor_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProperty)) return;

        switch (typeProperty.GetString())
        {
            case "ready":
                _editorReady = true;
                PostToEditor(new { type = "init", catalog = BuildCatalogPayload(), background = CodeBackgroundHex() });
                if (_pendingOpenPath != null)
                {
                    var path = _pendingOpenPath;
                    _pendingOpenPath = null;
                    OpenFileInEditor(path);
                }
                break;

            case "dirty":
                var dirtyPath = root.GetProperty("path").GetString();
                var isDirty = root.GetProperty("dirty").GetBoolean();
                var file = _openFiles.FirstOrDefault(f => f.Path == dirtyPath);
                if (file != null)
                {
                    file.IsDirty = isDirty;
                    if (file == _active) UpdateActionState();
                }
                break;

            case "save":
                if (_active != null)
                {
                    var toSave = _active;
                    ErrorReporter.FireAndForget(() => SaveAsync(toSave));
                }
                break;

            case "validate":
                ErrorReporter.FireAndForget(() => ValidateAsync(
                    root.GetProperty("path").GetString(),
                    root.GetProperty("content").GetString()));
                break;

            case "cursor":
                var selected = root.GetProperty("selected").GetInt32();
                StatusPosition.Text = $"Ln {root.GetProperty("line").GetInt32()}, Col {root.GetProperty("column").GetInt32()}";
                StatusSelection.Text = selected > 0 ? $"{selected} selected" : "";
                break;
        }
    }

    private void PostToEditor(object message) =>
        EditorWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message));

    /// <summary>Reads a buffer out of Monaco; null when it no longer holds one.</summary>
    private async Task<string?> GetBufferAsync(string path)
    {
        if (EditorWebView.CoreWebView2 == null) return null;
        var json = await EditorWebView.CoreWebView2.ExecuteScriptAsync(
            $"window.packmanContent({JsonSerializer.Serialize(path)})");
        return json is null or "null" ? null : JsonSerializer.Deserialize<string>(json);
    }

    private string CodeBackgroundHex()
    {
        var color = (TryFindResource("CodeBgBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x07, 0x08, 0x0B);
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>Keeps Monaco's canvas on the card colour.</summary>
    private void ApplyEditorTheme()
    {
        var color = (TryFindResource("CodeBgBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x07, 0x08, 0x0B);
        EditorWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
        if (_editorReady) PostToEditor(new { type = "theme", background = CodeBackgroundHex() });
    }

    /// <summary>
    /// Parses a buffer and sends syntax errors back as Monaco markers. Off the UI thread:
    /// it fires on every keystroke pause and a large script is enough to feel.
    /// </summary>
    private async Task ValidateAsync(string? path, string? content)
    {
        if (path is null || content is null) return;

        var errors = await Task.Run(() => PowerShellSyntaxValidator.Validate(content));
        PostToEditor(new
        {
            type = "markers",
            path,
            markers = errors.Select(e => new
            {
                line = e.Line,
                column = e.Column,
                endLine = e.EndLine,
                endColumn = e.EndColumn,
                message = e.Message
            })
        });

        var file = _openFiles.FirstOrDefault(f => f.Path == path);
        if (file == null) return;
        file.ErrorCount = errors.Count;
        if (file == _active) UpdateStatusBar();
    }

    private static object BuildCatalogPayload()
    {
        _catalog ??= PSADTFunctionCatalog.LoadFromCsv(PSADTFunctionCatalog.GetCsvPath());
        return _catalog.Select(f => new
        {
            name = f.Name,
            synopsis = f.Synopsis,
            category = f.Category,
            snippet = f.GenerateCallWithPlaceholders(),
            @params = f.Parameters
                .Where(p => p.Name != "(none)")
                .Select(p => new
                {
                    name = p.Name,
                    type = p.Type,
                    mandatory = p.Mandatory,
                    isSwitch = p.IsSwitch,
                    description = p.Description
                })
        }).ToList();
    }

    // ═══════════ Package tree ═══════════

    private async Task LoadPackageAsync()
    {
        var packagePath = VM?.CreatePackage.CurrentPackagePath;
        var appFolder = ApplicationFolder;

        if (string.IsNullOrEmpty(packagePath) || !Directory.Exists(appFolder))
        {
            EmptyState.Visibility = Visibility.Visible;
            EditorGrid.Visibility = Visibility.Collapsed;
            StopWatching();
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        EditorGrid.Visibility = Visibility.Visible;
        PackageNameText.Text = new DirectoryInfo(packagePath).Name;

        var isNewPackage = packagePath != _loadedPackagePath;
        if (isNewPackage)
        {
            CloseAllFiles();
            _loadedPackagePath = packagePath;
            StartWatching(appFolder);
        }

        await RefreshTreeAsync();

        if (isNewPackage)
        {
            // Open the main deployment script by default.
            var script = Path.Combine(appFolder, "Invoke-AppDeployToolkit.ps1");
            if (File.Exists(script)) OpenFileInEditor(script);
        }
    }

    private async Task RefreshTreeAsync()
    {
        var appFolder = ApplicationFolder;
        if (!Directory.Exists(appFolder)) return;

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CaptureExpanded(FileTree.Items, expanded);

        var roots = await Task.Run(() => ScanFolder(appFolder));

        _suppressTreeSelection = true;
        FileTree.Items.Clear();
        foreach (var node in roots)
            FileTree.Items.Add(BuildTreeItem(node, expanded));
        _suppressTreeSelection = false;

        if (_active != null) SelectInTree(_active.Path);
    }

    private sealed record ScanNode(string Path, string Name, bool IsDirectory, List<ScanNode> Children);

    private static List<ScanNode> ScanFolder(string folder)
    {
        var nodes = new List<ScanNode>();
        try
        {
            foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => d))
                nodes.Add(new ScanNode(dir, Path.GetFileName(dir), true, ScanFolder(dir)));
            foreach (var f in Directory.GetFiles(folder).OrderBy(f => f))
                if (!f.EndsWith(TextFileIO.TempSuffix, StringComparison.OrdinalIgnoreCase))
                    nodes.Add(new ScanNode(f, Path.GetFileName(f), false, new List<ScanNode>()));
        }
        catch (UnauthorizedAccessException) { }
        return nodes;
    }

    private TreeViewItem BuildTreeItem(ScanNode node, HashSet<string> expanded)
    {
        var item = new TreeViewItem
        {
            Header = BuildNodeHeader(node),
            Tag = node.Path,
            IsExpanded = node.IsDirectory && expanded.Contains(node.Path)
        };
        foreach (var child in node.Children)
            item.Items.Add(BuildTreeItem(child, expanded));
        return item;
    }

    private StackPanel BuildNodeHeader(ScanNode node)
    {
        var iconKey = node.IsDirectory
            ? "IconFolder"
            : PowerShellExtensions.Contains(Path.GetExtension(node.Path)) ? "IconCode" : "IconFile";

        var icon = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource(iconKey),
            StrokeThickness = 1.8,
            Fill = Brushes.Transparent,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,
            node.IsDirectory ? "MutedBrush" : "InkBrush");

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Viewbox { Width = 13, Height = 13, Child = icon });
        panel.Children.Add(new TextBlock { Text = node.Name, Margin = new Thickness(6, 0, 0, 0) });
        return panel;
    }

    private static void CaptureExpanded(ItemCollection items, HashSet<string> expanded)
    {
        foreach (TreeViewItem item in items)
        {
            if (item.IsExpanded && item.Tag is string path) expanded.Add(path);
            CaptureExpanded(item.Items, expanded);
        }
    }

    private void ToggleTree_Click(object sender, RoutedEventArgs e)
    {
        var show = TreePanel.Visibility != Visibility.Visible;
        TreePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TreeRail.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshTree_Click(object sender, RoutedEventArgs e) => ErrorReporter.FireAndForget(RefreshTreeAsync);

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressTreeSelection) return;
        if (e.NewValue is TreeViewItem { Tag: string path } && File.Exists(path))
            OpenFileInEditor(path);
    }

    // ═══════════ Watching the package folder ═══════════

    private void StartWatching(string folder)
    {
        StopWatching();
        try
        {
            _watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnPackageFolderEvent;
            _watcher.Created += OnPackageFolderEvent;
            _watcher.Deleted += OnPackageFolderEvent;
            _watcher.Renamed += OnPackageFolderEvent;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not watch package folder: {ex.Message}");
        }
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _watchTimer.Stop();
    }

    private void OnPackageFolderEvent(object sender, FileSystemEventArgs e) =>
        Dispatcher.InvokeAsync(() => { _watchTimer.Stop(); _watchTimer.Start(); });

    /// <summary>
    /// The package folder changed: refresh the tree and pull external edits into open
    /// files. Files with unsaved edits are flagged and left alone.
    /// </summary>
    private async Task OnPackageChangedOnDiskAsync()
    {
        await RefreshTreeAsync();

        foreach (var file in _openFiles.ToList())
        {
            if (!File.Exists(file.Path)) continue;
            if (File.GetLastWriteTimeUtc(file.Path) == file.LastWriteUtc) continue;

            if (file.IsDirty)
            {
                file.ChangedOnDisk = true;
                if (file == _active) DiskChangedButton.Visibility = Visibility.Visible;
            }
            else
            {
                ReadIntoEditor(file);
            }
        }
    }

    // ═══════════ File open / save ═══════════

    private void OpenFileInEditor(string path)
    {
        if (!_editorReady)
        {
            _pendingOpenPath = path;
            return;
        }

        var existing = _openFiles.FirstOrDefault(f => f.Path == path);
        if (existing != null)
        {
            Activate(existing);
            return;
        }

        var file = new OpenFile(path);
        _openFiles.Add(file);
        ReadIntoEditor(file);
        Activate(file);
    }

    /// <summary>Reads the file and hands the text to Monaco.</summary>
    private void ReadIntoEditor(OpenFile file)
    {
        var ext = Path.GetExtension(file.Path);
        string content;

        if (!TextExtensions.Contains(ext))
        {
            content = $"[Binary file — open in an external editor to view]\n\n{file.Path}";
            file.IsReadOnly = true;
            file.EncodingLabel = "binary";
        }
        else
        {
            try
            {
                var text = TextFileIO.Read(file.Path);
                content = text.Content;
                file.Encoding = text.Encoding;
                file.Crlf = text.Crlf;
                file.EncodingLabel = EncodingLabel(text.Encoding);
                file.IsReadOnly = false;
            }
            catch (Exception ex)
            {
                content = $"Could not read file: {ex.Message}";
                file.IsReadOnly = true;
                file.EncodingLabel = "—";
            }
        }

        file.LastWriteUtc = File.Exists(file.Path) ? File.GetLastWriteTimeUtc(file.Path) : DateTime.MinValue;
        file.ChangedOnDisk = false;
        file.IsPowerShell = PowerShellExtensions.Contains(ext);

        PostToEditor(new
        {
            type = "open",
            path = file.Path,
            content,
            language = file.IsPowerShell ? "powershell" : "plaintext",
            readOnly = file.IsReadOnly,
            eol = file.Crlf ? "crlf" : "lf",
            activate = file == _active
        });

        if (file == _active) UpdateStatusBar();
    }

    private void Activate(OpenFile file)
    {
        foreach (var f in _openFiles) f.IsActive = ReferenceEquals(f, file);
        _active = file;
        PostToEditor(new { type = "activate", path = file.Path });
        UpdateStatusBar();
        UpdateActionState();
        SelectInTree(file.Path);
    }

    private void SelectInTree(string path)
    {
        _suppressTreeSelection = true;
        var item = FindTreeItem(FileTree.Items, path);
        if (item != null) item.IsSelected = true;
        _suppressTreeSelection = false;
    }

    private static TreeViewItem? FindTreeItem(ItemCollection items, string path)
    {
        foreach (TreeViewItem item in items)
        {
            if (item.Tag as string == path) return item;
            var hit = FindTreeItem(item.Items, path);
            if (hit != null) return hit;
        }
        return null;
    }

    private void UpdateStatusBar()
    {
        StatusEol.Text = _active is null ? "" : _active.Crlf ? "CRLF" : "LF";
        StatusEncoding.Text = _active?.EncodingLabel ?? "";
        StatusLanguage.Text = _active is null ? "" : _active.IsPowerShell ? "PowerShell" : "Plain Text";

        var errors = _active?.IsPowerShell == true ? _active.ErrorCount : 0;
        StatusProblems.Text = _active?.IsPowerShell != true ? ""
            : errors == 0 ? "No problems"
            : errors == 1 ? "1 problem"
            : $"{errors} problems";
        // Clearing hands the colour back to the DynamicResource on a theme change.
        if (errors > 0)
            StatusProblems.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x7A, 0x7A));
        else
            StatusProblems.ClearValue(TextBlock.ForegroundProperty);
    }

    private void UpdateActionState()
    {
        var dirty = _active?.IsDirty == true;
        SaveButton.IsEnabled = dirty && _active?.IsReadOnly == false;
        RevertButton.IsEnabled = dirty;
        ReloadButton.IsEnabled = _active != null;
        DiskChangedButton.Visibility = _active?.ChangedOnDisk == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string EncodingLabel(Encoding encoding) => encoding switch
    {
        UTF8Encoding utf8 => utf8.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8",
        UnicodeEncoding { CodePage: 1201 } => "UTF-16 BE",
        UnicodeEncoding => "UTF-16 LE",
        _ => encoding.WebName.ToUpperInvariant()
    };

    /// <summary>Writes the buffer back in the file's original encoding.</summary>
    private async Task<bool> SaveAsync(OpenFile file)
    {
        if (file.IsReadOnly) return true;

        var content = await GetBufferAsync(file.Path);
        if (content == null)
        {
            MessageBox.Show($"Could not read the editor buffer for {file.Name}.", "Save failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (File.Exists(file.Path) && File.GetLastWriteTimeUtc(file.Path) != file.LastWriteUtc)
        {
            var overwrite = MessageBox.Show(
                $"{file.Name} has changed on disk since it was opened.\n\nOverwrite it with your version?",
                "File changed on disk", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!overwrite) return false;
        }

        try
        {
            TextFileIO.Write(file.Path, content, file.Encoding);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save file: {ex.Message}", "Save failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        file.LastWriteUtc = File.GetLastWriteTimeUtc(file.Path);
        file.ChangedOnDisk = false;
        PostToEditor(new { type = "markSaved", path = file.Path });
        if (file == _active) UpdateActionState();
        return true;
    }

    /// <summary>Offers to save every dirty file. False means the user cancelled.</summary>
    public async Task<bool> PromptSaveAllAsync()
    {
        if (EditorWebView.CoreWebView2 == null) return true;

        var dirty = _openFiles.Where(f => f.IsDirty).ToList();
        if (dirty.Count == 0) return true;

        var names = string.Join("\n", dirty.Select(f => f.Name));
        var answer = MessageBox.Show(
            dirty.Count == 1 ? $"Save changes to {names}?" : $"Save changes to these {dirty.Count} files?\n\n{names}",
            "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel) return false;

        foreach (var file in dirty)
        {
            if (answer == MessageBoxResult.Yes)
            {
                if (!await SaveAsync(file)) return false;
            }
            else
            {
                ReadIntoEditor(file); // discard: put the file back the way it is on disk
            }
        }
        return true;
    }

    private void CloseAllFiles()
    {
        foreach (var file in _openFiles)
            PostToEditor(new { type = "close", path = file.Path });
        _openFiles.Clear();
        _active = null;
        UpdateActionState();
        UpdateStatusBar();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null) return;
        var file = _active;
        ErrorReporter.FireAndForget(() => SaveAsync(file));
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (_active != null) ReadIntoEditor(_active);
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null) return;
        if (_active.IsDirty &&
            MessageBox.Show($"Discard your unsaved changes to {_active.Name} and reload it from disk?",
                "Reload file", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        ReadIntoEditor(_active);
        DiskChangedButton.Visibility = Visibility.Collapsed;
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is OpenFile file) Activate(file);
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not OpenFile file) return;
        ErrorReporter.FireAndForget(() => CloseTabAsync(file));
    }

    private async Task CloseTabAsync(OpenFile file)
    {
        if (file.IsDirty)
        {
            var answer = MessageBox.Show($"Save changes to {file.Name}?", "Unsaved changes",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.Yes && !await SaveAsync(file)) return;
        }

        var index = _openFiles.IndexOf(file);
        _openFiles.Remove(file);
        PostToEditor(new { type = "close", path = file.Path });

        if (file == _active)
        {
            _active = null;
            if (_openFiles.Count > 0) Activate(_openFiles[Math.Min(index, _openFiles.Count - 1)]);
            else { UpdateActionState(); UpdateStatusBar(); }
        }
    }

    // ═══════════ Search ═══════════

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async Task RunSearchAsync(string query)
    {
        _searchCts?.Cancel();

        if (query.Length < 2)
        {
            SearchResults.Visibility = Visibility.Collapsed;
            FileTree.Visibility = Visibility.Visible;
            return;
        }

        var appFolder = ApplicationFolder;
        if (!Directory.Exists(appFolder)) return;

        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        List<SearchHit> hits;
        try
        {
            hits = await Task.Run(() => PackageFileSearch.Search(appFolder, query, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return; // a newer query is already running

        SearchResults.ItemsSource = hits;
        SearchResults.Visibility = Visibility.Visible;
        FileTree.Visibility = Visibility.Collapsed;
    }

    private void SearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is not SearchHit hit) return;
        OpenFileInEditor(hit.Path);
        if (hit.Line > 0) PostToEditor(new { type = "reveal", line = hit.Line });
    }

    // ═══════════ External editor ═══════════

    /// <summary>Opens the active file, or the deploy script, in VS Code or ISE.</summary>
    public void OpenInExternalEditor()
    {
        var appFolder = ApplicationFolder;
        var scriptPath = Path.Combine(appFolder, "Invoke-AppDeployToolkit.ps1");

        // Prefer the open file, fall back to the script.
        var target = _active?.Path;
        if (string.IsNullOrEmpty(target) || !File.Exists(target))
            target = scriptPath;

        if (!File.Exists(target))
        {
            MessageBox.Show("Script not found. Generate the package first.", "Not Found",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var vsCode = EditorLocator.FindVSCodePath();
            var ise = vsCode == null ? EditorLocator.FindPowerShellISEPath() : null;

            ProcessStartInfo psi;
            if (vsCode != null)
                psi = new ProcessStartInfo(vsCode, $"\"{target}\"") { UseShellExecute = true };
            else if (ise != null)
                psi = new ProcessStartInfo(ise, $"\"{target}\"") { UseShellExecute = true };
            else
                psi = new ProcessStartInfo(target) { UseShellExecute = true };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open script: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // ═══════════ Models ═══════════

    /// <summary>An open file with a live Monaco buffer, shown as a tab.</summary>
    public sealed class OpenFile : INotifyPropertyChanged
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

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

}
