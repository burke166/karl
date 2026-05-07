using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;

internal static class KarlCliConfiguration
{
    private static readonly string[] ConfigFileNames = [".karl", "karl", "karl.json"];

    public static IConfigurationRoot Load(string? explicitConfigPath = null)
    {
        var builder = new ConfigurationBuilder();
        var configFilePath = ResolveConfigFilePath(explicitConfigPath, Environment.CurrentDirectory, GetUserConfigDirectory());

        if (!string.IsNullOrWhiteSpace(configFilePath))
        {
            builder.AddJsonFile(configFilePath, optional: true, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables("KARL_");
        return builder.Build();
    }

    internal static string? ResolveConfigFilePath(
        string? explicitConfigPath,
        string currentDirectory,
        string? userConfigDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            return Path.GetFullPath(explicitConfigPath);
        }

        var cwdMatch = FindFirstConfigFile(currentDirectory);
        if (cwdMatch is not null)
        {
            return cwdMatch;
        }

        if (userConfigDirectory is null)
        {
            return null;
        }

        return FindFirstConfigFile(userConfigDirectory);
    }

    internal static string? GetUserConfigDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrWhiteSpace(userProfile)
                ? null
                : Path.Combine(userProfile, ".karl");
        }

        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfig))
        {
            return Path.Combine(xdgConfig, "karl");
        }

        var fallbackProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(fallbackProfile)
            ? null
            : Path.Combine(fallbackProfile, ".config", "karl");
    }

    private static string? FindFirstConfigFile(string directoryPath)
    {
        foreach (var name in ConfigFileNames)
        {
            var candidate = Path.Combine(directoryPath, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
