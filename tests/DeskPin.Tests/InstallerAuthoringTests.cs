using System.Xml.Linq;

namespace DeskPin.Tests;

public sealed class InstallerAuthoringTests
{
    private const string ReleaseVersion = "1.1.0";
    private const string AssemblyReleaseVersion = "1.1.0.0";

    [Fact]
    public void InstallerProvidesPerUserLocalizedInstallDirectoryUi()
    {
        var root = FindRepositoryRoot();
        var package = XDocument.Load(Path.Combine(root, "installer", "DeskPin.Installer", "Package.wxs"));
        XNamespace wix = "http://wixtoolset.org/schemas/v4/wxs";
        XNamespace ui = "http://wixtoolset.org/schemas/v4/wxs/ui";

        var packageElement = Assert.Single(package.Descendants(wix + "Package"));
        Assert.Equal("perUser", packageElement.Attribute("Scope")?.Value);
        Assert.Equal("2052", packageElement.Attribute("Language")?.Value);

        var installDirectoryUi = Assert.Single(package.Descendants(ui + "WixUI"));
        Assert.Equal("WixUI_InstallDir", installDirectoryUi.Attribute("Id")?.Value);
        Assert.Equal("INSTALLFOLDER", installDirectoryUi.Attribute("InstallDirectory")?.Value);

        var installLocation = Assert.Single(
            package.Descendants(wix + "RegistryValue"),
            value => value.Attribute("Name")?.Value == "InstallLocation");
        Assert.Equal("[INSTALLFOLDER]", installLocation.Attribute("Value")?.Value);
        Assert.True(File.Exists(Path.Combine(root, "installer", "DeskPin.Installer", "License.rtf")));
    }

    [Fact]
    public void InstallerProjectReferencesMatchingUiExtensionAndChineseCulture()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "installer", "DeskPin.Installer", "DeskPin.Installer.wixproj"));

        var packageReference = Assert.Single(
            project.Descendants("PackageReference"),
            reference => reference.Attribute("Include")?.Value == "WixToolset.UI.wixext");
        Assert.Equal("5.0.2", packageReference.Attribute("Version")?.Value);
        Assert.Equal("zh-CN", Assert.Single(project.Descendants("Cultures")).Value);
        Assert.Equal("high", Assert.Single(project.Descendants("DefaultCompressionLevel")).Value);
    }

    [Fact]
    public void ApplicationUsesCompressedChineseOnlyPublishWithoutWinForms()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "src", "DeskPin", "DeskPin.csproj");
        var project = XDocument.Load(projectPath);

        Assert.Empty(project.Descendants("UseWindowsForms"));
        Assert.Equal("Assets\\DeskPin.ico", Assert.Single(project.Descendants("ApplicationIcon")).Value);
        Assert.Equal("true", Assert.Single(project.Descendants("EnableCompressionInSingleFile")).Value);
        Assert.Equal("zh-Hans", Assert.Single(project.Descendants("SatelliteResourceLanguages")).Value);
        Assert.Equal("false", Assert.Single(project.Descendants("PublishTrimmed")).Value);
        var excludedPublishFile = Assert.Single(
            project.Descendants("ResolvedFileToPublish"),
            item => item.Attribute("Remove") is not null);
        Assert.Contains("System.Drawing.Common.dll", excludedPublishFile.Attribute("Condition")?.Value);

        var sourceFiles = Directory.GetFiles(
            Path.Combine(root, "src", "DeskPin"),
            "*.cs",
            SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        Assert.DoesNotContain(sourceFiles, path =>
        {
            var source = File.ReadAllText(path);
            return source.Contains("System.Windows.Forms", StringComparison.Ordinal) ||
                   source.Contains("System.Drawing", StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ApplicationManifestAndInstallerUseMatchingReleaseVersion()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src", "DeskPin", "DeskPin.csproj"));
        var manifest = XDocument.Load(Path.Combine(root, "src", "DeskPin", "app.manifest"));
        var package = XDocument.Load(Path.Combine(root, "installer", "DeskPin.Installer", "Package.wxs"));
        XNamespace assembly = "urn:schemas-microsoft-com:asm.v1";
        XNamespace wix = "http://wixtoolset.org/schemas/v4/wxs";

        Assert.Equal(ReleaseVersion, Assert.Single(project.Descendants("Version")).Value);
        Assert.Equal(AssemblyReleaseVersion, Assert.Single(project.Descendants("AssemblyVersion")).Value);
        Assert.Equal(AssemblyReleaseVersion, Assert.Single(project.Descendants("FileVersion")).Value);
        Assert.Equal(
            AssemblyReleaseVersion,
            Assert.Single(manifest.Descendants(assembly + "assemblyIdentity")).Attribute("version")?.Value);

        var packageElement = Assert.Single(package.Descendants(wix + "Package"));
        Assert.Equal(ReleaseVersion, packageElement.Attribute("Version")?.Value);
        Assert.Equal(
            "8E4278A5-5014-49CF-9E85-2C3A38B01D77",
            packageElement.Attribute("UpgradeCode")?.Value);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskPin.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("未找到 DeskPin 仓库根目录。");
    }
}
