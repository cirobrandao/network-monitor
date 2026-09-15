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
    public bool IsPeaking { get => _isPeaking; private set => Set(ref _isPeaking, value); }
    public string PeakText { get => _peakText; private set => Set(ref _peakText, value); }
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
    public string ElevationText => IsElevated ? "Executando como administrador" : "Sem administrador — bloqueios precisam de elevação";
    public bool OverlayDetailsVisible => !Settings.OverlayCompact && (Settings.OverlayShowPublicIp || IsPeaking);

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
            Raise(nameof(OverlayDetailsVisible));
            OverlayStyleChanged?.Invoke();
            Persist();
        }
    }

    public double OverlayOpacity
    {
        get => Settings.OverlayOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0.45, 1);
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

    public bool OverlayShowConnections
    {
        get => Settings.OverlayShowConnections;
        set
        {
            if (Settings.OverlayShowConnections == value) return;
            Settings.OverlayShowConnections = value;
            Raise();
            OverlayStyleChanged?.Invoke();
            Persist();
        }
    }

    public bool OverlayShowPublicIp
    {
        get => Settings.OverlayShowPublicIp;
        set
        {
            if (Settings.OverlayShowPublicIp == value) return;
            Settings.OverlayShowPublicIp = value;
            Raise();
            Raise(nameof(OverlayDetailsVisible));
            OverlayStyleChanged?.Invoke();
            Persist();
        }
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
        Raise(nameof(OverlayShowConnections));
        Raise(nameof(OverlayShowPublicIp));
        Raise(nameof(ShowChart));
        Raise(nameof(ChartType));
        Raise(nameof(ChartLine));
        Raise(nameof(ChartArea));
        Raise(nameof(ChartBar));
        Raise(nameof(OverlayDetailsVisible));
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
    }

    public void SetPublicIp(string? ip)
    {
        PublicIp = string.IsNullOrWhiteSpace(ip) ? "—" : ip;
    }

    public void ApplySnapshot(BandwidthSnapshot bandwidth, List<NetConnection> connections)
    {
        DownBps = bandwidth.DownBps;
        UpBps = bandwidth.UpBps;
        DownText = Format.Rate(DownBps);
        UpText = Format.Rate(UpBps);
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
        if (!FirewallService.IsElevated)
            return "O Firewall do Windows exige executar o monitor como administrador.";

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
        var wasPeakingUi = IsPeaking;
        IsPeaking = peaking;
        if (wasPeakingUi != peaking)
        {
            Raise(nameof(OverlayDetailsVisible));
            OverlayStyleChanged?.Invoke();
        }
        PeakText = peaking
            ? peakDown && peakUp
                ? $"Pico de consumo  ↓ {DownText}  ↑ {UpText}"
                : peakDown
                    ? $"Pico de download  {DownText}"
                    : $"Pico de upload  {UpText}"
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
            .OrderBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.RemoteAddress, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.RemotePort)
            .ToList();

        Connections = list;
        Processes = list
            .GroupBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g =>
            {
                var path = g.Select(x => x.ProcessPath).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
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
                    IsBlocked = path is not null && Settings.BlockedPrograms.Contains(path, StringComparer.OrdinalIgnoreCase)
                };
            })
            .ToList();

        Addresses = list
            .Where(c => c.RemoteAddress.Length > 0)
            .GroupBy(c => c.RemoteAddress, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new IpGroup
            {
                Address = g.Key,
                HostName = g.Select(x => x.HostName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                Scope = g.First().Scope,
                ConnectionCount = g.Count(),
                Apps = string.Join(", ", g.Select(x => x.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
                Connections = g.ToList(),
                IsExpanded = _expandedAddresses.Contains(g.Key),
                IsBlocked = Settings.BlockedAddresses.Contains(g.Key, StringComparer.OrdinalIgnoreCase)
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
            .Select(c => new ClosedConnection
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
                AgoLabel = Format.Ago(now - c.EndedAt)
            })
            .ToList();

        ConnectionCount = list.Count;
        ProcessCount = Processes.Count;
        AddressCount = Addresses.Count;
        StatusText = $"{ConnectionCount} ativas · {ClosedConnections.Count} encerradas · {ProcessCount} apps · {AddressCount} IPs";
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
