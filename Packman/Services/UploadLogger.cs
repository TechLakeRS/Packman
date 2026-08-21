using System.Diagnostics;
using System.IO;
using System.Text;

namespace Packman.Services;

public enum LogOperationType
{
    Upload,
    Update
}

/// <summary>
/// Per-package upload log, written to
/// %LocalAppData%\Packman\Logs\{Upload|Update}\{App}-{date}.log.
/// </summary>
public class UploadLogger : IDisposable
{
    private readonly string _logFilePath;
    private readonly object _lockObject = new();
    private StreamWriter? _logWriter;
    private bool _disposed;
    private readonly LogOperationType _operationType;

    public UploadLogger(string applicationName) : this(applicationName, LogOperationType.Upload) { }

    public UploadLogger(string applicationName, LogOperationType operationType)
    {
        _operationType = operationType;

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packman", "Logs", operationType.ToString());

        if (!Directory.Exists(logDirectory))
            Directory.CreateDirectory(logDirectory);

        var safeAppName = string.Join("_", applicationName.Split(Path.GetInvalidFileNameChars()));
        var fileName = $"{safeAppName}-{DateTime.Now:yyyy-MM-dd}.log";
        _logFilePath = Path.Combine(logDirectory, fileName);

        InitializeLogFile();
    }

    private void InitializeLogFile()
    {
        try
        {
            _logWriter = new StreamWriter(_logFilePath, append: true, Encoding.UTF8) { AutoFlush = true };
            LogSessionStart();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UploadLogger: Failed to initialize log file: {ex.Message}");
        }
    }

    private void LogSessionStart()
    {
        var separator = new string('=', 80);
        WriteToLog(separator);
        WriteToLog($"{_operationType} Session Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteToLog(separator);
        WriteToLog("");
    }

    public void Info(string message) => WriteToLog($"[INFO ] {DateTime.Now:HH:mm:ss} - {message}");
    public void Success(string message) => WriteToLog($"[ OK  ] {DateTime.Now:HH:mm:ss} - {message}");
    public void Warning(string message) => WriteToLog($"[WARN ] {DateTime.Now:HH:mm:ss} - {message}");
    public void Error(string message) => WriteToLog($"[ERROR] {DateTime.Now:HH:mm:ss} - {message}");

    public void Error(string message, Exception ex)
    {
        WriteToLog($"[ERROR] {DateTime.Now:HH:mm:ss} - {message}");
        WriteToLog($"        Exception: {ex.GetType().Name}");
        WriteToLog($"        Message: {ex.Message}");
        if (ex.InnerException != null)
            WriteToLog($"        Inner Exception: {ex.InnerException.Message}");
        WriteToLog("        Stack Trace:");
        WriteToLog($"        {ex.StackTrace?.Replace(Environment.NewLine, Environment.NewLine + "        ")}");
    }

    public void Progress(int percentage, string message) => WriteToLog($"[{percentage,3}%] {DateTime.Now:HH:mm:ss} - {message}");
    public void LogMetadata(string key, string value) => WriteToLog($"  {key}: {value}");

    public void Section(string sectionName)
    {
        WriteToLog("");
        WriteToLog($"--- {sectionName} ---");
    }

    private void WriteToLog(string message)
    {
        if (_disposed || _logWriter == null)
            return;

        lock (_lockObject)
        {
            try
            {
                _logWriter.WriteLine(message);
                Debug.WriteLine(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UploadLogger: Failed to write to log: {ex.Message}");
            }
        }
    }

    private void LogSessionEnd()
    {
        WriteToLog("");
        WriteToLog($"{_operationType} Session Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteToLog(new string('=', 80));
        WriteToLog("");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lockObject)
        {
            if (_logWriter != null)
            {
                LogSessionEnd();
                _logWriter.Flush();
                _logWriter.Dispose();
                _logWriter = null;
            }
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    public string LogFilePath => _logFilePath;
}
