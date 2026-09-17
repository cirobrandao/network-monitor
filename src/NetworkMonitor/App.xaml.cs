using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NetworkMonitor.Native;
using NetworkMonitor.Services;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;

namespace NetworkMonitor;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private Forms.NotifyIcon? _tray;
    private MainWindow? _main;
    private OverlayWindow? _overlay;
    private CancellationTokenSource? _loopCts;
    private CancellationTokenSource? _publicIpCts;
    private bool _ownsMutex;
    private readonly BandwidthMonitor _bandwidth = new();
    private readonly ProcessResolver _processes = new();
    private readonly DnsResolver _dns = new();
    private readonly PublicIpService _publicIp = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "network-monitor-crash.txt"), args.Exception.ToString());
            args.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "network-monitor-crash.txt"), args.ExceptionObject.ToString());
        };
        base.OnStartup(e);

        if (e.Args.Any(a => string.Equals(a, "--dump", StringComparison.OrdinalIgnoreCase)))
        {
            DumpConnections();
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, @"Local\NetworkMonitor.SingleInstance", out var created);
        _ownsMutex = created;
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\NetworkMonitor.ShowWindow");
        if (!created)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        ThreadPool.RegisterWaitForSingleObject(
            _showSignal,
            (_, _) => Dispatcher.Invoke(() => _main?.RestoreFromTray()),
            null,
            -1,
            false);

        var loading = new LoadingWindow();
        loading.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var state = AppState.Current;
        await Task.Run(state.Load);
        _main = new MainWindow();
        MainWindow = _main;
        _overlay = new OverlayWindow();
        CreateTray();

        AppState.Current.OverlayVisibilityChanged += ApplyOverlayVisibility;
        AppState.Current.RefreshIntervalChanged += RestartLoop;
        AppState.Current.PeakRaised += OnPeakRaised;

        ApplyOverlayVisibility();
        if (!AppState.Current.StartMinimized)
            _main.Show();

        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        loading.Close();

        _loopCts = new CancellationTokenSource();
        var samplingToken = _loopCts.Token;
        _ = Task.Run(() => RunLoopAsync(samplingToken));
        _publicIpCts = new CancellationTokenSource();
        _ = RunPublicIpLoopAsync(_publicIpCts.Token);

        // Auto-update: silent unless a newer GitHub Release exists
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try { await UpdateUi.CheckAndPromptAsync(quietWhenCurrent: true); }
            catch { /* ignore startup update errors */ }
        });
    }

    public void ShowMainWindow() => _main?.RestoreFromTray();

    public void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = true,
                Verb = "runas"
            });
            ExitApp();
        }
        catch
        {
            // UAC cancelado
        }
    }

    public void ExitApp()
    {
        _loopCts?.Cancel();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _overlay?.Close();
        _main?.ForceClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _loopCts?.Cancel();
        _publicIpCts?.Cancel();
        _tray?.Dispose();
        _showSignal?.Dispose();
        if (_ownsMutex)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void ApplyOverlayVisibility()
    {
        if (_overlay is null) return;
        if (AppState.Current.ShowOverlay)
        {
            _overlay.ApplyStyle();
            _overlay.Show();
        }
        else
        {
            _overlay.Hide();
        }
    }

    private void RestartLoop()
    {
        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;
        _ = Task.Run(() => RunLoopAsync(token));
    }

    private async Task RunPublicIpLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var settings = AppState.Current.Settings;
                if (settings.ShowPublicIp || settings.ShowOverlay)
                {
                    await _publicIp.RefreshIfDueAsync(token: token);
                    AppState.Current.SetPublicIp(_publicIp.Address);
                }
                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var settings = AppState.Current.Settings;
                var bandwidth = _bandwidth.Capture(settings.DisabledAdapters);
                var raw = IpHelper.GetAll(settings.ShowUdp);
                var mapped = new List<(Native.RawConnection Row, ProcessInfo Process, AddressScope Scope, string? Host)>(raw.Count);
                foreach (var row in raw)
                {
                    var process = _processes.Resolve(row.Pid);
                    var scope = Format.Classify(row.RemoteAddress);
                    string? host = null;
                    if (settings.ResolveDns && scope == AddressScope.Public && row.RemoteAddress.Length > 0)
                    {
                        host = _dns.Lookup(row.RemoteAddress);
                        _dns.Request(row.RemoteAddress);
                    }
                    mapped.Add((row, process, scope, host));
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    var connections = new List<NetConnection>(mapped.Count);
                    foreach (var item in mapped)
                    {
                        connections.Add(new NetConnection
                        {
                            Protocol = item.Row.Protocol,
                            State = item.Row.State,
                            Pid = item.Row.Pid,
                            ProcessName = item.Process.Name,
                            ProcessPath = item.Process.Path,
                            Icon = _processes.GetIcon(item.Process.Path),
                            LocalAddress = item.Row.LocalAddress,
                            LocalPort = item.Row.LocalPort,
                            RemoteAddress = item.Row.RemoteAddress,
                            RemotePort = item.Row.RemotePort,
                            HostName = item.Host,
                            Scope = item.Scope
                        });
                    }
                    AppState.Current.ApplySnapshot(bandwidth, connections);
                });
            }
            catch
            {
                // keep sampling even if a single capture fails
            }

            try
            {
                var delay = Math.Max(400, AppState.Current.Settings.RefreshMs);
                await Task.Delay(delay, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private void OnPeakRaised(string text)
    {
        Dispatcher.Invoke(() =>
        {
            _tray?.ShowBalloonTip(4000, "Pico de consumo", text, Forms.ToolTipIcon.Warning);
        });
    }

    private static void DumpConnections()
    {
        var path = Path.Combine(Path.GetTempPath(), "network-monitor-dump.txt");
        var rows = IpHelper.GetAll(includeUdp: false);
        using var writer = new StreamWriter(path);
        writer.WriteLine("pid\tlocal\tremote\tstate");
        foreach (var row in rows.OrderBy(r => r.Pid).ThenBy(r => r.RemoteAddress))
            writer.WriteLine($"{row.Pid}\t{row.LocalAddress}:{row.LocalPort}\t{row.RemoteAddress}:{row.RemotePort}\t{row.State}");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "network-monitor-dump-path.txt"), path);
    }

    private void CreateTray()
    {
        var icon = LoadTrayIcon();
        _tray = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "Network Monitor",
            Icon = icon
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                _main?.RestoreFromTray();
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => _main?.RestoreFromTray());
        menu.Items.Add("Mostrar widget", null, (_, _) => AppState.Current.ShowOverlay = !AppState.Current.ShowOverlay);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var extracted = Icon.ExtractAssociatedIcon(path);
                if (extracted is not null)
                    return extracted;
            }
        }
        catch
        {
            // fallback below
        }

        return SystemIcons.Application;
    }
}
