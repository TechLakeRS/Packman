using System.IO;

namespace Packman.Services;

/// <summary>
/// Represents a single PSADT v4 function with its metadata and parameters.
/// </summary>
public class PSADTFunction
{
    public string Name { get; set; } = string.Empty;
    public string Synopsis { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<PSADTParameter> Parameters { get; set; } = new();

    /// <summary>
    /// Generates a PowerShell call string with mandatory parameter placeholders.
    /// </summary>
    public string GenerateCallWithPlaceholders()
    {
        var mandatoryParams = Parameters
            .Where(p => p.Mandatory && !p.IsSwitch && p.Name != "(none)")
            .ToList();

        // If no mandatory params, include the first few optional non-switch params
        // so the user sees what parameters are available
        var paramsToShow = mandatoryParams;
        if (paramsToShow.Count == 0)
        {
            paramsToShow = Parameters
                .Where(p => !p.IsSwitch && p.Name != "(none)")
                .Take(3)
                .ToList();
        }

        if (paramsToShow.Count == 0)
            return Name;

        var parts = new List<string> { Name };
        foreach (var param in paramsToShow)
        {
            string placeholder = GetPlaceholder(param);
            parts.Add($"-{param.Name} {placeholder}");
        }

        return string.Join(" ", parts);
    }

    private static string GetPlaceholder(PSADTParameter param)
    {
        return param.Type switch
        {
            "String" => $"'<{param.Name.ToUpper()}>'",
            "String[]" => $"'<{param.Name.ToUpper()}>'",
            "FileInfo" => $"'<FILE_PATH>'",
            "FileInfo[]" => $"'<FILE_PATH>'",
            "DirectoryInfo" => $"'<DIR_PATH>'",
            "DirectoryInfo[]" => $"'<DIR_PATH>'",
            "Guid" => "'{00000000-0000-0000-0000-000000000000}'",
            "Guid[]" => "'{00000000-0000-0000-0000-000000000000}'",
            "Int32" => "0",
            "Int32[]" => "0",
            "Int64" => "0",
            "UInt32" => "0",
            "Nullable`1" => "0",
            "ScriptBlock" => "{ <# SCRIPT_BLOCK #> }",
            "ScriptBlock[]" => "{ <# SCRIPT_BLOCK #> }",
            "Hashtable" => "@{ <# KEY = VALUE #> }",
            "IDictionary" => "@{ <# KEY = VALUE #> }",
            "TimeSpan" => "(New-TimeSpan -Minutes 5)",
            "DateTime" => "(Get-Date).AddDays(7)",
            "Version" => "'1.0.0'",
            "ServiceController" => "(Get-Service -Name '<SERVICE_NAME>')",
            "ProcessDefinition[]" => "@('<PROCESS_NAME>')",
            "UserProfile[]" => "(Get-ADTUserProfiles)",
            "InstalledApplication[]" => "(Get-ADTApplication -Name '<APP_NAME>')",
            "InstalledApplication" => "(Get-ADTApplication -Name '<APP_NAME>')",
            "Object" => "'<VALUE>'",
            "Object[]" => "'<VALUE>'",
            _ => $"'<{param.Name.ToUpper()}>'"
        };
    }
}

/// <summary>
/// Represents a single parameter of a PSADT function.
/// </summary>
public class PSADTParameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Mandatory { get; set; }
    public bool IsSwitch { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Loads and categorizes PSADT v4 functions from the CSV reference file.
/// Excludes internal/module functions and those already in the template.
/// </summary>
public static class PSADTFunctionCatalog
{
    // Functions already called in the template - exclude from browser
    private static readonly HashSet<string> TemplateExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Show-ADTInstallationWelcome",
        "Show-ADTInstallationProgress",
        "Show-ADTInstallationPrompt",
        "Open-ADTSession",
        "Close-ADTSession",
        "Write-ADTLogEntry",
        "Resolve-ADTErrorRecord",
        "Get-ADTBoundParametersAndDefaultValues",
        "Remove-ADTHashtableNullOrEmptyValues"
    };

    // Internal/module/deprecated functions - exclude from browser
    private static readonly HashSet<string> InternalExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Initialize-ADTFunction", "Complete-ADTFunction", "Invoke-ADTFunctionErrorHandler",
        "Add-ADTModuleCallback", "Remove-ADTModuleCallback", "Get-ADTModuleCallback", "Clear-ADTModuleCallback",
        "New-ADTErrorRecord", "New-ADTValidateScriptErrorRecord",
        "Convert-ADTValuesFromRemainingArguments", "Convert-ADTValueType",
        "Remove-ADTInvalidFileNameChars", "Get-ADTObjectProperty", "Invoke-ADTObjectMethod",
        "Export-ADTEnvironmentTableToSessionState", "Get-ADTEnvironmentTable",
        "Get-ADTEnvironment", // deprecated
        "Get-ADTUniversalDate", // deprecated
        "Install-ADTMSUpdates", // deprecated
        "Test-ADTModuleInitialized", "Test-ADTSessionActive", "Get-ADTSession",
        "Initialize-ADTModule", "Get-ADTCommandTable", "Get-ADTConfig", "Get-ADTStringTable",
        "Get-ADTDeferHistory", "Set-ADTDeferHistory", "Reset-ADTDeferHistory",
        "Enable-ADTTerminalServerInstallMode", "Disable-ADTTerminalServerInstallMode",
        "New-ADTTemplate", "Show-ADTHelpConsole", "Get-ADTPowerShellProcessPath",
        "Remove-ADTHashtableNullOrEmptyValues", "Get-ADTBoundParametersAndDefaultValues"
    };

    // Category assignments
    private static readonly Dictionary<string, string> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Application Management
        { "Get-ADTApplication", "Application Management" },
        { "Uninstall-ADTApplication", "Application Management" },
        { "Block-ADTAppExecution", "Application Management" },
        { "Unblock-ADTAppExecution", "Application Management" },
        { "Get-ADTRunningProcesses", "Application Management" },

        // Process Execution
        { "Start-ADTProcess", "Process Execution" },
        { "Start-ADTProcessAsUser", "Process Execution" },
        { "Start-ADTMsiProcess", "Process Execution" },
        { "Start-ADTMsiProcessAsUser", "Process Execution" },
        { "Start-ADTMspProcess", "Process Execution" },
        { "Start-ADTMspProcessAsUser", "Process Execution" },

        // User Interaction
        { "Show-ADTInstallationRestartPrompt", "User Interaction" },
        { "Show-ADTDialogBox", "User Interaction" },
        { "Show-ADTBalloonTip", "User Interaction" },
        { "Close-ADTInstallationProgress", "User Interaction" },

        // Registry
        { "Get-ADTRegistryKey", "Registry" },
        { "Set-ADTRegistryKey", "Registry" },
        { "Remove-ADTRegistryKey", "Registry" },
        { "Test-ADTRegistryValue", "Registry" },
        { "Convert-ADTRegistryPath", "Registry" },
        { "Invoke-ADTAllUsersRegistryAction", "Registry" },

        // File & Folder
        { "Copy-ADTFile", "File & Folder" },
        { "Copy-ADTFileToUserProfiles", "File & Folder" },
        { "Remove-ADTFile", "File & Folder" },
        { "Remove-ADTFileFromUserProfiles", "File & Folder" },
        { "Remove-ADTFolder", "File & Folder" },
        { "New-ADTFolder", "File & Folder" },
        { "New-ADTZipFile", "File & Folder" },
        { "Copy-ADTContentToCache", "File & Folder" },
        { "Remove-ADTContentFromCache", "File & Folder" },

        // Shortcuts
        { "New-ADTShortcut", "Shortcuts" },
        { "Set-ADTShortcut", "Shortcuts" },
        { "Get-ADTShortcut", "Shortcuts" },

        // Services
        { "Start-ADTServiceAndDependencies", "Services" },
        { "Stop-ADTServiceAndDependencies", "Services" },
        { "Test-ADTServiceExists", "Services" },
        { "Get-ADTServiceStartMode", "Services" },
        { "Set-ADTServiceStartMode", "Services" },

        // Environment & System
        { "Get-ADTEnvironmentVariable", "Environment & System" },
        { "Set-ADTEnvironmentVariable", "Environment & System" },
        { "Remove-ADTEnvironmentVariable", "Environment & System" },
        { "Update-ADTDesktop", "Environment & System" },
        { "Update-ADTEnvironmentPsProvider", "Environment & System" },
        { "Update-ADTGroupPolicy", "Environment & System" },

        // INI Files
        { "Get-ADTIniValue", "INI Files" },
        { "Set-ADTIniValue", "INI Files" },
        { "Get-ADTIniSection", "INI Files" },
        { "Set-ADTIniSection", "INI Files" },
        { "Remove-ADTIniValue", "INI Files" },
        { "Remove-ADTIniSection", "INI Files" },

        // DLL Registration
        { "Register-ADTDll", "DLL Registration" },
        { "Unregister-ADTDll", "DLL Registration" },
        { "Invoke-ADTRegSvr32", "DLL Registration" },

        // MSI/MSP Tools
        { "Get-ADTMsiTableProperty", "MSI/MSP Tools" },
        { "New-ADTMsiTransform", "MSI/MSP Tools" },
        { "Set-ADTMsiProperty", "MSI/MSP Tools" },
        { "Get-ADTMsiExitCodeMessage", "MSI/MSP Tools" },

        // System Checks
        { "Test-ADTBattery", "System Checks" },
        { "Test-ADTNetworkConnection", "System Checks" },
        { "Test-ADTPowerPoint", "System Checks" },
        { "Test-ADTMicrophoneInUse", "System Checks" },
        { "Test-ADTUserIsBusy", "System Checks" },
        { "Test-ADTCallerIsAdmin", "System Checks" },
        { "Test-ADTMSUpdates", "System Checks" },
        { "Test-ADTMutexAvailability", "System Checks" },
        { "Get-ADTPendingReboot", "System Checks" },
        { "Get-ADTFreeDiskSpace", "System Checks" },
        { "Get-ADTOperatingSystemInfo", "System Checks" },
        { "Get-ADTLoggedOnUser", "System Checks" },
        { "Test-ADTEspActive", "System Checks" },

        // Advanced
        { "Set-ADTActiveSetup", "Advanced" },
        { "Add-ADTEdgeExtension", "Advanced" },
        { "Remove-ADTEdgeExtension", "Advanced" },
        { "Mount-ADTWimFile", "Advanced" },
        { "Dismount-ADTWimFile", "Advanced" },
        { "Send-ADTKeys", "Advanced" },
        { "Set-ADTItemPermission", "Advanced" },
        { "Set-ADTPowerShellCulture", "Advanced" },
        { "ConvertTo-ADTNTAccountOrSID", "Advanced" },
        { "Invoke-ADTCommandWithRetries", "Advanced" },
        { "Install-ADTSCCMSoftwareUpdates", "Advanced" },
        { "Invoke-ADTSCCMTask", "Advanced" },

        // Info & Utility
        { "Get-ADTFileVersion", "Info & Utility" },
        { "Get-ADTWindowTitle", "Info & Utility" },
        { "Get-ADTUserProfiles", "Info & Utility" },
        { "Get-ADTExecutableInfo", "Info & Utility" },
        { "Get-ADTPEFileArchitecture", "Info & Utility" },
        { "Out-ADTPowerShellEncodedCommand", "Info & Utility" },
        { "Get-ADTPresentationSettingsEnabledUsers", "Info & Utility" },
        { "Get-ADTUserNotificationState", "Info & Utility" },
    };

    // Category display order
    public static readonly string[] CategoryOrder =
    {
        "Application Management",
        "Process Execution",
        "User Interaction",
        "Registry",
        "File & Folder",
        "Shortcuts",
        "Services",
        "Environment & System",
        "INI Files",
        "DLL Registration",
        "MSI/MSP Tools",
        "System Checks",
        "Advanced",
        "Info & Utility",
        OtherCategory
    };

    /// <summary>Catches functions a PSADT upgrade adds before CategoryMap knows about them.</summary>
    public const string OtherCategory = "Other";

    /// <summary>
    /// Loads functions from the detailed CSV (PSADT_v4_Functions.csv) with per-parameter rows.
    /// Falls back to the reference CSV if the detailed one is not found.
    /// </summary>
    public static List<PSADTFunction> LoadFromCsv(string csvPath)
    {
        var functions = new Dictionary<string, PSADTFunction>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(csvPath))
            return new List<PSADTFunction>();

        var lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2) return new List<PSADTFunction>();

        // Detect format: detailed CSV has 10 columns, reference CSV has 3
        var headerParts = ParseCsvLine(lines[0]);

        if (headerParts.Length >= 10 && headerParts[2].Equals("Parameter", StringComparison.OrdinalIgnoreCase))
        {
            // Detailed CSV format: Function,Synopsis,Parameter,Type,Mandatory,IsSwitch,...,Description
            LoadDetailedCsv(lines, functions);
        }
        else
        {
            // Reference CSV format: Function,Synopsis,Syntax
            LoadReferenceCsv(lines, functions);
        }

        return functions.Values
            .Where(f => !TemplateExclusions.Contains(f.Name) && !InternalExclusions.Contains(f.Name))
            .Select(f =>
            {
                f.Category = CategoryMap.TryGetValue(f.Name, out var category) ? category : OtherCategory;
                return f;
            })
            .OrderBy(f => Array.IndexOf(CategoryOrder, f.Category))
            .ThenBy(f => f.Name)
            .ToList();
    }

    private static void LoadDetailedCsv(string[] lines, Dictionary<string, PSADTFunction> functions)
    {
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = ParseCsvLine(lines[i]);
            if (parts.Length < 6) continue;

            var funcName = parts[0].Trim();
            var synopsis = parts[1].Trim();
            var paramName = parts[2].Trim();
            var paramType = parts[3].Trim();
            var mandatory = parts[4].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            var isSwitch = parts[5].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            var description = parts.Length >= 10 ? parts[9].Trim() : "";

            if (string.IsNullOrWhiteSpace(funcName)) continue;

            if (!functions.TryGetValue(funcName, out var func))
            {
                func = new PSADTFunction { Name = funcName, Synopsis = synopsis };
                functions[funcName] = func;
            }

            if (!string.IsNullOrWhiteSpace(paramName) && paramName != "(none)")
            {
                func.Parameters.Add(new PSADTParameter
                {
                    Name = paramName,
                    Type = paramType,
                    Mandatory = mandatory,
                    IsSwitch = isSwitch,
                    Description = description
                });
            }
        }
    }

    private static void LoadReferenceCsv(string[] lines, Dictionary<string, PSADTFunction> functions)
    {
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = ParseCsvLine(lines[i]);
            if (parts.Length < 2) continue;

            var funcName = parts[0].Trim();
            var synopsis = parts[1].Trim();

            if (string.IsNullOrWhiteSpace(funcName)) continue;

            functions[funcName] = new PSADTFunction
            {
                Name = funcName,
                Synopsis = synopsis
            };
        }
    }

    /// <summary>
    /// Simple CSV line parser that handles quoted fields with commas.
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    /// <summary>
    /// Gets the path to the PSADT functions CSV bundled with the application.
    /// Prefers the detailed CSV, falls back to the reference CSV.
    /// </summary>
    public static string GetCsvPath()
    {
        var appDir = AppContext.BaseDirectory;
        var detailed = Path.Combine(appDir, "PSADT_v4_Functions.csv");
        if (File.Exists(detailed)) return detailed;

        return Path.Combine(appDir, "PSADT_v4_Functions_Reference.csv");
    }
}
