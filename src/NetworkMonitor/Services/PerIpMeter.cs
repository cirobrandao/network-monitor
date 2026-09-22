using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using NetworkMonitor.Native;

namespace NetworkMonitor.Services;

/// <summary>
/// Windows only returns per-connection TCP byte counters to an elevated
/// process. This helper starts one hidden elevated meter (UAC once) and
/// reads its samples over a local pipe, so each remote IP gets its own
/// speed instead of an even split of the application total.
/// </summary>
internal sealed class PerIpMeter : IDisposable
{
    public const string PipeName = "NetworkMonitor.PerIp.v1";
    private const string MeterMutexName = @"Local\NetworkMonitor.PerIpMeter";
    private const string ConsentFlag = "per-ip-meter.ok";

    private readonly object _gate = new();
    private Dictionary<string, Rates> _latest = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _consent;

    public bool HasFreshSample { get; private set; }

    public void Start()
    {
        if (FirewallService.IsElevated || _loop is not null)
            return;
        _consent = true;
        RememberConsent();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => ClientLoopAsync(_cts.Token));
    }

    public void RequestElevatedMeter()
    {
        Start();
        RememberConsent();
        if (!IsMeterRunning())
            TryLaunchElevated();
    }

    public bool TryGet(string connectionKey, out Rates rates)
    {
        lock (_gate)
            return _latest.TryGetValue(connectionKey, out rates);
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _loop?.Wait(500); } catch { }
        _cts?.Dispose();
    }

    public static int RunServer(string[] args)
    {
        if (!FirewallService.IsElevated)
            return 3;
        var parent = ParseParentPid(args);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try
        {
            RunServerLoop(parent, cts.Token);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private async Task ClientLoopAsync(CancellationToken token)
    {
        var asked = false;
        while (!token.IsCancellationRequested)
        {
            if (!IsMeterRunning())
            {
                if (_consent && !asked)
                {
                    asked = true;
                    TryLaunchElevated();
                }
                await Task.Delay(1500, token).ConfigureAwait(false);
            }

            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.In);
                await pipe.ConnectAsync(1200, token).ConfigureAwait(false);
                RememberConsent();
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line is null)
                        break;
                    ApplyLine(line);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(800, token).ConfigureAwait(false);
            }
        }
    }

    private void ApplyLine(string line)
    {
        var next = new Dictionary<string, Rates>(StringComparer.Ordinal);
        foreach (var part in line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(';');
            if (bits.Length != 3)
                continue;
            if (!double.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var down))
                continue;
            if (!double.TryParse(bits[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var up))
                continue;
            next[bits[0]] = new Rates(down, up);
        }
        lock (_gate)
        {
            _latest = next;
            HasFreshSample = true;
        }
    }

    private static void RunServerLoop(int parentPid, CancellationToken token)
    {
        using var meterMutex = new Mutex(true, MeterMutexName, out var created);
        if (!created)
            return;

        var previous = new Dictionary<string, (ulong In, ulong Out, long Ticks)>(StringComparer.Ordinal);
        using var server = new NamedPipeServerStream(
            PipeName,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        server.WaitForConnection();
        using var writer = new StreamWriter(server, new UTF8Encoding(false)) { AutoFlush = true };
        while (!token.IsCancellationRequested)
        {
            if (parentPid > 0 && !IsAlive(parentPid))
                return;

            var now = Stopwatch.GetTimestamp();
            var rows = IpHelper.GetAll(includeUdp: false);
            var active = new HashSet<string>(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (var row in rows)
            {
                if (!row.IsIPv4 || row.Protocol != NetProtocol.Tcp || row.Pid <= 4)
                    continue;
                if (!string.Equals(row.State, "ESTABLISHED", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrEmpty(row.RemoteAddress))
                    continue;
                if (!TcpEStats.TryGetDataBytes(row, out var bytesIn, out var bytesOut))
                    continue;

                var key = $"{row.RawLocalAddr}:{row.RawLocalPort}-{row.RawRemoteAddr}:{row.RawRemotePort}";
                active.Add(key);
                if (previous.TryGetValue(key, out var prev))
                {
                    var dt = (now - prev.Ticks) / (double)Stopwatch.Frequency;
                    if (dt > 0.05 && bytesIn >= prev.In && bytesOut >= prev.Out)
                    {
                        var down = (bytesIn - prev.In) / dt;
                        var up = (bytesOut - prev.Out) / dt;
                        parts.Add(string.Create(CultureInfo.InvariantCulture, $"{row.Key};{down:0.###};{up:0.###}"));
                    }
                }
                previous[key] = (bytesIn, bytesOut, now);
            }

            foreach (var stale in previous.Keys.Except(active).ToList())
                previous.Remove(stale);
            TcpEStats.ForgetStaleConnections(active);

            writer.WriteLine(string.Join('\t', parts));
            Thread.Sleep(1000);
        }
    }

    private static void TryLaunchElevated()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--per-ip-meter --parent " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch
        {
            // UAC cancelled. The UI keeps the even split until the user accepts.
        }
    }

    private static bool IsMeterRunning()
    {
        try
        {
            using var mutex = Mutex.OpenExisting(MeterMutexName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static int ParseParentPid(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--parent", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var pid))
                return pid;
        }
        return 0;
    }

    private void RememberConsent()
    {
        if (_consent)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConsentPath())!);
            File.WriteAllText(ConsentPath(), "ok");
            _consent = true;
        }
        catch { }
    }

    private static string ConsentPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkMonitor",
            ConsentFlag);
}
