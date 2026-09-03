using Packman.Helpers;
using Xunit;

namespace Packman.Tests;

public class PackagePathsTests
{
    [Theory]
    [InlineData("Mozilla", "Firefox", "Mozilla_Firefox")]
    [InlineData("Foo/Bar Inc.", "App: One", "Foo_Bar_Inc._App__One")]
    [InlineData("  spaced  ", "name ", "spaced_name")]
    public void App_folder_names_are_underscored_and_sanitised(string vendor, string app, string expected)
        => Assert.Equal(expected, PackagePaths.AppFolderName(vendor, app));

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("   ")]
    [InlineData("...")]
    public void Segments_that_leave_nothing_behind_are_rejected(string value)
        => Assert.Throws<ArgumentException>(() => PackagePaths.SanitizeSegment(value));

    [Fact]
    public void Reserved_device_names_are_prefixed()
        => Assert.Equal("_CON", PackagePaths.SanitizeSegment("CON"));

    [Fact]
    public void Trailing_dots_and_spaces_are_trimmed()
        => Assert.Equal("1.0", PackagePaths.SanitizeSegment("1.0. "));

    [Fact]
    public void A_traversal_version_cannot_escape_the_root()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Throws<ArgumentException>(() => PackagePaths.VersionFolder(root, "Vendor_App", ".."));
            var ok = PackagePaths.VersionFolder(root, "Vendor_App", "1.0.0");
            Assert.StartsWith(root, ok);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DeleteInside_refuses_the_root_itself_and_anything_above()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Throws<InvalidOperationException>(() => PackagePaths.DeleteInside(root, root));
            Assert.Throws<InvalidOperationException>(() => PackagePaths.DeleteInside(root, Path.GetDirectoryName(root)!));

            var child = Path.Combine(root, "Vendor_App", "1.0");
            Directory.CreateDirectory(child);
            PackagePaths.DeleteInside(root, child);
            Assert.False(Directory.Exists(child));
            Assert.True(Directory.Exists(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
