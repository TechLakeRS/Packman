using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Packman.Services;

public class MsiInfoService
{
    public class MsiInfo
    {
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string ProductVersion { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string UpgradeCode { get; set; } = "";
        public bool IsValid => !string.IsNullOrEmpty(ProductCode);
    }

    public static MsiInfo ExtractMsiInfo(string msiPath)
    {
        var info = new MsiInfo();
        try
        {
            Type? installerType = Type.GetTypeFromProgID("WindowsInstaller.Installer");
            if (installerType == null) return info;

            dynamic? installer = Activator.CreateInstance(installerType);
            if (installer == null) return info;

            dynamic database = installer.OpenDatabase(msiPath, 0);
            info.ProductCode = GetMsiProperty(database, "ProductCode");
            info.ProductName = GetMsiProperty(database, "ProductName");
            info.ProductVersion = GetMsiProperty(database, "ProductVersion");
            info.Manufacturer = GetMsiProperty(database, "Manufacturer");
            info.UpgradeCode = GetMsiProperty(database, "UpgradeCode");

            Marshal.ReleaseComObject(database);
            Marshal.ReleaseComObject(installer);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MSI extraction failed: {ex.Message}");
        }
        return info;
    }

    private static string GetMsiProperty(dynamic database, string propertyName)
    {
        try
        {
            dynamic view = database.OpenView($"SELECT `Value` FROM `Property` WHERE `Property` = '{propertyName}'");
            view.Execute();
            dynamic record = view.Fetch();
            if (record != null)
            {
                string value = record.StringData[1];
                Marshal.ReleaseComObject(record);
                Marshal.ReleaseComObject(view);
                return value ?? "";
            }
            Marshal.ReleaseComObject(view);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MSI property '{propertyName}' error: {ex.Message}");
        }
        return "";
    }
}
