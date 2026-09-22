using System.Runtime.InteropServices;

namespace NetworkMonitor.Native;

/// <summary>
/// Wraps the Windows "Extended TCP Statistics" API (iphlpapi.h) so real,
/// per-connection byte counters can be read for TCP sockets. This lets the
/// app show an accurate upload/download speed for each remote IP instead of
/// splitting the process-level rate evenly across every connection.
/// </summary>
/// <remarks>
/// Enabling collection (<see cref="SetPerTcpConnectionEStats"/>) requires
/// administrator privileges; when not elevated, every call fails with
/// ERROR_ACCESS_DENIED and callers should keep using the estimation
/// heuristic in <see cref="Services.ProcessBandwidthService"/>.
/// </remarks>
internal static class TcpEStats
{
    private const int TcpConnectionEstatsData = 1;
    private const uint NoError = 0;
    private const uint ErrorAccessDenied = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TCP_ESTATS_DATA_RW_v0
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool EnableCollection;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TCP_ESTATS_DATA_ROD_v0
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

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SetPerTcpConnectionEStats(
        ref MIB_TCPROW row,
        int estatsType,
        ref TCP_ESTATS_DATA_RW_v0 rw,
        uint rwVersion,
        uint rwSize,
        uint offset);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetPerTcpConnectionEStats(
        ref MIB_TCPROW row,
        int estatsType,
        IntPtr rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        ref TCP_ESTATS_DATA_ROD_v0 rod,
        uint rodVersion,
        uint rodSize);

    /// <summary>
    /// True once a call has failed with an error that indicates the API is
    /// unusable for the lifetime of the process (e.g. not elevated), so we
    /// stop retrying every sampling cycle.
    /// </summary>
    public static bool Unavailable { get; private set; }

    private static readonly HashSet<string> EnabledConnections = new(StringComparer.Ordinal);

    public static bool TryGetDataBytes(RawConnection connection, out ulong bytesIn, out ulong bytesOut)
    {
        bytesIn = 0;
        bytesOut = 0;
        if (Unavailable || !connection.IsIPv4)
            return false;

        var row = new MIB_TCPROW
        {
            dwState = connection.RawState,
            dwLocalAddr = connection.RawLocalAddr,
            dwLocalPort = connection.RawLocalPort,
            dwRemoteAddr = connection.RawRemoteAddr,
            dwRemotePort = connection.RawRemotePort
        };

        var key = $"{connection.RawLocalAddr}:{connection.RawLocalPort}-{connection.RawRemoteAddr}:{connection.RawRemotePort}";
        if (EnabledConnections.Add(key))
        {
            var rw = new TCP_ESTATS_DATA_RW_v0 { EnableCollection = true };
            var setResult = SetPerTcpConnectionEStats(ref row, TcpConnectionEstatsData, ref rw, 0, (uint)Marshal.SizeOf<TCP_ESTATS_DATA_RW_v0>(), 0);
            if (setResult != NoError)
            {
                EnabledConnections.Remove(key);
                if (setResult == ErrorAccessDenied)
                    Unavailable = true;
                return false;
            }
        }

        var rod = new TCP_ESTATS_DATA_ROD_v0();
        var getResult = GetPerTcpConnectionEStats(
            ref row,
            TcpConnectionEstatsData,
            IntPtr.Zero, 0, 0,
            IntPtr.Zero, 0, 0,
            ref rod, 0, (uint)Marshal.SizeOf<TCP_ESTATS_DATA_ROD_v0>());

        if (getResult != NoError)
            return false;

        bytesIn = rod.DataBytesIn;
        bytesOut = rod.DataBytesOut;
        return true;
    }

    public static void ForgetStaleConnections(IReadOnlyCollection<string> activeKeys)
    {
        if (EnabledConnections.Count == 0)
            return;
        EnabledConnections.RemoveWhere(k => !activeKeys.Contains(k));
    }
}
