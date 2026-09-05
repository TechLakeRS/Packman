using Packman.Helpers;
using Packman.Models;
using Packman.Services;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Packman.Tests;

public class TextFileIOTests
{
    [Fact]
    public void Detects_and_preserves_the_encoding()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "a.ps1");
            File.WriteAllText(path, "héllo\r\nworld", new UTF8Encoding(true));

            var file = TextFileIO.Read(path);
            Assert.True(file.Crlf);
            Assert.Equal("héllo\r\nworld", file.Content);
            Assert.NotEmpty(file.Encoding.GetPreamble());

            TextFileIO.Write(path, "changed", file.Encoding);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
            Assert.False(File.Exists(path + TextFileIO.TempSuffix));
        }
        finally { dir.Delete(true); }
    }
}

public class DetectionRuleGraphTests
{
    [Fact]
    public void File_version_rule_round_trips_through_graph_json()
    {
        var rule = DetectionRuleFactory.FileVersion(@"%ProgramFiles%\App", "app.exe", "1.2.3");
        var payload = DetectionRuleGraph.Serialize(rule);

        Assert.Equal("#microsoft.graph.win32LobAppFileSystemDetection", payload["@odata.type"]);
        Assert.Equal("version", payload["detectionType"]);
        Assert.Equal("greaterThanOrEqual", payload["operator"]);
        Assert.Equal("1.2.3", payload["detectionValue"]);
        Assert.Equal(true, payload["check32BitOn64System"]);

        var app = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new { detectionRules = new[] { payload } }));
        var parsed = Assert.Single(DetectionRuleGraph.Parse(app));
        Assert.Equal(DetectionRuleType.File, parsed.Type);
        Assert.True(parsed.CheckVersion);
        Assert.Equal("app.exe", parsed.FileOrFolderName);
    }

    [Fact]
    public void Registry_and_msi_rules_keep_their_fields()
    {
        var registry = DetectionRuleGraph.Serialize(DetectionRuleFactory.RegistryKeyExists("HKEY_LOCAL_MACHINE", @"\SOFTWARE\App\", "Version"));
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\App", registry["keyPath"]);
        Assert.Equal("exists", registry["detectionType"]);
        Assert.Equal("notConfigured", registry["operator"]);
        Assert.Null(registry["detectionValue"]);

        var msi = DetectionRuleGraph.Serialize(DetectionRuleFactory.Msi(" {12345678-1234-1234-1234-123456789012} "));
        Assert.Equal("{12345678-1234-1234-1234-123456789012}", msi["productCode"]);
        Assert.Equal("notConfigured", msi["productVersionOperator"]);
    }
}

public class DetectionDiscoveryTests
{
    [Fact]
    public void Profile_paths_become_LOCALAPPDATA()
    {
        Assert.Equal(@"%LOCALAPPDATA%\Vendor\App",
            DetectionDiscoveryService.ToLocalPath(@"\\PC01\C$\Users\bob\AppData\Local\Vendor\App", "PC01"));
        Assert.Equal(@"C:\Program Files\App",
            DetectionDiscoveryService.ToLocalPath(@"\\pc01\C$\Program Files\App", "PC01"));
    }
}

public class SmallHelperTests
{
    [Theory]
    [InlineData(0, "—")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void ByteSize_formats(long bytes, string expected)
    {
        // Size labels follow the user's decimal separator, including comma locales.
        var localized = expected.Replace(".", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
        Assert.Equal(localized, ByteSize.Format(bytes));
    }

    [Fact]
    public void RequirementInfo_parse_ignores_blank_and_non_positive()
    {
        var req = RequirementInfo.Parse("", "1024", "0", "abc", " 8 ");
        Assert.Equal("Windows 10 1607", req.MinimumOperatingSystem);
        Assert.Equal(1024, req.MinimumFreeDiskSpaceMB);
        Assert.Null(req.MinimumMemoryMB);
        Assert.Null(req.MinimumNumberOfProcessors);
        Assert.Equal(8, req.MinimumCpuSpeedMHz);
    }

    [Fact]
    public void GraphException_uses_the_message_from_the_error_body()
    {
        var ex = new GraphException("Create group", 403, """{"error":{"code":"Authorization_RequestDenied","message":"Insufficient privileges"}}""", "abc");
        Assert.True(ex.IsForbidden);
        Assert.Contains("Authorization_RequestDenied: Insufficient privileges", ex.Message);
        Assert.Contains("HTTP 403", ex.Message);
    }

    [Fact]
    public void DirectoryCopy_skips_the_excluded_top_level_folder_only()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var src = Path.Combine(root, "src");
            Directory.CreateDirectory(Path.Combine(src, "Files"));
            Directory.CreateDirectory(Path.Combine(src, "SupportFiles", "Files"));
            File.WriteAllText(Path.Combine(src, "Files", "old.msi"), "x");
            File.WriteAllText(Path.Combine(src, "SupportFiles", "Files", "keep.txt"), "x");
            File.WriteAllText(Path.Combine(src, "script.ps1"), "x");

            var dst = Path.Combine(root, "dst");
            DirectoryCopy.Copy(src, dst, default, "Files");

            Assert.False(File.Exists(Path.Combine(dst, "Files", "old.msi")));
            Assert.True(File.Exists(Path.Combine(dst, "SupportFiles", "Files", "keep.txt")));
            Assert.True(File.Exists(Path.Combine(dst, "script.ps1")));
            Assert.Equal(2, DirectoryCopy.TotalSize(dst));
        }
        finally { Directory.Delete(root, true); }
    }
}
