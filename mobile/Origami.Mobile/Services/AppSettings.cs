using System.Text.Json;

namespace Origami.Mobile.Services;

/// <summary>
/// 以 JSON 保存於平台應用程式資料目錄的應用程式設定。
/// 預設指向本機 5100 連接埠的 Api 主機；Android 模擬器則改透過
/// 10.0.2.2 連至主機的回送位址。
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
            // 設定檔損毀時改回預設值，不阻擋啟動。
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
        // ApplicationData 在桌面平台可用；行動平台改用可寫入的應用程式目錄。
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
        return Path.Combine(root, "Origami.Mobile", "settings.json");
    }
}
