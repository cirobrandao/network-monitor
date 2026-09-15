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
    private readonly double[] _downHist = new double[60];
    private readonly double[] _upHist = new double[60];
    private int _histCount;
    private bool _persistReady;
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
    private IReadOnlyList<AdapterOption> _adapters = [];
    private string _statusText = "Capturando conexões…";
    private int _connectionCount;
    private int _processCount;
    private int _addressCount;
    private List<NetConnection> _raw = [];

    public AppSettings Settings { get; private set; } = new();

    public event Action? OverlayVisibilityChanged;
    public event Action? OverlayStyleChanged;
    public event Action? RefreshIntervalChanged;

    public double DownBps { get => _downBps; private set => Set(ref _downBps, value); }
    public double UpBps { get => _upBps; private set => Set(ref _upBps, value); }
    public string DownText { get => _downText; private set => Set(ref _downText, value); }
    public string UpText { get => _upText; private set => Set(ref _upText, value); }
    public PointCollection DownSpark { get => _downSpark; private set => Set(ref _downSpark, value); }
    public PointCollection UpSpark { get => _upSpark; private set => Set(ref _upSpark, value); }
    public IReadOnlyList<NetConnection> Connections { get => _connections; private set => Set(ref _connections, value); }
    public IReadOnlyList<ProcessGroup> Processes { get => _processes; private set => Set(ref _processes, value); }
    public IReadOnlyList<IpGroup> Addresses { get => _addresses; private set => Set(ref _addresses, value); }
    public IReadOnlyList<AdapterOption> Adapters { get => _adapters; private set => Set(ref _adapters, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public int ConnectionCount { get => _connectionCount; private set => Set(ref _connectionCount, value); }
    public int ProcessCount { get => _processCount; private set => Set(ref _processCount, value); }
    public int AddressCount { get => _addressCount; private set => Set(ref _addressCount, value); }

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
        Raise(nameof(RefreshMs));
        Raise(nameof(RefreshFast));
        Raise(nameof(RefreshNormal));
        Raise(nameof(RefreshSlow));
    }

    public void ApplySnapshot(BandwidthSnapshot bandwidth, List<NetConnection> connections)
    {
        DownBps = bandwidth.DownBps;
        UpBps = bandwidth.UpBps;
        DownText = Format.Rate(DownBps);
        UpText = Format.Rate(UpBps);
        PushHistory(DownBps, UpBps);
        SyncAdapters(bandwidth.Adapters);
        _raw = connections;
        RebuildViews();
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
            .Select(g => new ProcessGroup
            {
                Name = g.Key,
                Icon = g.Select(x => x.Icon).FirstOrDefault(x => x is not null),
                PidSummary = "PID " + string.Join(", ", g.Select(x => x.Pid).Distinct().OrderBy(x => x)),
                ConnectionCount = g.Count(),
                IpCount = g.Select(x => x.RemoteAddress).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Connections = g.ToList(),
                IsExpanded = _expandedProcesses.Contains(g.Key)
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
                IsExpanded = _expandedAddresses.Contains(g.Key)
            })
            .ToList();

        ConnectionCount = list.Count;
        ProcessCount = Processes.Count;
        AddressCount = Addresses.Count;
        StatusText = $"{ConnectionCount} conexões · {ProcessCount} aplicativos · {AddressCount} IPs";
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

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
