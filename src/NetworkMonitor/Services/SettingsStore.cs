using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetworkMonitor.Services;

public sealed class AppSettings
{
    public bool ShowOverlay { get; set; } = true;
    public double OverlayLeft { get; set; } = double.NaN;
    public double OverlayTop { get; set; } = double.NaN;
    public double OverlayOpacity { get; set; } = 0.94;
    public bool OverlayClickThrough { get; set; }
    public bool OverlayCompact { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public int RefreshMs { get; set; } = 1000;
    public bool ShowUdp { get; set; }
    public bool EstablishedOnly { get; set; } = true;
    public bool HideLoopback { get; set; } = true;
    public bool HidePrivate { get; set; }
    public bool ResolveDns { get; set; } = true;
    public bool ShowPublicIp { get; set; } = true;
    public int ClosedRetentionMinutes { get; set; } = 10;
    public bool PeakAlertEnabled { get; set; } = true;
    public double PeakDownMBps { get; set; } = 10;
    public double PeakUpMBps { get; set; } = 3;
    public List<string> DisabledAdapters { get; set; } = [];
    public bool FirstRun { get; set; } = true;
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string FolderPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetworkMonitor");

    public static string FilePath => Path.Combine(FolderPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(FolderPath);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
