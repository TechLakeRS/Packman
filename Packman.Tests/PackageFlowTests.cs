using Packman.Helpers;
using Packman.Models;
using Packman.Services;
using Xunit;

namespace Packman.Tests;

/// <summary>Create a package from a template, then upgrade it: the two flows must agree.</summary>
public class PackageFlowTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    private readonly string _share;
    private readonly string _template;

    private static readonly string TemplateScript = RawTemplateScript.ReplaceLineEndings("\n");

    private const string RawTemplateScript = """
        $adtSession = @{
            AppVendor = ''
            AppName = ''
            AppVersion = ''
            AppArch = ''
            AppScriptDate = '2025-10-21'
            AppScriptAuthor = '<author name>'
            RequireAdmin = $true
        }

        function Install-ADTDeployment
        {
            ## <Perform Installation tasks here>
        }

        function Uninstall-ADTDeployment
        {
            ## <Perform Uninstallation tasks here>
        }

        # SIG # Begin signature block
        # MIIabc
        # SIG # End signature block
        """;

    public PackageFlowTests()
    {
        _share = Path.Combine(_root, "share");
        _template = Path.Combine(_root, "template");
        Directory.CreateDirectory(_share);
        Directory.CreateDirectory(Path.Combine(_template, "Files"));
        File.WriteAllText(Path.Combine(_template, PsadtLayout.ScriptName), TemplateScript, new System.Text.UTF8Encoding(true));
        File.WriteAllText(Path.Combine(_template, "Invoke-AppDeployToolkit.exe"), "stub");
    }

    public void Dispose() => Directory.Delete(_root, true);

    private string Installer(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "installer " + name);
        return path;
    }

    [Fact]
    public async Task Create_then_upgrade_rewrites_the_installer_and_keeps_the_packagers_edits()
    {
        var generator = new PSADTGenerator(_share, _template);
        var info = new ApplicationInfo
        {
            Manufacturer = "O'Reilly",
            Name = "Reader",
            Version = "1.0",
            SourcesPath = Installer("reader-1.0.exe"),
            InstallContext = "User",
            Architecture = "x64",
        };

        var created = await generator.CreatePackageAsync(info);

        Assert.Empty(created.Warnings);
        Assert.Equal(Path.Combine(_share, "O'Reilly_Reader", "1.0"), created.PackagePath);
        var scriptPath = Path.Combine(created.PackagePath, "Application", PsadtLayout.ScriptName);
        var script = PsadtScript.Load(scriptPath);
        Assert.Equal("O'Reilly", script.Vendor);
        Assert.Equal("User", script.InstallContext);
        Assert.Equal("reader-1.0.exe", script.SourceFileName);
        Assert.False(script.HasSignatureBlock);
        Assert.True(File.Exists(Path.Combine(created.PackagePath, "Application", "Files", "reader-1.0.exe")));
        Assert.True(Directory.Exists(Path.Combine(created.PackagePath, "Intune")));

        // The packager customises the script; an upgrade must carry that forward.
        var edited = script.Content.Replace("## <Perform Installation tasks here>", "## <Perform Installation tasks here>\nWrite-ADTLogEntry 'custom'");
        File.WriteAllText(scriptPath, edited, new System.Text.UTF8Encoding(true));

        var upgraded = await new PackageUpgradeService(_share)
            .UpgradePackageAsync(created.PackagePath, "2.0", Installer("reader-2.0.exe"));

        Assert.Equal(Path.Combine(_share, "O'Reilly_Reader", "2.0"), upgraded);
        var next = PsadtScript.Load(Path.Combine(upgraded, "Application", PsadtLayout.ScriptName));
        Assert.Equal("2.0", next.AppVersion);
        Assert.Equal("O'Reilly", next.Vendor);
        Assert.Equal("reader-2.0.exe", next.SourceFileName);
        Assert.Contains("Write-ADTLogEntry 'custom'", next.Content);
        Assert.False(File.Exists(Path.Combine(upgraded, "Application", "Files", "reader-1.0.exe")));
        Assert.True(File.Exists(Path.Combine(upgraded, "Application", "Files", "reader-2.0.exe")));
    }

    [Fact]
    public async Task Overwrite_deletes_only_the_version_folder()
    {
        var generator = new PSADTGenerator(_share, _template);
        var info = new ApplicationInfo { Manufacturer = "V", Name = "A", Version = "1.0", SourcesPath = Installer("a.exe") };
        var first = await generator.CreatePackageAsync(info);
        var sibling = Path.Combine(_share, "V_A", "0.9");
        Directory.CreateDirectory(sibling);

        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.CreatePackageAsync(info));
        var second = await generator.CreatePackageAsync(info, overwriteExisting: true);

        Assert.Equal(first.PackagePath, second.PackagePath);
        Assert.True(Directory.Exists(sibling));
    }

    [Fact]
    public async Task A_traversal_version_never_touches_the_share()
    {
        var generator = new PSADTGenerator(_share, _template);
        var info = new ApplicationInfo { Manufacturer = "V", Name = "A", Version = "..", SourcesPath = Installer("a.exe") };

        await Assert.ThrowsAsync<ArgumentException>(() => generator.CreatePackageAsync(info, overwriteExisting: true));
        Assert.True(Directory.Exists(_share));
    }

    [Fact]
    public async Task A_failed_upgrade_leaves_no_half_built_folder()
    {
        var generator = new PSADTGenerator(_share, _template);
        var created = await generator.CreatePackageAsync(new ApplicationInfo { Manufacturer = "V", Name = "A", Version = "1.0", SourcesPath = Installer("a.exe") });

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            new PackageUpgradeService(_share).UpgradePackageAsync(created.PackagePath, "2.0", Path.Combine(_root, "missing.exe")));

        Assert.False(Directory.Exists(Path.Combine(_share, "V_A", "2.0")));
    }
}
