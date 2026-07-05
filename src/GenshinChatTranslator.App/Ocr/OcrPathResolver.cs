using System.IO;
using GenshinChatTranslator.App.Services;

namespace GenshinChatTranslator.App.Ocr;

internal static class OcrPathResolver
{
    public static string ResolveWorkspacePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(path)
            ? path
            : WorkspacePaths.GetContentPath(path.Replace('/', Path.DirectorySeparatorChar));
    }
}
