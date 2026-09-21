using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using NetworkMonitor.Native;

namespace NetworkMonitor.Services;

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

internal sealed class ProcessBandwidthService
{
    private readonly Dictionary<string, (ulong Out, ulong In, long Ticks)> _last = new(StringComparer.Ordinal);
    private bool _disabled;

    public bool IsAvailable => !_disabled;

    public IReadOnlyList<ProcessBandwidthRow> Sample(IEnumerable<RawConnection> connections, int top = 5)
    {
        if (_disabled)
            return Array.Empty<ProcessBandwidthRow>();

        var now = Stopwatch.GetTimestamp();
        var perPid = new Dictionary<int, (double Down, double Up)>();
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

            var key = $"{c.Pid}|{c.LocalAddress}|{c.LocalPort}|{c.RemoteAddress}|{c.RemotePort}";
            double down = 0, up = 0;
            if (_last.TryGetValue(key, out var prev))
            {
                var dt = (now - prev.Ticks) / (double)Stopwatch.Frequency;
                if (dt > 0.05)
                {
                    up = Math.Max(0, (long)(dataOut - prev.Out)) / dt;
                    down = Math.Max(0, (long)(dataIn - prev.In)) / dt;
                }
            }
            _last[key] = (dataOut, dataIn, now);

            if (!perPid.TryGetValue(c.Pid, out var agg))
                agg = (0, 0);
            perPid[c.Pid] = (agg.Down + down, agg.Up + up);
        }

        if (attempts > 8 && failures == attempts)
            _disabled = true;

        if (_last.Count > 4000)
            _last.Clear();

        var rows = new List<ProcessBandwidthRow>();
        foreach (var (pid, rates) in perPid)
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
                DownBps = rates.Down,
                UpBps = rates.Up
            });
        }

        return rows
            .OrderByDescending(r => r.DownBps + r.UpBps)
            .Take(Math.Max(1, top))
            .Where(r => r.DownBps + r.UpBps > 256)
            .ToList();
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