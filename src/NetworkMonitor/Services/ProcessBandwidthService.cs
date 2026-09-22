using System.Diagnostics;
using System.Runtime.InteropServices;
using NetworkMonitor.Native;

namespace NetworkMonitor.Services;

internal readonly struct Rates
{
    public Rates(double downBps, double upBps)
    {
        DownBps = downBps;
        UpBps = upBps;
    }

    public double DownBps { get; }
    public double UpBps { get; }

    public Rates Add(Rates other) => new(DownBps + other.DownBps, UpBps + other.UpBps);

    public Rates Share(int parts)
        => parts <= 0 ? this : new Rates(DownBps / parts, UpBps / parts);
}

internal sealed class ProcessBandwidthRow
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public required double DownBps { get; init; }
    public required double UpBps { get; init; }
    public string DownText => Format.Rate(DownBps);
    public string UpText => Format.Rate(UpBps);
    public string Label => $"{Name}  {DownText}\u2193  {UpText}\u2191";
}

internal sealed class TrafficSnapshot
{
    public static TrafficSnapshot Empty { get; } = new()
    {
        ByConnection = new Dictionary<string, Rates>(),
        ByPid = new Dictionary<int, Rates>(),
        ByRemote = new Dictionary<string, Rates>(StringComparer.OrdinalIgnoreCase),
        TopProcesses = []
    };

    public required IReadOnlyDictionary<string, Rates> ByConnection { get; init; }
    public required IReadOnlyDictionary<int, Rates> ByPid { get; init; }
    public required IReadOnlyDictionary<string, Rates> ByRemote { get; init; }
    public required IReadOnlyList<ProcessBandwidthRow> TopProcesses { get; init; }
}

internal sealed class ProcessBandwidthService
{
    private const int ProcessQueryLimitedInformation = 0x1000;
    private readonly Dictionary<int, (ulong Read, ulong Write, long Ticks)> _io = [];
    private readonly Dictionary<string, (ulong BytesIn, ulong BytesOut, long Ticks)> _estats = [];

    public TrafficSnapshot Sample(
        IEnumerable<RawConnection> connections,
        double nicDownBps,
        double nicUpBps,
        int top = 5,
        PerIpMeter? perIp = null)
    {
        var now = Stopwatch.GetTimestamp();
        var established = connections
            .Where(c =>
                c.Protocol == NetProtocol.Tcp
                && c.Pid > 4
                && !string.IsNullOrEmpty(c.RemoteAddress)
                && string.Equals(c.State, "ESTABLISHED", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pids = established.Select(c => c.Pid).Distinct().ToList();
        var ioRates = new Dictionary<int, Rates>();
        foreach (var pid in pids)
        {
            if (!TryReadIo(pid, out var read, out var write))
                continue;
            double down = 0, up = 0;
            if (_io.TryGetValue(pid, out var prev))
            {
                var dt = (now - prev.Ticks) / (double)Stopwatch.Frequency;
                if (dt > 0.05)
                {
                    down = Math.Max(0, (long)(read - prev.Read)) / dt;
                    up = Math.Max(0, (long)(write - prev.Write)) / dt;
                }
            }
            _io[pid] = (read, write, now);
            ioRates[pid] = new Rates(down, up);
        }

        foreach (var stale in _io.Keys.Except(pids).ToList())
            _io.Remove(stale);

        var nicDown = Math.Max(0, nicDownBps);
        var nicUp = Math.Max(0, nicUpBps);
        var weightDown = ioRates.Values.Sum(r => r.DownBps);
        var weightUp = ioRates.Values.Sum(r => r.UpBps);

        var byPid = new Dictionary<int, Rates>();
        foreach (var pid in pids)
        {
            ioRates.TryGetValue(pid, out var io);
            var down = nicDown > 1 && weightDown > 1 ? nicDown * (io.DownBps / weightDown) : io.DownBps;
            var up = nicUp > 1 && weightUp > 1 ? nicUp * (io.UpBps / weightUp) : io.UpBps;
            byPid[pid] = new Rates(down, up);
        }

        if (nicDown + nicUp > 1024 && byPid.Values.All(r => r.DownBps + r.UpBps < 1))
        {
            var counts = established.GroupBy(c => c.Pid).ToDictionary(g => g.Key, g => g.Count());
            var total = Math.Max(1, counts.Values.Sum());
            foreach (var (pid, n) in counts)
                byPid[pid] = new Rates(nicDown * n / total, nicUp * n / total);
        }

        // Try to read real per-connection byte counters first (accurate, but
        // only works for IPv4 TCP sockets and only when running elevated).
        // Connections without real data fall back to an even split of the
        // remaining process-level rate, so every IP no longer shows an
        // identical, misleading speed when real measurements are available.
        var measured = new Dictionary<string, Rates>(StringComparer.Ordinal);
        var activeEstatsKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in established)
        {
            if (!c.IsIPv4)
                continue;
            var estatsKey = $"{c.RawLocalAddr}:{c.RawLocalPort}-{c.RawRemoteAddr}:{c.RawRemotePort}";
            activeEstatsKeys.Add(estatsKey);
            if (!TcpEStats.TryGetDataBytes(c, out var bytesIn, out var bytesOut))
                continue;

            if (_estats.TryGetValue(estatsKey, out var prev))
            {
                var dt = (now - prev.Ticks) / (double)Stopwatch.Frequency;
                if (dt > 0.05 && bytesIn >= prev.BytesIn && bytesOut >= prev.BytesOut)
                {
                    var connKey = $"{c.Protocol}|{c.Pid}|{c.LocalAddress}:{c.LocalPort}|{c.RemoteAddress}:{c.RemotePort}";
                    measured[connKey] = new Rates((bytesIn - prev.BytesIn) / dt, (bytesOut - prev.BytesOut) / dt);
                }
            }
            _estats[estatsKey] = (bytesIn, bytesOut, now);
        }
        TcpEStats.ForgetStaleConnections(activeEstatsKeys);
        foreach (var stale in _estats.Keys.Except(activeEstatsKeys).ToList())
            _estats.Remove(stale);

        var measuredByPid = new Dictionary<int, Rates>();
        foreach (var c in established)
        {
            var connKey = $"{c.Protocol}|{c.Pid}|{c.LocalAddress}:{c.LocalPort}|{c.RemoteAddress}:{c.RemotePort}";
            if (!measured.TryGetValue(connKey, out var rate))
                continue;
            measuredByPid[c.Pid] = measuredByPid.TryGetValue(c.Pid, out var sum) ? sum.Add(rate) : rate;
        }

        var unmeasuredWeight = new Dictionary<int, (double Down, double Up)>();
        foreach (var c in established)
        {
            if (measured.ContainsKey(c.Key))
                continue;
            var (downW, upW) = ActivityWeight(c);
            unmeasuredWeight.TryGetValue(c.Pid, out var sum);
            unmeasuredWeight[c.Pid] = (sum.Down + downW, sum.Up + upW);
        }

        var byConn = new Dictionary<string, Rates>(StringComparer.Ordinal);
        var byRemote = new Dictionary<string, Rates>(StringComparer.OrdinalIgnoreCase);
        var meterLive = perIp is not null && perIp.HasFreshSample;
        foreach (var c in established)
        {
            var connKey = c.Key;
            Rates share;
            if (meterLive)
            {
                perIp!.TryGet(connKey, out share);
            }
            else if (measured.TryGetValue(connKey, out var measuredRate))
            {
                share = measuredRate;
            }
            else
            {
                if (!byPid.TryGetValue(c.Pid, out var processRate))
                    continue;
                measuredByPid.TryGetValue(c.Pid, out var alreadyMeasured);
                var remaining = new Rates(
                    Math.Max(0, processRate.DownBps - alreadyMeasured.DownBps),
                    Math.Max(0, processRate.UpBps - alreadyMeasured.UpBps));
                var (downW, upW) = ActivityWeight(c);
                unmeasuredWeight.TryGetValue(c.Pid, out var totalW);
                share = new Rates(
                    totalW.Down > 0 ? remaining.DownBps * (downW / totalW.Down) : 0,
                    totalW.Up > 0 ? remaining.UpBps * (upW / totalW.Up) : 0);
            }

            byConn[connKey] = share;
            byRemote[c.RemoteAddress] = byRemote.TryGetValue(c.RemoteAddress, out var ipRate)
                ? ipRate.Add(share)
                : share;
        }

        var rows = new List<ProcessBandwidthRow>(byPid.Count);
        foreach (var (pid, rates) in byPid)
        {
            string name;
            try
            {
                using var p = Process.GetProcessById(pid);
                name = p.ProcessName;
            }
            catch
            {
                name = $"pid:{pid}";
            }

            rows.Add(new ProcessBandwidthRow
            {
                Pid = pid,
                Name = name,
                DownBps = rates.DownBps,
                UpBps = rates.UpBps
            });
        }

        return new TrafficSnapshot
        {
            ByConnection = byConn,
            ByPid = byPid,
            ByRemote = byRemote,
            TopProcesses = rows
                .OrderByDescending(r => r.DownBps + r.UpBps)
                .Take(Math.Max(1, top))
                .Where(r => r.DownBps + r.UpBps > 256)
                .ToList()
        };
    }

    private static (double Down, double Up) ActivityWeight(RawConnection connection)
    {
        if (TcpEStats.TryReadCounters(connection, out var bytesIn, out var bytesOut))
            return (Math.Max(1, bytesIn), Math.Max(1, bytesOut));
        return (1, 1);
    }

    private static bool TryReadIo(int pid, out ulong read, out ulong write)
    {
        read = 0;
        write = 0;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return false;
        try
        {
            if (!GetProcessIoCounters(handle, out var io))
                return false;
            read = io.ReadTransferCount;
            write = io.WriteTransferCount;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IoCounters counters);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
