using LanMountainDesktop.Shared.Contracts.Launcher;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class AppVersionProviderTests
{
    [Fact]
    public void ResolveFromPackageRoot_WhenVersionJsonExists_UsesVersionFile()
    {
        using var temp = TemporaryPackage.Create();
        temp.CreateDeployment("app-0.8.5.7", """
        {"Version":"0.8.5.7","Codename":"Administrate"}
        """);

        var info = AppVersionProvider.ResolveFromPackageRoot(temp.Root, "LanMountainDesktop.exe");

        Assert.Equal("0.8.5.7", info.Version);
        Assert.Equal("Administrate", info.Codename);
    }

    [Fact]
    public void ResolveFromPackageRoot_WhenVersionJsonIsMissing_FallsBackToDeploymentDirectory()
    {
        using var temp = TemporaryPackage.Create();
        temp.CreateDeployment("app-0.8.5.7");

        var info = AppVersionProvider.ResolveFromPackageRoot(temp.Root, "LanMountainDesktop.exe");

        Assert.Equal("0.8.5.7", info.Version);
    }

    [Fact]
    public void ResolveFromPackageRoot_WhenVersionJsonContainsQuotedValues_NormalizesValues()
    {
        using var temp = TemporaryPackage.Create();
        temp.CreateDeployment("app-1.2.3", """
        {"Version":"'1.2.3'","Codename":"'Administrate'"}
        """);

        var info = AppVersionProvider.ResolveFromPackageRoot(temp.Root, "LanMountainDesktop.exe");

        Assert.Equal("1.2.3", info.Version);
        Assert.Equal("Administrate", info.Codename);
    }

    private sealed class TemporaryPackage : IDisposable
    {
        private TemporaryPackage(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporaryPackage Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.VersionTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TemporaryPackage(root);
        }

        public void CreateDeployment(string name, string? versionJson = null)
        {
            var deployment = Path.Combine(Root, name);
            Directory.CreateDirectory(deployment);
            File.WriteAllText(Path.Combine(deployment, "LanMountainDesktop.exe"), string.Empty);
            File.WriteAllText(Path.Combine(deployment, ".current"), string.Empty);
            if (versionJson is not null)
            {
                File.WriteAllText(Path.Combine(deployment, "version.json"), versionJson);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
