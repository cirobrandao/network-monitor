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

    public TrafficSnapshot Sample(
        IEnumerable<RawConnection> connections,
        double nicDownBps,
        double nicUpBps,
        int top = 5)
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

        var pidConnCount = established
            .GroupBy(c => c.Pid)
            .ToDictionary(g => g.Key, g => g.Count());
        var byConn = new Dictionary<string, Rates>(StringComparer.Ordinal);
        var byRemote = new Dictionary<string, Rates>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in established)
        {
            if (!byPid.TryGetValue(c.Pid, out var processRate))
                continue;
            var share = processRate.Share(pidConnCount[c.Pid]);
            var connKey = $"{c.Protocol}|{c.Pid}|{c.LocalAddress}:{c.LocalPort}|{c.RemoteAddress}:{c.RemotePort}";
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
