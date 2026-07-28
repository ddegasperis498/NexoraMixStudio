using System.IO;
using System.Text.Json;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;
using IODirectory = System.IO.Directory;

namespace NexoraMix.App.Services;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string SpotifyClientId { get; set; } = string.Empty;
    public int TransitionBeats { get; set; } = 16;
    public double MaximumSyncPercent { get; set; } = 25d;
    public bool BassSwapEnabled { get; set; } = true;
    public double WindowWidth { get; set; } = 1680d;
    public double WindowHeight { get; set; } = 980d;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WindowMaximized { get; set; }

    public static string SettingsPath
    {
        get
        {
            var folder = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexoraMix",
                "Settings");
            IODirectory.CreateDirectory(folder);
            return IOPath.Combine(folder, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        var path = SettingsPath;
        if (!IOFile.Exists(path))
        {
            var legacyPath = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexoraMix",
                "settings.json");
            if (IOFile.Exists(legacyPath)) path = legacyPath;
            else return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(IOFile.ReadAllText(path), JsonOptions)
                   ?? new AppSettings();
        }
        catch (JsonException)
        {
            PreserveCorruptFile(path);
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var target = SettingsPath;
        var temporary = target + ".tmp";
        var backup = target + ".bak";
        var json = JsonSerializer.Serialize(this, JsonOptions);

        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 8 * 1024,
                       options: FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (IOFile.Exists(target))
            {
                try
                {
                    IOFile.Replace(temporary, target, backup, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    IOFile.Copy(target, backup, overwrite: true);
                    IOFile.Move(temporary, target, overwrite: true);
                }
                catch (IOException)
                {
                    IOFile.Copy(target, backup, overwrite: true);
                    IOFile.Move(temporary, target, overwrite: true);
                }
            }
            else
            {
                IOFile.Move(temporary, target);
            }
        }
        finally
        {
            if (IOFile.Exists(temporary)) IOFile.Delete(temporary);
        }
    }

    private static void PreserveCorruptFile(string path)
    {
        try
        {
            var corruptPath = path + $".corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
            IOFile.Copy(path, corruptPath, overwrite: false);
        }
        catch (IOException)
        {
            // Il file originale resta intatto; l'app prosegue con impostazioni predefinite.
        }
        catch (UnauthorizedAccessException)
        {
            // Il file originale resta intatto; l'app prosegue con impostazioni predefinite.
        }
    }
}
