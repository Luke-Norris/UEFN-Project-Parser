using FortniteForge.Core.Config;
using Xunit;

namespace FortniteForge.Tests;

public class ForgeConfigTests
{
    [Fact]
    public void DefaultConfig_HasExpectedDefaults()
    {
        var config = new ForgeConfig();

        Assert.Equal("", config.ProjectPath);
        Assert.True(config.RequireDryRun);
        Assert.True(config.AutoBackup);
        Assert.Equal(10, config.MaxBackupsPerFile);
        Assert.Equal(".fortniteforge/backups", config.BackupDirectory);
        Assert.Contains("FortniteGame", config.ReadOnlyFolders);
        Assert.Contains("Engine", config.ReadOnlyFolders);
    }

    [Fact]
    public void ContentPath_CombinesProjectPathWithContent()
    {
        var config = new ForgeConfig { ProjectPath = @"C:\Projects\MyProject" };

        Assert.Equal(@"C:\Projects\MyProject\Content", config.ContentPath);
    }

    [Fact]
    public void Validate_EmptyProjectPath_ReturnsIssue()
    {
        var config = new ForgeConfig();

        var issues = config.Validate();

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Contains("ProjectPath"));
    }

    [Fact]
    public void Validate_NonExistentProjectPath_ReturnsIssue()
    {
        var config = new ForgeConfig { ProjectPath = @"C:\NonExistent\Path\12345" };

        var issues = config.Validate();

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Contains("does not exist"));
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"forge_test_{Guid.NewGuid():N}.json");
        try
        {
            var config = new ForgeConfig
            {
                ProjectPath = @"C:\Test\Project",
                RequireDryRun = false,
                AutoBackup = false,
                MaxBackupsPerFile = 5,
                ReadOnlyFolders = new List<string> { "FortniteGame", "Engine", "Custom" },
                ModifiableFolders = new List<string> { "MyMaps" }
            };

            config.Save(tempFile);
            var loaded = ForgeConfig.Load(tempFile);

            Assert.Equal(config.ProjectPath, loaded.ProjectPath);
            Assert.Equal(config.RequireDryRun, loaded.RequireDryRun);
            Assert.Equal(config.AutoBackup, loaded.AutoBackup);
            Assert.Equal(config.MaxBackupsPerFile, loaded.MaxBackupsPerFile);
            Assert.Equal(config.ReadOnlyFolders.Count, loaded.ReadOnlyFolders.Count);
            Assert.Equal(config.ModifiableFolders.Count, loaded.ModifiableFolders.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_NonExistentFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ForgeConfig.Load(@"C:\does_not_exist_12345.json"));
    }

    [Fact]
    public void BuildConfig_HasDefaults()
    {
        var config = new ForgeConfig();

        Assert.Equal("", config.Build.BuildCommand);
        Assert.Equal(300, config.Build.TimeoutSeconds);
    }
}
