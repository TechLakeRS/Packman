using Packman.Models;
using System.Diagnostics;

namespace Packman.Services;

public partial class IntuneUploadService
{
    private Dictionary<string, object>? ConvertDetectionRuleForBetaAPI(DetectionRule rule)
    {
        switch (rule.Type)
        {
            case DetectionRuleType.File:
                return ConvertFileDetectionForBetaAPI(rule);
            case DetectionRuleType.Registry:
                return ConvertRegistryDetectionForBetaAPI(rule);
            case DetectionRuleType.MSI:
                return ConvertMsiDetectionForBetaAPI(rule);
            case DetectionRuleType.Script:
                return ConvertScriptDetectionForBetaAPI(rule);
            default:
                Debug.WriteLine($"Cannot convert {rule.Type} detection rule for beta API");
                return null;
        }
    }

    private Dictionary<string, object> ConvertFileDetectionForBetaAPI(DetectionRule rule)
    {
        var fileRule = new Dictionary<string, object>
        {
            ["@odata.type"] = "#microsoft.graph.win32LobAppFileSystemDetection",
            ["path"] = rule.Path.Trim(),
            ["fileOrFolderName"] = rule.FileOrFolderName.Trim(),
            ["check32BitOn64System"] = rule.Check32BitOn64System
        };

        if (rule.CheckVersion)
        {
            fileRule["detectionType"] = "version";
            fileRule["operator"] = string.IsNullOrEmpty(rule.Operator) ? "greaterThanOrEqual" : rule.Operator;
            fileRule["detectionValue"] = string.IsNullOrEmpty(rule.DetectionValue) ? "1.0.0" : rule.DetectionValue;
        }
        else
        {
            fileRule["detectionType"] = "exists";
        }

        return fileRule;
    }

    private Dictionary<string, object> ConvertRegistryDetectionForBetaAPI(DetectionRule rule)
    {
        var registryRule = new Dictionary<string, object>
        {
            ["@odata.type"] = "#microsoft.graph.win32LobAppRegistryDetection",
            ["keyPath"] = rule.Path.Trim(),
            ["check32BitOn64System"] = false
        };

        if (!string.IsNullOrEmpty(rule.FileOrFolderName))
        {
            registryRule["valueName"] = rule.FileOrFolderName.Trim();
            registryRule["detectionType"] = "exists";
        }
        else
        {
            registryRule["detectionType"] = "exists";
        }

        return registryRule;
    }

    private Dictionary<string, object> ConvertMsiDetectionForBetaAPI(DetectionRule rule)
    {
        var msiRule = new Dictionary<string, object>
        {
            ["@odata.type"] = "#microsoft.graph.win32LobAppProductCodeDetection",
            ["productCode"] = rule.Path.Trim(),
            ["productVersionOperator"] = "notConfigured"
        };

        if (rule.CheckVersion && !string.IsNullOrEmpty(rule.FileOrFolderName))
        {
            var versionParts = rule.FileOrFolderName.Split(':');
            if (versionParts.Length == 2)
            {
                var operatorText = versionParts[0].Trim();
                var versionValue = versionParts[1].Trim();

                var apiOperator = operatorText switch
                {
                    "Greater than or equal to" => "greaterThanOrEqual",
                    "Equal to" => "equal",
                    "Greater than" => "greaterThan",
                    "Less than" => "lessThan",
                    "Less than or equal to" => "lessThanOrEqual",
                    _ => "greaterThanOrEqual"
                };

                msiRule["productVersionOperator"] = apiOperator;
                msiRule["productVersion"] = versionValue;
            }
        }

        return msiRule;
    }

    private Dictionary<string, object> ConvertScriptDetectionForBetaAPI(DetectionRule rule)
    {
        return new Dictionary<string, object>
        {
            ["@odata.type"] = "#microsoft.graph.win32LobAppPowerShellScriptDetection",
            ["scriptContent"] = rule.ScriptContent,
            ["enforceSignatureCheck"] = rule.EnforceSignatureCheck,
            ["runAs32Bit"] = rule.RunAs32Bit
        };
    }
}
