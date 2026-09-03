using Packman.Helpers;
using Xunit;

namespace Packman.Tests;

public class PsadtScriptTests
{
    // Normalised to LF: a raw string literal takes the source file's own line endings,
    // and a Windows checkout turns those into CRLF.
    private static readonly string Template = RawTemplate.ReplaceLineEndings("\n");

    private const string RawTemplate = """
        $adtSession = @{
            # App variables.
            AppVendor = ''
            AppName = ''
            AppVersion = ''
            AppArch = ''
            AppLang = 'EN'
            AppScriptDate = '2025-10-21'
            AppScriptAuthor = '<author name>'
            RequireAdmin = $true
            AppNameWithVersion = 'not this one'
            DeployAppScriptParameters = $PSBoundParameters
        }

        function Install-ADTDeployment
        {
            ## <Perform Installation tasks here>

            ## <Perform Uninstallation tasks here>
        }
        """;

    [Fact]
    public void Reads_and_writes_session_values_with_escaping()
    {
        var script = PsadtScript.Parse(Template);

        script.Vendor = "O'Reilly";
        script.AppName = "Book \"Reader\"";
        script.AppVersion = "1.2.3";

        Assert.Equal("O'Reilly", script.Vendor);
        Assert.Equal("Book \"Reader\"", script.AppName);
        Assert.Contains("AppVendor = 'O''Reilly'", script.Content);

        // Re-parsing the written text yields the same values: the round trip is stable.
        var again = PsadtScript.Parse(script.Content);
        Assert.Equal("O'Reilly", again.Vendor);
        Assert.Equal("1.2.3", again.AppVersion);
    }

    [Fact]
    public void AppName_does_not_match_AppNameWithVersion()
    {
        var script = PsadtScript.Parse(Template);
        script.AppName = "Firefox";

        Assert.Contains("AppNameWithVersion = 'not this one'", script.Content);
        Assert.Equal("Firefox", script.AppName);
    }

    [Fact]
    public void RequireAdmin_maps_to_install_context()
    {
        var script = PsadtScript.Parse(Template);
        Assert.Equal("System", script.InstallContext);

        script.InstallContext = "User";
        Assert.Contains("RequireAdmin = $false", script.Content);
        Assert.Equal("User", script.InstallContext);
    }

    [Fact]
    public void Missing_key_is_reported_not_invented()
    {
        var script = PsadtScript.Parse(Template);
        Assert.False(script.SetSessionValue("AppRevision", "02"));
        Assert.Null(script.GetSessionValue("AppRevision"));
    }

    [Fact]
    public void Inserts_install_commands_after_the_markers_with_the_file_newline()
    {
        var crlf = Template.Replace("\n", "\r\n");
        var script = PsadtScript.Parse(crlf);

        Assert.True(script.InsertAfterSection(PsadtScript.InstallSection, "", "## MSI Installation",
            PsadtScript.InstallCommand(msi: true, "setup.msi")));
        Assert.True(script.InsertAfterSection(PsadtScript.UninstallSection, "", "## Uninstall MSI",
            PsadtScript.UninstallCommand(msi: true, "setup.msi", "{12345678-1234-1234-1234-123456789012}")));

        Assert.Equal("\r\n", script.NewLine);
        Assert.DoesNotContain("\n\n\n\n", script.Content);
        Assert.Contains("## <Perform Installation tasks here>\r\n\r\n## MSI Installation\r\nStart-ADTMsiProcess -Action 'Install' -FilePath \"$($adtSession.DirFiles)\\setup.msi\"\r\n", script.Content);
        Assert.Equal("{12345678-1234-1234-1234-123456789012}", script.MsiProductCode);
        Assert.False(script.InsertAfterSection("NoSuchSection", "x"));
    }

    [Fact]
    public void Upgrade_rewrites_the_generators_own_source_file_reference()
    {
        var script = PsadtScript.Parse(Template);
        script.InsertAfterSection(PsadtScript.InstallSection, PsadtScript.InstallCommand(msi: false, "app-1.0.exe"));
        script.InsertAfterSection(PsadtScript.UninstallSection, PsadtScript.UninstallCommand(msi: false, "app-1.0.exe", null));
        Assert.Equal("app-1.0.exe", script.SourceFileName);

        var changed = script.ReplaceSourceFileName("app $2.0.exe");

        Assert.Equal(2, changed);
        Assert.DoesNotContain("app-1.0.exe", script.Content);
        // The name is escaped for the double-quoted string it sits in.
        Assert.Contains("\"$($adtSession.DirFiles)\\app `$2.0.exe\"", script.Content);
    }

    [Fact]
    public void Only_the_packages_own_product_code_is_replaced()
    {
        const string old = "{AAAAAAAA-0000-0000-0000-000000000001}";
        const string other = "{BBBBBBBB-0000-0000-0000-000000000002}";
        var script = PsadtScript.Parse(Template + $"\nRemove-ADTRegistryKey -Key 'HKLM:\\CLSID\\{other}'\n");
        script.InsertAfterSection(PsadtScript.UninstallSection, PsadtScript.UninstallCommand(msi: true, "a.msi", old));

        Assert.Equal(old, script.MsiProductCode);
        Assert.Equal(1, script.ReplaceProductCode(old, "{CCCCCCCC-0000-0000-0000-000000000003}"));
        Assert.Contains(other, script.Content);
        Assert.DoesNotContain(old, script.Content);
        Assert.Equal(0, script.ReplaceProductCode(null, "{DDDDDDDD-0000-0000-0000-000000000004}"));
    }

    [Fact]
    public void Strips_a_signature_block()
    {
        var signed = Template + "\n\n# SIG # Begin signature block\n# MIIabc\n# SIG # End signature block\n";
        var script = PsadtScript.Parse(signed);

        Assert.True(script.HasSignatureBlock);
        Assert.True(script.StripSignatureBlock());
        Assert.False(script.HasSignatureBlock);
        Assert.EndsWith("}\n", script.Content);
        Assert.False(script.StripSignatureBlock());
    }

    [Fact]
    public void Load_and_save_keep_the_BOM_and_line_endings()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "Invoke-AppDeployToolkit.ps1");
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            File.WriteAllBytes(path, bom.Concat(System.Text.Encoding.UTF8.GetBytes(Template.Replace("\n", "\r\n"))).ToArray());

            var script = PsadtScript.Load(path);
            script.Vendor = "Société Générale";
            script.Save();

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(bom, bytes.Take(3));
            var text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            Assert.Contains("AppVendor = 'Société Générale'\r\n", text);
            Assert.DoesNotContain("\n\n\n", text.Replace("\r", ""));
        }
        finally { dir.Delete(true); }
    }
}
