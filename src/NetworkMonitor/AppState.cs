using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using NetworkMonitor.Services;

namespace NetworkMonitor;

public sealed class AppState : ObservableObject
{
    public static AppState Current { get; } = new();

    private readonly HashSet<string> _expandedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrackedLive> _live = new(StringComparer.Ordinal);
    private readonly List<ClosedRecord> _closed = [];
    private readonly double[] _downHist = new double[60];
    private readonly double[] _upHist = new double[60];
    private int _histCount;
    private bool _persistReady;
    private bool _wasPeaking;
    private readonly Dictionary<string, GeoInfo> _geo = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, Rates> _bwByConn = TrafficSnapshot.Empty.ByConnection;
    private IReadOnlyDictionary<int, Rates> _bwByPid = TrafficSnapshot.Empty.ByPid;
    private IReadOnlyDictionary<string, Rates> _bwByRemote = TrafficSnapshot.Empty.ByRemote;
    private IReadOnlyList<ProcessBandwidthItem> _topBw = Array.Empty<ProcessBandwidthItem>();
    private string? _flashStatus;
    private DateTime _flashUntil;
    private bool _updateAvailable;
    private string _updateLabel = "";
    private string _search = "";
    private ViewMode _viewMode = ViewMode.Processes;
    private bool _settingsOpen;
    private double _downBps;
    private double _upBps;
    private string _downText = "0 B/s";
    private string _upText = "0 B/s";
    private PointCollection _downSpark = new();
    private PointCollection _upSpark = new();
    private IReadOnlyList<NetConnection> _connections = [];
    private IReadOnlyList<ProcessGroup> _processes = [];
    private IReadOnlyList<IpGroup> _addresses = [];
    private IReadOnlyList<ClosedConnection> _closedConnections = [];
    private IReadOnlyList<AdapterOption> _adapters = [];
    private string _statusText = "Capturando conexões…";
    private int _connectionCount;
    private int _processCount;
    private int _addressCount;
    private string _publicIp = "—";
    private string _adapterName = "—";
    private string _internalIp = "—";
    private string _downTotalText = "0 B";
    private string _upTotalText = "0 B";
    private bool _isPeaking;
    private string _peakText = "";
    private IReadOnlyList<double> _downHistory = [];
    private IReadOnlyList<double> _upHistory = [];
    private IReadOnlyList<DnsServerRow> _dnsResults = [];
    private IReadOnlyList<BlockRule> _blockRules = [];
    private string _dnsHosts = "google.com, cloudflare.com, microsoft.com, cirobrandao.com.br";
    private string _dnsStatus = "Clique em Testar DNS para medir os servidores.";
    private bool _dnsBusy;
    private List<NetConnection> _raw = [];

    public AppSettings Settings { get; private set; } = new();

    public event Action? OverlayVisibilityChanged;
    public event Action? OverlayStyleChanged;
    public event Action? RefreshIntervalChanged;
    public event Action? ThemeChanged;
    public event Action<string>? PeakRaised;

    public double DownBps { get => _downBps; private set => Set(ref _downBps, value); }
    public double UpBps { get => _upBps; private set => Set(ref _upBps, value); }
    public string DownText { get => _downText; private set => Set(ref _downText, value); }
    public string UpText { get => _upText; private set => Set(ref _upText, value); }
    public PointCollection DownSpark { get => _downSpark; private set => Set(ref _downSpark, value); }
    public PointCollection UpSpark { get => _upSpark; private set => Set(ref _upSpark, value); }
    public IReadOnlyList<NetConnection> Connections { get => _connections; private set => Set(ref _connections, value); }
    public IReadOnlyList<ProcessGroup> Processes { get => _processes; private set => Set(ref _processes, value); }
    public IReadOnlyList<IpGroup> Addresses { get => _addresses; private set => Set(ref _addresses, value); }
    public IReadOnlyList<ClosedConnection> ClosedConnections { get => _closedConnections; private set => Set(ref _closedConnections, value); }
    public IReadOnlyList<AdapterOption> Adapters { get => _adapters; private set => Set(ref _adapters, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public int ConnectionCount { get => _connectionCount; private set => Set(ref _connectionCount, value); }
    public int ProcessCount { get => _processCount; private set => Set(ref _processCount, value); }
    public int AddressCount { get => _addressCount; private set => Set(ref _addressCount, value); }
    public string ConnectionCountText => $"{ConnectionCount} conexões";
    public string PublicIp { get => _publicIp; private set => Set(ref _publicIp, value); }
    public string AdapterName { get => _adapterName; private set => Set(ref _adapterName, value); }
    public string InternalIp { get => _internalIp; private set => Set(ref _internalIp, value); }
    public string DownTotalText { get => _downTotalText; private set => Set(ref _downTotalText, value); }
    public string UpTotalText { get => _upTotalText; private set => Set(ref _upTotalText, value); }
    public bool IsPeaking
    {
        get => _isPeaking;
        private set
        {
            if (!Set(ref _isPeaking, value)) return;
            Raise(nameof(OverlayBackgroundBrush));
        }
    }
    public string PeakText { get => _peakText; private set => Set(ref _peakText, value); }

    public IReadOnlyList<ProcessBandwidthItem> TopProcessBandwidth
    {
        get => _topBw;
        private set => Set(ref _topBw, value);
    }

    public string TopProcessBandwidthText =>
        _topBw.Count == 0 ? string.Empty : string.Join(" | ", _topBw.Take(3).Select(x => x.Label));

    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set => Set(ref _updateAvailable, value);
    }

    public string UpdateLabel
    {
        get => _updateLabel;
        private set => Set(ref _updateLabel, value);
    }

    public bool OverlayThemeIsLight => IsLightTheme;

    public bool IsLightTheme => Theme.ResolveLight(Settings.Theme);

    public bool ThemeIsSystem
    {
        get => string.Equals(Settings.Theme, "System", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(Settings.Theme);
        set { if (value) SetTheme("System"); }
    }

    public bool ThemeIsLight
    {
        get => string.Equals(Settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        set { if (value) SetTheme("Light"); }
    }

    public bool ThemeIsDark
    {
        get => string.Equals(Settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
        set { if (value) SetTheme("Dark"); }
    }

    public Brush OverlayForegroundBrush => OverlayThemeIsLight
        ? new SolidColorBrush(Color.FromRgb(0x12, 0x34, 0x4D))
        : new SolidColorBrush(Color.FromRgb(0xEC, 0xF2, 0xF8));

    public Brush OverlayMutedBrush => OverlayThemeIsLight
        ? new SolidColorBrush(Color.FromRgb(0x5C, 0x67, 0x73))
        : new SolidColorBrush(Color.FromRgb(0x9A, 0xA7, 0xB5));

    public IReadOnlyList<double> DownHistory { get => _downHistory; private set => Set(ref _downHistory, value); }
    public IReadOnlyList<double> UpHistory { get => _upHistory; private set => Set(ref _upHistory, value); }
    public IReadOnlyList<DnsServerRow> DnsResults { get => _dnsResults; private set => Set(ref _dnsResults, value); }
    public IReadOnlyList<BlockRule> BlockRules { get => _blockRules; private set => Set(ref _blockRules, value); }
    public string DnsStatus { get => _dnsStatus; private set => Set(ref _dnsStatus, value); }
    public bool DnsBusy
    {
        get => _dnsBusy;
        private set
        {
            if (Set(ref _dnsBusy, value))
                Raise(nameof(DnsIdle));
        }
    }
    public bool DnsIdle => !DnsBusy;
    public bool IsElevated { get; } = FirewallService.IsElevated;
    public string ElevationText => IsElevated ? "Executando como administrador" : "Sem administrador — o bloqueio pedirá elevação (UAC) quando necessário";
    public bool OverlayComplete
    {
        get => !OverlayCompact;
        set => OverlayCompact = !value;
    }
    public double OverlayWidthFixed => OverlayCompact ? 280 : 240;
    public double OverlayHeightFixed => OverlayCompact ? 48 : 300;
    public Brush OverlayBackgroundBrush
    {
        get
        {
            var baseColor = IsPeaking
                ? (OverlayThemeIsLight ? Color.FromRgb(0xFF, 0xF8, 0xF4) : Color.FromRgb(0x5A, 0x22, 0x18))
                : (OverlayThemeIsLight ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x16, 0x1B, 0x22));
            var alpha = (byte)Math.Round(Math.Clamp(Settings.OverlayBackgroundOpacity, 0, 1) * 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            brush.Freeze();
            return brush;
        }
    }

    public string DnsHosts
    {
        get => _dnsHosts;
        set => Set(ref _dnsHosts, value ?? "");
    }

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? ""))
                RebuildViews();
        }
    }

    public ViewMode ViewMode
    {
        get => _viewMode;
        set => Set(ref _viewMode, value);
    }

    public bool SettingsOpen
    {
        get => _settingsOpen;
        set => Set(ref _settingsOpen, value);
    }

    public bool ShowOverlay
    {
        get => Settings.ShowOverlay;
        set
        {
            if (Settings.ShowOverlay == value) return;
            Settings.ShowOverlay = value;
            Raise();
            OverlayVisibilityChanged?.Invoke();
            Persist();
        }
    }

    public bool OverlayClickThrough
    {
        get => Settings.OverlayClickThrough;
        set
        {
            if (Settings.OverlayClickThrough == value) return;
            Settings.OverlayClickThrough = value;
            Raise();
            OverlayStyleChanged?.Invoke();
            Persist();
        }
    }

    public bool OverlayCompact
    {
        get => Settings.OverlayCompact;
        set
        {
            if (Settings.OverlayCompact == value) return;
            Settings.OverlayCompact = value;
            Raise();
            Raise(nameof(OverlayComplete));
            Raise(nameof(OverlayWidthFixed));
            Raise(nameof(OverlayHeightFixed));
            OverlayStyleChanged?.Invoke();
            Persist();
        }
    }

    public double OverlayBackgroundOpacity
    {
        get => Settings.OverlayBackgroundOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(Settings.OverlayBackgroundOpacity - clamped) < 0.001) return;
            Settings.OverlayBackgroundOpacity = clamped;
            Raise();
            Raise(nameof(OverlayBackgroundBrush));
            Persist();
        }
    }

    public double OverlayOpacity
    {
        get => Settings.OverlayOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0.3, 1);
            if (Math.Abs(Settings.OverlayOpacity - clamped) < 0.001) return;
            Settings.OverlayOpacity = clamped;
            Raise();
            OverlayStyleChanged?.Invoke();
            Persist();
        }
    }

    public bool StartWithWindows
    {
        get => Settings.StartWithWindows;
        set
        {
            if (Settings.StartWithWindows == value) return;
            Settings.StartWithWindows = value;
            StartupManager.Apply(value);
            Raise();
            Persist();
        }
    }

    public bool StartMinimized
    {
        get => Settings.StartMinimized;
        set
        {
            if (Settings.StartMinimized == value) return;
            Settings.StartMinimized = value;
            Raise();
            Persist();
        }
    }

    public bool ShowUdp
    {
        get => Settings.ShowUdp;
        set
        {
            if (Settings.ShowUdp == value) return;
            Settings.ShowUdp = value;
            Raise();
            Persist();
            RebuildViews();
        }
    }

    public bool EstablishedOnly
    {
        get => Settings.EstablishedOnly;
        set
        {
            if (Settings.EstablishedOnly == value) return;
            Settings.EstablishedOnly = value;
            Raise();
            Persist();
            RebuildViews();
        }
    }

    public bool HideLoopback
    {
        get => Settings.HideLoopback;
        set
        {
            if (Settings.HideLoopback == value) return;
            Settings.HideLoopback = value;
            Raise();
            Persist();
            RebuildViews();
        }
    }

    public bool HidePrivate
    {
        get => Settings.HidePrivate;
        set
        {
            if (Settings.HidePrivate == value) return;
            Settings.HidePrivate = value;
            Raise();
            Persist();
            RebuildViews();
        }
    }

    public bool ResolveDns
    {
        get => Settings.ResolveDns;
        set
        {
            if (Settings.ResolveDns == value) return;
            Settings.ResolveDns = value;
            Raise();
            Persist();
        }
    }

    public bool ShowPublicIp
    {
        get => Settings.ShowPublicIp;
        set
        {
            if (Settings.ShowPublicIp == value) return;
            Settings.ShowPublicIp = value;
            Raise();
            Persist();
        }
    }

    public ChartKind OverlayChartType
    {
        get => Enum.TryParse<ChartKind>(Settings.OverlayChartType, true, out var kind) ? kind : ChartKind.Area;
        set
        {
            if (OverlayChartType == value) return;
            Settings.OverlayChartType = value.ToString();
            Raise();
            Raise(nameof(OverlayChartLine));
            Raise(nameof(OverlayChartArea));
            Raise(nameof(OverlayChartBar));
            Persist();
        }
    }

    public bool OverlayChartLine
    {
        get => OverlayChartType == ChartKind.Line;
        set { if (value) OverlayChartType = ChartKind.Line; }
    }

    public bool OverlayChartArea
    {
        get => OverlayChartType == ChartKind.Area;
        set { if (value) OverlayChartType = ChartKind.Area; }
    }

    public bool OverlayChartBar
    {
        get => OverlayChartType == ChartKind.Bar;
        set { if (value) OverlayChartType = ChartKind.Bar; }
    }

    public bool ShowChart
    {
        get => Settings.ShowChart;
        set
        {
            if (Settings.ShowChart == value) return;
            Settings.ShowChart = value;
            Raise();
            Persist();
        }
    }

    public ChartKind ChartType
    {
        get => Enum.TryParse<ChartKind>(Settings.ChartType, true, out var kind) ? kind : ChartKind.Area;
        set
        {
            if (ChartType == value) return;
            Settings.ChartType = value.ToString();
            Raise();
            Raise(nameof(ChartLine));
            Raise(nameof(ChartArea));
            Raise(nameof(ChartBar));
            Persist();
        }
    }

    public bool ChartLine
    {
        get => ChartType == ChartKind.Line;
        set { if (value) ChartType = ChartKind.Line; }
    }

    public bool ChartArea
    {
        get => ChartType == ChartKind.Area;
        set { if (value) ChartType = ChartKind.Area; }
    }

    public bool ChartBar
    {
        get => ChartType == ChartKind.Bar;
        set { if (value) ChartType = ChartKind.Bar; }
    }

    public bool PeakAlertEnabled
    {
        get => Settings.PeakAlertEnabled;
        set
        {
            if (Settings.PeakAlertEnabled == value) return;
            Settings.PeakAlertEnabled = value;
            Raise();
            Persist();
        }
    }

    public string PeakDownText
    {
        get => Settings.PeakDownMBps.ToString("0.##", CultureInfo.CurrentCulture);
        set
        {
            if (!TryParseRate(value, out var parsed)) return;
            Settings.PeakDownMBps = parsed;
            Raise();
            Persist();
        }
    }

    public string PeakUpText
    {
        get => Settings.PeakUpMBps.ToString("0.##", CultureInfo.CurrentCulture);
        set
        {
            if (!TryParseRate(value, out var parsed)) return;
            Settings.PeakUpMBps = parsed;
            Raise();
            Persist();
        }
    }

    public bool Retention5
    {
        get => Settings.ClosedRetentionMinutes == 5;
        set { if (value) ClosedRetentionMinutes = 5; }
    }

    public bool Retention10
    {
        get => Settings.ClosedRetentionMinutes is 10 or 0;
        set { if (value) ClosedRetentionMinutes = 10; }
    }

    public bool Retention30
    {
        get => Settings.ClosedRetentionMinutes == 30;
        set { if (value) ClosedRetentionMinutes = 30; }
    }

    public int ClosedRetentionMinutes
    {
        get => Settings.ClosedRetentionMinutes;
        set
        {
            if (Settings.ClosedRetentionMinutes == value) return;
            Settings.ClosedRetentionMinutes = value;
            Raise();
            Raise(nameof(Retention5));
            Raise(nameof(Retention10));
            Raise(nameof(Retention30));
            Persist();
        }
    }

    public int RefreshMs
    {
        get => Settings.RefreshMs;
        set
        {
            if (Settings.RefreshMs == value) return;
            Settings.RefreshMs = value;
            Raise();
            Raise(nameof(RefreshFast));
            Raise(nameof(RefreshNormal));
            Raise(nameof(RefreshSlow));
            RefreshIntervalChanged?.Invoke();
            Persist();
        }
    }

    public bool RefreshFast
    {
        get => Settings.RefreshMs == 500;
        set { if (value) RefreshMs = 500; }
    }

    public bool RefreshNormal
    {
        get => Settings.RefreshMs is 1000 or 0;
        set { if (value) RefreshMs = 1000; }
    }

    public bool RefreshSlow
    {
        get => Settings.RefreshMs == 2000;
        set { if (value) RefreshMs = 2000; }
    }

    public void Load()
    {
        Settings = SettingsStore.Load();
        if (Settings.RefreshMs is not (500 or 1000 or 2000))
            Settings.RefreshMs = 1000;
        if (Settings.ClosedRetentionMinutes is not (5 or 10 or 30))
            Settings.ClosedRetentionMinutes = 10;
        if (Settings.PeakDownMBps < 0) Settings.PeakDownMBps = 10;
        if (Settings.PeakUpMBps < 0) Settings.PeakUpMBps = 3;
        Settings.BlockedPrograms ??= [];
        Settings.BlockedAddresses ??= [];
        Settings.BlockedProgramAddresses ??= [];
        if (string.IsNullOrWhiteSpace(Settings.ChartType))
            Settings.ChartType = "Area";
        if (string.IsNullOrWhiteSpace(Settings.OverlayChartType))
            Settings.OverlayChartType = "Area";
        if (string.IsNullOrWhiteSpace(Settings.Theme)
            || Settings.Theme is not ("System" or "Light" or "Dark"))
            Settings.Theme = "System";
        StartupManager.Apply(Settings.StartWithWindows);
        _persistReady = true;
        Raise(nameof(ShowOverlay));
        Raise(nameof(OverlayClickThrough));
        Raise(nameof(OverlayCompact));
        Raise(nameof(OverlayOpacity));
        Raise(nameof(StartWithWindows));
        Raise(nameof(StartMinimized));
        Raise(nameof(ShowUdp));
        Raise(nameof(EstablishedOnly));
        Raise(nameof(HideLoopback));
        Raise(nameof(HidePrivate));
        Raise(nameof(ResolveDns));
        Raise(nameof(ShowPublicIp));
        Raise(nameof(OverlayComplete));
        Raise(nameof(OverlayChartType));
        Raise(nameof(OverlayChartLine));
        Raise(nameof(OverlayChartArea));
        Raise(nameof(OverlayChartBar));
        Raise(nameof(ShowChart));
        Raise(nameof(ChartType));
        Raise(nameof(ChartLine));
        Raise(nameof(ChartArea));
        Raise(nameof(ChartBar));
        Raise(nameof(IsElevated));
        Raise(nameof(ElevationText));
        RefreshBlockRules();
        Raise(nameof(PeakAlertEnabled));
        Raise(nameof(PeakDownText));
        Raise(nameof(PeakUpText));
        Raise(nameof(ClosedRetentionMinutes));
        Raise(nameof(Retention5));
        Raise(nameof(Retention10));
        Raise(nameof(Retention30));
        Raise(nameof(RefreshMs));
        Raise(nameof(RefreshFast));
        Raise(nameof(RefreshNormal));
        Raise(nameof(RefreshSlow));
        RaiseTheme();
    }

    private void SetTheme(string theme)
    {
        if (string.Equals(Settings.Theme, theme, StringComparison.OrdinalIgnoreCase))
            return;
        Settings.Theme = theme;
        Persist();
        Theme.Apply(theme);
        RaiseTheme();
        OverlayStyleChanged?.Invoke();
        ThemeChanged?.Invoke();
    }

    public void ApplySystemThemeIfNeeded()
    {
        if (!ThemeIsSystem)
            return;
        Theme.Apply("System");
        RaiseTheme();
        OverlayStyleChanged?.Invoke();
        ThemeChanged?.Invoke();
    }

    private void RaiseTheme()
    {
        Raise(nameof(ThemeIsSystem));
        Raise(nameof(ThemeIsLight));
        Raise(nameof(ThemeIsDark));
        Raise(nameof(IsLightTheme));
        Raise(nameof(OverlayThemeIsLight));
        Raise(nameof(OverlayBackgroundBrush));
        Raise(nameof(OverlayForegroundBrush));
        Raise(nameof(OverlayMutedBrush));
    }

    public void SetUpdateAvailable(string? versionLabel)
    {
        if (string.IsNullOrWhiteSpace(versionLabel))
        {
            UpdateAvailable = false;
            UpdateLabel = "";
            return;
        }
        UpdateAvailable = true;
        UpdateLabel = "Nova versão " + versionLabel;
    }

    public void FlashStatus(string message)
    {
        _flashStatus = message;
        _flashUntil = DateTime.UtcNow.AddSeconds(2.5);
        StatusText = message;
    }

    internal void RefreshViews() => RebuildViews();

    public void SetPublicIp(string? ip)
    {
        PublicIp = string.IsNullOrWhiteSpace(ip) ? "—" : ip;
    }

    internal void ApplyTraffic(TrafficSnapshot traffic)
    {
        _bwByConn = traffic.ByConnection;
        _bwByPid = traffic.ByPid;
        _bwByRemote = traffic.ByRemote;
        TopProcessBandwidth = traffic.TopProcesses.Select(r => new ProcessBandwidthItem
        {
            Name = r.Name,
            Label = r.Label,
            DownBps = r.DownBps,
            UpBps = r.UpBps
        }).ToList();
        Raise(nameof(TopProcessBandwidthText));
        UpdatePeaks();
    }

    internal void RequestGeo(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || GeoIpService.IsNonPublic(ip)) return;
        if (_geo.ContainsKey(ip)) return;
        var cached = GeoIpService.TryGetCached(ip);
        if (cached is not null)
        {
            _geo[ip] = cached;
            FlagImages.Prefetch(cached.CountryCode);
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var info = await GeoIpService.LookupAsync(ip).ConfigureAwait(false);
                if (info is null) return;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _geo[ip] = info;
                    FlagImages.Prefetch(info.CountryCode);
                    RebuildViews();
                });
            }
            catch { }
        });
    }

    internal void ApplySnapshot(BandwidthSnapshot bandwidth, List<NetConnection> connections, TrafficSnapshot? traffic = null)
    {
        if (traffic is not null)
            ApplyTraffic(traffic);
        DownBps = bandwidth.DownBps;
        UpBps = bandwidth.UpBps;
        DownText = Format.Rate(DownBps);
        UpText = Format.Rate(UpBps);
        DownTotalText = Format.Bytes(bandwidth.DownBytes);
        UpTotalText = Format.Bytes(bandwidth.UpBytes);
        AdapterName = string.IsNullOrWhiteSpace(bandwidth.AdapterName) ? "—" : bandwidth.AdapterName;
        InternalIp = string.IsNullOrWhiteSpace(bandwidth.InternalIp) ? "—" : bandwidth.InternalIp;
        PushHistory(DownBps, UpBps);
        UpdatePeaks();
        SyncAdapters(bandwidth.Adapters);
        TrackClosed(connections);
        _raw = connections;
        RebuildViews();
        Raise(nameof(ConnectionCountText));
    }

    public void OnAdapterEnabledChanged()
    {
        Settings.DisabledAdapters = Adapters.Where(a => !a.Enabled).Select(a => a.Id).ToList();
        Persist();
    }

    public void SaveOverlayPosition(double left, double top)
    {
        Settings.OverlayLeft = left;
        Settings.OverlayTop = top;
        Persist();
    }

    public void MarkFirstRunComplete()
    {
        if (!Settings.FirstRun) return;
        Settings.FirstRun = false;
        Persist();
    }

    public string ToggleBlockProgram(ProcessGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.Path))
            return "Este processo não tem caminho de executável.";
        return ToggleBlocked(Settings.BlockedPrograms, group.Path, block => FirewallService.BlockProgram(block), FirewallService.UnblockProgram);
    }

    public string ToggleBlockAddress(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip == "—")
            return "IP inválido.";
        return ToggleBlocked(Settings.BlockedAddresses, ip, FirewallService.BlockAddress, FirewallService.UnblockAddress);
    }

    public string ToggleBlockProgramAddress(string? path, string ip)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Este processo não tem caminho de executável.";
        if (string.IsNullOrWhiteSpace(ip) || ip == "—")
            return "IP inválido.";
        var key = path + "|" + ip;
        return ToggleBlocked(
            Settings.BlockedProgramAddresses,
            key,
            _ => FirewallService.BlockProgramAddress(path, ip),
            _ => FirewallService.UnblockProgramAddress(path, ip));
    }

    public string RemoveBlock(BlockRule rule)
    {
        if (rule.Kind == "App")
            return ToggleBlocked(Settings.BlockedPrograms, rule.Key, FirewallService.BlockProgram, FirewallService.UnblockProgram);
        if (rule.Kind == "IP")
            return ToggleBlocked(Settings.BlockedAddresses, rule.Key, FirewallService.BlockAddress, FirewallService.UnblockAddress);
        var parts = rule.Key.Split('|');
        if (parts.Length != 2)
            return "Regra inválida.";
        return ToggleBlocked(
            Settings.BlockedProgramAddresses,
            rule.Key,
            _ => FirewallService.BlockProgramAddress(parts[0], parts[1]),
            _ => FirewallService.UnblockProgramAddress(parts[0], parts[1]));
    }

    public async Task RunDnsTestAsync()
    {
        if (DnsBusy)
            return;
        DnsBusy = true;
        DnsStatus = "Testando servidores DNS…";
        var hosts = DnsHosts.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0)
            hosts = ["google.com"];
        try
        {
            var servers = DnsProbeService.DefaultServers();
            var rows = new List<DnsServerRow>();
            foreach (var server in servers)
            {
                var result = await DnsProbeService.ProbeServerAsync(server, hosts, CancellationToken.None);
                rows.Add(new DnsServerRow
                {
                    Name = result.Name,
                    Address = result.Address,
                    AverageText = result.AverageText,
                    Status = result.Status,
                    SamplesText = string.Join("   ", result.Samples.Select(s => $"{s.Host} {s.Result}"))
                });
            }
            DnsResults = rows.OrderBy(r => r.AverageText == "—" ? 99999 : 0)
                .ThenBy(r => r.AverageText)
                .ToList();
            var best = DnsResults.FirstOrDefault(r => r.AverageText != "—");
            DnsStatus = best is null
                ? "Nenhum servidor respondeu."
                : $"Mais rápido: {best.Name} ({best.Address}) — {best.AverageText}";
        }
        catch (Exception ex)
        {
            DnsStatus = "Falha no teste: " + ex.Message;
        }
        finally
        {
            DnsBusy = false;
        }
    }

    private string ToggleBlocked(
        List<string> list,
        string key,
        Func<string, bool> block,
        Func<string, bool> unblock)
    {
        // Elevação UAC sob demanda ocorre dentro do FirewallService (Verb=runas).

        var exists = list.Contains(key, StringComparer.OrdinalIgnoreCase);
        var ok = exists ? unblock(key) : block(key);
        if (!ok)
            return FirewallService.LastError ?? "Não foi possível alterar a regra do firewall.";

        if (exists)
            list.RemoveAll(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
        else
            list.Add(key);
        Persist();
        RefreshBlockRules();
        RebuildViews();
        return exists ? "Bloqueio removido." : "Bloqueio aplicado. Conexões já abertas podem continuar até fecharem.";
    }

    private void RefreshBlockRules()
    {
        var rules = new List<BlockRule>();
        foreach (var path in Settings.BlockedPrograms)
        {
            rules.Add(new BlockRule
            {
                Kind = "App",
                Key = path,
                Label = "App · " + (System.IO.Path.GetFileName(path) ?? path)
            });
        }
        foreach (var ip in Settings.BlockedAddresses)
            rules.Add(new BlockRule { Kind = "IP", Key = ip, Label = "IP · " + ip });
        foreach (var item in Settings.BlockedProgramAddresses)
        {
            var parts = item.Split('|');
            var name = parts.Length > 0 ? System.IO.Path.GetFileName(parts[0]) : item;
            var ip = parts.Length > 1 ? parts[1] : "";
            rules.Add(new BlockRule { Kind = "AppIP", Key = item, Label = $"{name} → {ip}" });
        }
        BlockRules = rules;
    }

    public void ExportCsv(string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Protocolo,Aplicativo,PID,Caminho,Local,Remoto,Host,Estado,Escopo");
        foreach (var c in Connections)
        {
            writer.WriteLine(string.Join(',',
                Csv(c.ProtocolText),
                Csv(c.ProcessName),
                c.Pid.ToString(CultureInfo.InvariantCulture),
                Csv(c.ProcessPath ?? ""),
                Csv(c.LocalDisplay),
                Csv(c.RemoteDisplay),
                Csv(c.HostName ?? ""),
                Csv(Format.State(c.State)),
                Csv(Format.Scope(c.Scope))));
        }
    }

    private void TrackClosed(List<NetConnection> connections)
    {
        var now = DateTime.UtcNow;
        var incoming = new Dictionary<string, NetConnection>(StringComparer.Ordinal);
        foreach (var connection in connections)
        {
            if (connection.Protocol != NetProtocol.Tcp || connection.State != "ESTABLISHED")
                continue;
            if (connection.RemoteAddress.Length == 0)
                continue;
            incoming[connection.Key] = connection;
        }

        if (_live.Count > 0)
        {
            foreach (var key in _live.Keys.ToList())
            {
                if (incoming.ContainsKey(key))
                    continue;
                var tracked = _live[key];
                _closed.Add(new ClosedRecord(tracked.Connection, tracked.FirstSeen, now));
                _live.Remove(key);
            }
        }

        foreach (var pair in incoming)
        {
            if (_live.TryGetValue(pair.Key, out var existing))
                _live[pair.Key] = existing with { Connection = pair.Value };
            else
                _live[pair.Key] = new TrackedLive(pair.Value, now);
        }

        var retain = TimeSpan.FromMinutes(Math.Max(1, Settings.ClosedRetentionMinutes));
        _closed.RemoveAll(item => now - item.EndedAt > retain);
        if (_closed.Count > 400)
            _closed.RemoveRange(0, _closed.Count - 400);
    }

    private void UpdatePeaks()
    {
        if (!Settings.PeakAlertEnabled)
        {
            IsPeaking = false;
            PeakText = "";
            _wasPeaking = false;
            return;
        }

        var downLimit = Settings.PeakDownMBps * 1024d * 1024d;
        var upLimit = Settings.PeakUpMBps * 1024d * 1024d;
        var peakDown = Settings.PeakDownMBps > 0 && DownBps >= downLimit;
        var peakUp = Settings.PeakUpMBps > 0 && UpBps >= upLimit;
        var peaking = peakDown || peakUp;
        string? ruleText = null;
        foreach (var rule in Settings.AlertRules ?? [])
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.ProcessNameContains)) continue;
            var hit = TopProcessBandwidth.FirstOrDefault(p => p.Name.Contains(rule.ProcessNameContains, StringComparison.OrdinalIgnoreCase));
            if (hit is null) continue;
            var downMb = hit.DownBps / (1024d * 1024d);
            var upMb = hit.UpBps / (1024d * 1024d);
            if ((rule.MaxDownMBps > 0 && downMb >= rule.MaxDownMBps) || (rule.MaxUpMBps > 0 && upMb >= rule.MaxUpMBps))
            {
                peaking = true;
                ruleText = "Alerta " + hit.Name + ": " + Format.Rate(hit.DownBps) + " down / " + Format.Rate(hit.UpBps) + " up";
                break;
            }
        }
        var wasPeakingUi = IsPeaking;
        IsPeaking = peaking;
        if (wasPeakingUi != peaking)
            OverlayStyleChanged?.Invoke();
        PeakText = peaking
            ? ruleText ?? (peakDown && peakUp
                ? $"Pico de consumo  ↓ {DownText}  ↑ {UpText}"
                : peakDown
                    ? $"Pico de download  {DownText}"
                    : $"Pico de upload  {UpText}")
            : "";

        if (peaking && !_wasPeaking)
            PeakRaised?.Invoke(PeakText);
        _wasPeaking = peaking;
    }

    private void RebuildViews()
    {
        foreach (var group in Processes)
        {
            if (group.IsExpanded) _expandedProcesses.Add(group.Name);
            else _expandedProcesses.Remove(group.Name);
        }
        foreach (var group in Addresses)
        {
            if (group.IsExpanded) _expandedAddresses.Add(group.Address);
            else _expandedAddresses.Remove(group.Address);
        }

        IEnumerable<NetConnection> query = _raw;
        if (!Settings.ShowUdp)
            query = query.Where(c => c.Protocol != NetProtocol.Udp);
        if (Settings.EstablishedOnly)
            query = query.Where(c => c.Protocol != NetProtocol.Tcp || c.State == "ESTABLISHED");
        if (Settings.HideLoopback)
            query = query.Where(c => c.Scope != AddressScope.Loopback && Format.Classify(c.LocalAddress) != AddressScope.Loopback);
        if (Settings.HidePrivate)
            query = query.Where(c => c.Scope is AddressScope.Public or AddressScope.Unspecified);

        var search = _search.Trim();
        if (search.Length > 0)
        {
            query = query.Where(c =>
                c.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.RemoteAddress.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.LocalAddress.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.Pid.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (c.HostName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = query
            .Select(Enrich)
            .OrderBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.RemoteAddress, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.RemotePort)
            .ToList();

        Connections = list;
        Processes = list
            .GroupBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(x => x.DownBps + x.UpBps))
            .ThenByDescending(g => g.Count())
            .Select(g =>
            {
                var path = g.Select(x => x.ProcessPath).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                var down = 0d;
                var up = 0d;
                foreach (var pid in g.Select(x => x.Pid).Distinct())
                {
                    if (_bwByPid.TryGetValue(pid, out var rates))
                    {
                        down += rates.DownBps;
                        up += rates.UpBps;
                    }
                }
                return new ProcessGroup
                {
                    Name = g.Key,
                    Icon = g.Select(x => x.Icon).FirstOrDefault(x => x is not null),
                    Path = path,
                    PidSummary = "PID " + string.Join(", ", g.Select(x => x.Pid).Distinct().OrderBy(x => x)),
                    ConnectionCount = g.Count(),
                    IpCount = g.Select(x => x.RemoteAddress).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Connections = g.ToList(),
                    IsExpanded = _expandedProcesses.Contains(g.Key),
                    IsBlocked = path is not null && Settings.BlockedPrograms.Contains(path, StringComparer.OrdinalIgnoreCase),
                    DownBps = down,
                    UpBps = up
                };
            })
            .ToList();

        Addresses = list
            .Where(c => c.RemoteAddress.Length > 0)
            .GroupBy(c => c.RemoteAddress, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(x => x.DownBps + x.UpBps))
            .ThenByDescending(g => g.Count())
            .Select(g =>
            {
                _geo.TryGetValue(g.Key, out var geo);
                _bwByRemote.TryGetValue(g.Key, out var ipRates);
                return new IpGroup
                {
                    Address = g.Key,
                    Flag = Format.FlagEmoji(geo?.CountryCode),
                    CountryCode = geo?.CountryCode ?? "",
                    FlagImage = FlagImages.Get(geo?.CountryCode),
                    GeoText = geo?.Display ?? string.Empty,
                    HostName = g.Select(x => x.HostName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                    Scope = g.First().Scope,
                    ConnectionCount = g.Count(),
                    Apps = string.Join(", ", g.Select(x => x.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
                    Connections = g.ToList(),
                    IsExpanded = _expandedAddresses.Contains(g.Key),
                    IsBlocked = Settings.BlockedAddresses.Contains(g.Key, StringComparer.OrdinalIgnoreCase),
                    DownBps = ipRates.DownBps,
                    UpBps = ipRates.UpBps
                };
            })
            .ToList();

        var now = DateTime.UtcNow;
        IEnumerable<ClosedRecord> closedQuery = _closed;
        if (Settings.HideLoopback)
            closedQuery = closedQuery.Where(c => c.Connection.Scope != AddressScope.Loopback);
        if (Settings.HidePrivate)
            closedQuery = closedQuery.Where(c => c.Connection.Scope is AddressScope.Public or AddressScope.Unspecified);
        if (search.Length > 0)
        {
            closedQuery = closedQuery.Where(c =>
                c.Connection.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.Connection.RemoteAddress.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.Connection.LocalAddress.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.Connection.Pid.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (c.Connection.HostName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        ClosedConnections = closedQuery
            .OrderByDescending(c => c.EndedAt)
            .Select(c =>
            {
                _geo.TryGetValue(c.Connection.RemoteAddress, out var closedGeo);
                return new ClosedConnection
                {
                    ProcessName = c.Connection.ProcessName,
                    Icon = c.Connection.Icon,
                    Pid = c.Connection.Pid,
                    ProtocolText = c.Connection.ProtocolText,
                    LocalDisplay = c.Connection.LocalDisplay,
                    RemoteDisplay = c.Connection.RemoteDisplay,
                    RemoteAddress = c.Connection.RemoteAddress,
                    HostName = c.Connection.HostName,
                    Scope = c.Connection.Scope,
                    StartedAt = c.StartedAt,
                    EndedAt = c.EndedAt,
                    EndedLabel = c.EndedAt.ToLocalTime().ToString("HH:mm:ss"),
                    DurationLabel = Format.Duration(c.EndedAt - c.StartedAt),
                    AgoLabel = Format.Ago(now - c.EndedAt),
                    Flag = Format.FlagEmoji(closedGeo?.CountryCode),
                    CountryCode = closedGeo?.CountryCode ?? "",
                    FlagImage = FlagImages.Get(closedGeo?.CountryCode),
                    GeoText = closedGeo?.Display ?? ""
                };
            })
            .ToList();

        ConnectionCount = list.Count;
        ProcessCount = Processes.Count;
        AddressCount = Addresses.Count;
        if (_flashStatus is not null && DateTime.UtcNow < _flashUntil)
            StatusText = _flashStatus;
        else
        {
            _flashStatus = null;
            StatusText = $"{ConnectionCount} ativas · {ClosedConnections.Count} encerradas · {ProcessCount} apps · {AddressCount} IPs";
        }
    }

    private NetConnection Enrich(NetConnection c)
    {
        if (c.Scope == AddressScope.Public && c.RemoteAddress.Length > 0)
            RequestGeo(c.RemoteAddress);
        _geo.TryGetValue(c.RemoteAddress, out var geo);
        _bwByConn.TryGetValue(c.Key, out var rates);
        return new NetConnection
        {
            Protocol = c.Protocol,
            State = c.State,
            Pid = c.Pid,
            ProcessName = c.ProcessName,
            ProcessPath = c.ProcessPath,
            Icon = c.Icon,
            LocalAddress = c.LocalAddress,
            LocalPort = c.LocalPort,
            RemoteAddress = c.RemoteAddress,
            RemotePort = c.RemotePort,
            HostName = c.HostName,
            Scope = c.Scope,
            Flag = Format.FlagEmoji(geo?.CountryCode),
            CountryCode = geo?.CountryCode ?? "",
            FlagImage = FlagImages.Get(geo?.CountryCode),
            GeoText = geo?.Display ?? "",
            DownBps = rates.DownBps,
            UpBps = rates.UpBps
        };
    }

    private void SyncAdapters(IReadOnlyList<AdapterRate> rates)
    {
        var current = Adapters.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var next = new List<AdapterOption>(rates.Count);
        var changed = rates.Count != Adapters.Count;
        foreach (var rate in rates)
        {
            if (current.TryGetValue(rate.Id, out var existing))
            {
                next.Add(existing);
            }
            else
            {
                next.Add(new AdapterOption
                {
                    Id = rate.Id,
                    Name = rate.Name,
                    Description = rate.Description,
                    Enabled = rate.Enabled
                });
                changed = true;
            }
        }

        if (changed || !next.Select(a => a.Id).SequenceEqual(Adapters.Select(a => a.Id)))
            Adapters = next;
    }

    private void PushHistory(double down, double up)
    {
        Shift(_downHist, down);
        Shift(_upHist, up);
        if (_histCount < _downHist.Length)
            _histCount++;
        DownSpark = ToPoints(_downHist, _histCount);
        UpSpark = ToPoints(_upHist, _histCount);
        var start = _downHist.Length - Math.Max(_histCount, 1);
        DownHistory = _downHist.Skip(start).ToArray();
        UpHistory = _upHist.Skip(start).ToArray();
    }

    private static void Shift(double[] values, double incoming)
    {
        Array.Copy(values, 1, values, 0, values.Length - 1);
        values[^1] = incoming;
    }

    private static PointCollection ToPoints(double[] data, int count)
    {
        const double width = 132;
        const double height = 28;
        var start = data.Length - Math.Max(count, 1);
        var max = 1d;
        for (var i = start; i < data.Length; i++)
            max = Math.Max(max, data[i]);

        var points = new PointCollection(count);
        var visible = Math.Max(count, 1);
        for (var i = 0; i < visible; i++)
        {
            var value = data[start + i];
            var x = visible == 1 ? 0 : i * (width / (visible - 1));
            var y = height - (value / max) * (height - 4) - 2;
            points.Add(new Point(x, y));
        }
        if (points.CanFreeze)
            points.Freeze();
        return points;
    }

    private void Persist()
    {
        if (!_persistReady)
            return;
        try
        {
            SettingsStore.Save(Settings);
        }
        catch
        {
            // keep running even if the settings file is locked
        }
    }

    private static bool TryParseRate(string value, out double parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out parsed) ||
            double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            parsed = Math.Clamp(parsed, 0, 10000);
            return true;
        }
        return false;
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private readonly record struct TrackedLive(NetConnection Connection, DateTime FirstSeen);
    private readonly record struct ClosedRecord(NetConnection Connection, DateTime StartedAt, DateTime EndedAt);
}
