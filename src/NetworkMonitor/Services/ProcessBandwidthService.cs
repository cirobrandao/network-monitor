using System.Diagnostics;
using System.Net;
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
    private readonly Dictionary<string, (ulong Out, ulong In, long Ticks)> _last = new(StringComparer.Ordinal);
    private bool _disabled;

    public bool IsAvailable => !_disabled;

    public TrafficSnapshot Sample(IEnumerable<RawConnection> connections, int top = 5)
    {
        if (_disabled)
            return TrafficSnapshot.Empty;

        var now = Stopwatch.GetTimestamp();
        var byConn = new Dictionary<string, Rates>(StringComparer.Ordinal);
        var byPid = new Dictionary<int, Rates>();
        var byRemote = new Dictionary<string, Rates>(StringComparer.OrdinalIgnoreCase);
        var attempts = 0;
        var failures = 0;

        foreach (var c in connections)
        {
            if (c.Protocol != NetProtocol.Tcp)
                continue;
            if (string.IsNullOrEmpty(c.RemoteAddress) || c.Pid <= 4)
                continue;
            if (!string.Equals(c.State, "ESTABLISHED", StringComparison.OrdinalIgnoreCase))
                continue;

            attempts++;
            if (!TryReadData(c, out var dataOut, out var dataIn))
            {
                failures++;
                continue;
            }

            var sampleKey = $"{c.Pid}|{c.LocalAddress}|{c.LocalPort}|{c.RemoteAddress}|{c.RemotePort}";
            double down = 0, up = 0;
            if (_last.TryGetValue(sampleKey, out var prev))
            {
                var dt = (now - prev.Ticks) / (double)Stopwatch.Frequency;
                if (dt > 0.05)
                {
                    up = Math.Max(0, (long)(dataOut - prev.Out)) / dt;
                    down = Math.Max(0, (long)(dataIn - prev.In)) / dt;
                }
            }
            _last[sampleKey] = (dataOut, dataIn, now);

            var rates = new Rates(down, up);
            var connKey = $"{c.Protocol}|{c.Pid}|{c.LocalAddress}:{c.LocalPort}|{c.RemoteAddress}:{c.RemotePort}";
            byConn[connKey] = rates;

            byPid[c.Pid] = byPid.TryGetValue(c.Pid, out var pidRates) ? pidRates.Add(rates) : rates;
            byRemote[c.RemoteAddress] = byRemote.TryGetValue(c.RemoteAddress, out var ipRates) ? ipRates.Add(rates) : rates;
        }

        if (attempts > 8 && failures == attempts)
            _disabled = true;

        if (_last.Count > 4000)
            _last.Clear();

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

    private bool TryReadData(RawConnection c, out ulong dataOut, out ulong dataIn)
    {
        dataOut = 0;
        dataIn = 0;
        try
        {
            if (!IPAddress.TryParse(c.LocalAddress, out var local) ||
                !IPAddress.TryParse(c.RemoteAddress, out var remote))
                return false;
            if (local.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                remote.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;

            var row = new MibTcpRow
            {
                State = 5,
                LocalAddr = BitConverter.ToUInt32(local.GetAddressBytes(), 0),
                LocalPort = HostPortToNetwork(c.LocalPort),
                RemoteAddr = BitConverter.ToUInt32(remote.GetAddressBytes(), 0),
                RemotePort = HostPortToNetwork(c.RemotePort)
            };

            var rw = new TcpEstatsDataRw { EnableCollection = 1 };
            var rwSize = Marshal.SizeOf<TcpEstatsDataRw>();
            var rwPtr = Marshal.AllocHGlobal(rwSize);
            try
            {
                Marshal.StructureToPtr(rw, rwPtr, false);
                SetPerTcpConnectionEStats(ref row, TcpEstatsType.Data, rwPtr, (uint)rwSize, IntPtr.Zero, 0, IntPtr.Zero, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(rwPtr);
            }

            var rodSize = Marshal.SizeOf<TcpEstatsDataRod>();
            var rodPtr = Marshal.AllocHGlobal(rodSize);
            try
            {
                var rv = GetPerTcpConnectionEStats(
                    ref row, TcpEstatsType.Data,
                    IntPtr.Zero, 0,
                    IntPtr.Zero, 0,
                    rodPtr, (uint)rodSize);
                if (rv != 0)
                    return false;
                var rod = Marshal.PtrToStructure<TcpEstatsDataRod>(rodPtr);
                dataOut = rod.DataBytesOut;
                dataIn = rod.DataBytesIn;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(rodPtr);
            }
        }
        catch
        {
            return false;
        }
    }

    private static uint HostPortToNetwork(int port)
        => (uint)IPAddress.HostToNetworkOrder((short)port) & 0xFFFF;

    private enum TcpEstatsType { Data = 1 }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpEstatsDataRw
    {
        public byte EnableCollection;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpEstatsDataRod
    {
        public ulong DataBytesOut;
        public ulong DataSegsOut;
        public ulong DataBytesIn;
        public ulong DataSegsIn;
        public ulong SegsOut;
        public ulong SegsIn;
        public uint SoftErrors;
        public uint SoftErrorReason;
        public uint SndUna;
        public uint SndNxt;
        public uint SndMax;
        public ulong ThruBytesAcked;
        public uint RcvNxt;
        public ulong ThruBytesReceived;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetPerTcpConnectionEStats(
        ref MibTcpRow row, TcpEstatsType estatsType,
        IntPtr rw, uint rwVersion, IntPtr ros, uint rosVersion, IntPtr rod, uint rodVersion);

    [DllImport("iphlpapi.dll")]
    private static extern uint SetPerTcpConnectionEStats(
        ref MibTcpRow row, TcpEstatsType estatsType,
        IntPtr rw, uint rwVersion, IntPtr ros, uint rosVersion, IntPtr rod, uint rodVersion);
}
