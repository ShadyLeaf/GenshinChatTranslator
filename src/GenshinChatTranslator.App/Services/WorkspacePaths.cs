using System.IO;

namespace GenshinChatTranslator.App.Services;

public static class WorkspacePaths
{
    private const string AppDataFolderName = "GenshinChatTranslator";

    private static readonly Lazy<string> ContentRoot = new(FindContentRoot);

    public static string FindWorkspaceFile(params string[] segments)
    {
        return FindContentFile(segments);
    }

    public static string FindContentFile(params string[] segments)
    {
        var path = Path.Combine(new[] { ContentRoot.Value }.Concat(segments).ToArray());
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find content file: {Path.Combine(segments)}", path);
        }

        return path;
    }

    public static string GetWorkspacePath(params string[] segments)
    {
        return GetContentPath(segments);
    }

    public static string GetContentPath(params string[] segments)
    {
        return Path.Combine(new[] { ContentRoot.Value }.Concat(segments).ToArray());
    }

    public static string GetUserConfigFile(string fileName)
    {
        var targetPath = Path.Combine(GetUserConfigDirectory(), fileName);
        if (!File.Exists(targetPath))
        {
            var sourcePath = FindContentFile("config", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath);
        }

        return targetPath;
    }

    public static string GetUserConfigDirectory()
    {
        return GetSpecialFolderPath(Environment.SpecialFolder.ApplicationData, "config");
    }

    public static string GetUserDataPath(params string[] segments)
    {
        var path = Path.Combine(new[] { GetSpecialFolderPath(Environment.SpecialFolder.LocalApplicationData) }.Concat(segments).ToArray());
        var directory = Path.HasExtension(path) ? Path.GetDirectoryName(path) : path;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return path;
    }

    private static string GetSpecialFolderPath(Environment.SpecialFolder folder, params string[] segments)
    {
        var root = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        var pathSegments = new[] { root, AppDataFolderName }.Concat(segments).ToArray();
        var path = Path.Combine(pathSegments);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindContentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var configCandidate = Path.Combine(directory.FullName, "config", "roi_detection.yml");
            if (File.Exists(configCandidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find application content root containing config/roi_detection.yml.");
    }
}
