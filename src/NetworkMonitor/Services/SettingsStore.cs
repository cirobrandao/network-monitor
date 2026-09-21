using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetworkMonitor.Services;


public sealed class AlertRule
{
    public string ProcessNameContains { get; set; } = "";
    public double MaxDownMBps { get; set; } = 5;
    public double MaxUpMBps { get; set; } = 2;
    public bool Enabled { get; set; } = true;
}
public sealed class AppSettings
{
    public bool ShowOverlay { get; set; } = true;
    public double OverlayLeft { get; set; } = double.NaN;
    public double OverlayTop { get; set; } = double.NaN;
    public double OverlayOpacity { get; set; } = 0.94;
    public double OverlayBackgroundOpacity { get; set; } = 0.97;
    public double OverlayScale { get; set; } = 1.0;
    public double OverlayWidth { get; set; } = 440;
    public double OverlayHeight { get; set; } = 128;
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
    public bool OverlayShowConnections { get; set; } = true;
    public bool OverlayShowPublicIp { get; set; } = true;
    public bool OverlayShowChart { get; set; } = true;
    public string OverlayChartType { get; set; } = "Area";
    public bool ShowChart { get; set; } = true;
    public string ChartType { get; set; } = "Area";
    public List<string> BlockedPrograms { get; set; } = [];
    public List<string> BlockedAddresses { get; set; } = [];
    public List<string> BlockedProgramAddresses { get; set; } = [];
    public int ClosedRetentionMinutes { get; set; } = 10;
    public bool PeakAlertEnabled { get; set; } = true;
    public double PeakDownMBps { get; set; } = 10;
    public double PeakUpMBps { get; set; } = 3;
    public List<string> DisabledAdapters { get; set; } = [];
    public bool FirstRun { get; set; } = true;
    public string OverlayTheme { get; set; } = "Dark";
    public List<AlertRule> AlertRules { get; set; } = [];
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
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
