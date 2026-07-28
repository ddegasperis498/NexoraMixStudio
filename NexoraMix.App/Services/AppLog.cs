using IOFile = System.IO.File;
using IOPath = System.IO.Path;
using IODirectory = System.IO.Directory;

namespace NexoraMix.App.Services;

public static class AppLog
{
    public static string LogPath
    {
        get
        {
            var folder = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexoraMix",
                "Logs");
            IODirectory.CreateDirectory(folder);
            return IOPath.Combine(folder, "nexora-mix.log");
        }
    }

    public static void Error(Exception exception, string context)
    {
        try
        {
            IOFile.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 90)}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never hide the original application error.
        }
    }
}
