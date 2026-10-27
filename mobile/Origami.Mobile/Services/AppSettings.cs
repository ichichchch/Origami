using System.Text.Json;

namespace Origami.Mobile.Services;

/// <summary>
/// App settings persisted as JSON under the platform app-data directory.
/// The default targets a local Api host on port 5100; the Android emulator reaches the host
/// machine's loopback through 10.0.2.2 instead.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string BaseUrl { get; set; } = "http://localhost:5100";

    public static AppSettings Load()
    {
        try
        {
            var path = PathForSettings();
            if (!File.Exists(path)) return new AppSettings();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
            return string.IsNullOrWhiteSpace(loaded?.BaseUrl) ? new AppSettings() : loaded;
        }
        catch
        {
            // A corrupt settings file falls back to defaults rather than blocking startup.
            return new AppSettings();
        }
    }

    public void Save()
    {
        var path = PathForSettings();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static string PathForSettings()
    {
        // ApplicationData works on desktop platforms; mobile falls back to the writable app directory.
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
        return Path.Combine(root, "Origami.Mobile", "settings.json");
    }
}
