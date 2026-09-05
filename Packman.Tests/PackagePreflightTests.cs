using Packman.Services;
using Xunit;

namespace Packman.Tests;

public sealed class PackagePreflightTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("packman-preflight-").FullName;

    private void CreatePackage(string script)
    {
        var app = Path.Combine(_root, "Application");
        Directory.CreateDirectory(Path.Combine(app, "PSAppDeployToolkit"));
        File.WriteAllText(Path.Combine(app, "Invoke-AppDeployToolkit.exe"), "test launcher");
        File.WriteAllText(Path.Combine(app, "PSAppDeployToolkit", "PSAppDeployToolkit.psd1"), "@{}");
        File.WriteAllText(Path.Combine(app, "Invoke-AppDeployToolkit.ps1"), script);
    }

    [Fact]
    public void Reports_missing_runtime_and_script_before_building()
    {
        var issues = PackagePreflight.Check(_root);
        Assert.Equal(3, issues.Count);
        Assert.Contains(issues, i => i.Contains("PSAppDeployToolkit.psd1"));
        Assert.Throws<InvalidOperationException>(() => PackagePreflight.EnsureReady(_root));
    }

    [Theory]
    [InlineData("Start-ADTProcess -FilePath 'setup.exe' -ArgumentList '<silent flags>'")]
    [InlineData("Start-ADTProcess -FilePath 'setup.exe' -ArgumentList '<uninstall flags>'")]
    public void Blocks_generated_exe_placeholders(string script)
    {
        CreatePackage(script);
        Assert.Contains(PackagePreflight.Check(_root), i => i.Contains("silent switches"));
    }

    [Fact]
    public void Allows_comments_and_checks_syntax_without_executing_the_script()
    {
        var marker = Path.Combine(_root, "must-not-exist").Replace("'", "''");
        CreatePackage($"# Example: -ArgumentList '<silent flags>'\nSet-Content -LiteralPath '{marker}' -Value 'executed'\nStart-ADTProcess -FilePath 'setup.exe' -ArgumentList '/quiet'");
        Assert.Empty(PackagePreflight.Check(_root));
        Assert.False(File.Exists(Path.Combine(_root, "must-not-exist")));
    }

    [Fact]
    public void Reports_script_parse_errors_with_line_numbers()
    {
        CreatePackage("function Install-ADTDeployment {\n");
        Assert.Contains(PackagePreflight.Check(_root), i => i.StartsWith("Script line "));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
