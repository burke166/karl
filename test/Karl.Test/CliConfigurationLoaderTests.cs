namespace Karl.Test;

public class CliConfigurationLoaderTests
{
    [Fact]
    public void ResolveConfigFilePath_PrefersCurrentDirectoryOverUserDirectory()
    {
        using var currentDir = TemporaryDirectory.Create();
        using var userDir = TemporaryDirectory.Create();
        var localConfig = System.IO.Path.Combine(currentDir.Path, "karl.json");
        var userConfig = System.IO.Path.Combine(userDir.Path, "karl.json");
        File.WriteAllText(localConfig, "{}");
        File.WriteAllText(userConfig, "{}");

        var resolved = KarlCliConfiguration.ResolveConfigFilePath(null, currentDir.Path, userDir.Path);

        Assert.Equal(localConfig, resolved);
    }

    [Fact]
    public void ResolveConfigFilePath_UsesUserDirectoryWhenNoLocalConfigExists()
    {
        using var currentDir = TemporaryDirectory.Create();
        using var userDir = TemporaryDirectory.Create();
        var userConfig = System.IO.Path.Combine(userDir.Path, "karl.json");
        File.WriteAllText(userConfig, "{}");

        var resolved = KarlCliConfiguration.ResolveConfigFilePath(null, currentDir.Path, userDir.Path);

        Assert.Equal(userConfig, resolved);
    }

    [Fact]
    public void ResolveConfigFilePath_SupportsExpectedFilenamesInPriorityOrder()
    {
        using var currentDir = TemporaryDirectory.Create();
        var dotKarl = System.IO.Path.Combine(currentDir.Path, ".karl");
        var karl = System.IO.Path.Combine(currentDir.Path, "karl");
        var json = System.IO.Path.Combine(currentDir.Path, "karl.json");
        File.WriteAllText(json, "{}");
        File.WriteAllText(karl, "{}");
        File.WriteAllText(dotKarl, "{}");

        var resolved = KarlCliConfiguration.ResolveConfigFilePath(null, currentDir.Path, null);

        Assert.Equal(dotKarl, resolved);
    }

    [Fact]
    public void Load_ReadsKARLEnvironmentVariables()
    {
        const string key = "KARL_Karl__Smtp__Host";
        var previous = Environment.GetEnvironmentVariable(key);

        try
        {
            Environment.SetEnvironmentVariable(key, "smtp.env.example.com");

            var configuration = KarlCliConfiguration.Load();

            Assert.Equal("smtp.env.example.com", configuration["Karl:Smtp:Host"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    [Fact]
    public void Load_HandlesMissingConfigFilesGracefully()
    {
        using var currentDir = TemporaryDirectory.Create();
        var originalCurrentDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = currentDir.Path;

            var configuration = KarlCliConfiguration.Load();

            Assert.NotNull(configuration);
            Assert.Null(configuration["Karl:Smtp:Host"]);
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Fact]
    public void Load_RestoresCurrentDirectoryAfterTest()
    {
        using var currentDir = TemporaryDirectory.Create();
        var originalCurrentDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = currentDir.Path;
            _ = KarlCliConfiguration.Load();
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }

        Assert.Equal(originalCurrentDirectory, Environment.CurrentDirectory);
    }
}
