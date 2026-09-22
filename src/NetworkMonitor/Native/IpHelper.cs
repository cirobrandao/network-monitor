using System.Net;
using System.Runtime.InteropServices;

namespace NetworkMonitor.Native;

internal readonly struct RawConnection
{
    public readonly NetProtocol Protocol;
    public readonly string State;
    public readonly int Pid;
    public readonly string LocalAddress;
    public readonly int LocalPort;
    public readonly string RemoteAddress;
    public readonly int RemotePort;

    /// <summary>
    /// Raw MIB_TCPROW fields (network byte order, IPv4 only) used to look up
    /// per-connection extended TCP statistics (see TcpEStats). Zero when not
    /// applicable (UDP rows or IPv6 rows).
    /// </summary>
    public readonly uint RawState;
    public readonly uint RawLocalAddr;
    public readonly uint RawLocalPort;
    public readonly uint RawRemoteAddr;
    public readonly uint RawRemotePort;
    public readonly bool IsIPv4;

    public RawConnection(
        NetProtocol protocol,
        string state,
        int pid,
        string localAddress,
        int localPort,
        string remoteAddress,
        int remotePort,
        uint rawState = 0,
        uint rawLocalAddr = 0,
        uint rawLocalPort = 0,
        uint rawRemoteAddr = 0,
        uint rawRemotePort = 0,
        bool isIPv4 = false)
    {
        Protocol = protocol;
        State = state;
        Pid = pid;
        LocalAddress = localAddress;
        LocalPort = localPort;
        RemoteAddress = remoteAddress;
        RemotePort = remotePort;
        RawState = rawState;
        RawLocalAddr = rawLocalAddr;
        RawLocalPort = rawLocalPort;
        RawRemoteAddr = rawRemoteAddr;
        RawRemotePort = rawRemotePort;
        IsIPv4 = isIPv4;
    }

    public string Key => $"{Protocol}|{Pid}|{LocalAddress}:{LocalPort}|{RemoteAddress}:{RemotePort}";
}

internal static class IpHelper
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;
    private const uint ErrorInsufficientBuffer = 122;
    private const uint NoError = 0;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        bool order,
        int ipVersion,
        int tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr udpTable,
        ref int size,
        bool order,
        int ipVersion,
        int tableClass,
        uint reserved);

    public static List<RawConnection> GetAll(bool includeUdp)
    {
        var list = new List<RawConnection>(256);
        ReadTcp(AfInet, list);
        ReadTcp(AfInet6, list);
        if (includeUdp)
        {
            ReadUdp(AfInet, list);
            ReadUdp(AfInet6, list);
        }
        return list;
    }

    private static void ReadTcp(int family, List<RawConnection> output)
    {
        if (!ReadTable(family, tcp: true, out var buffer, out var count))
            return;

        try
        {
            var rowSize = family == AfInet ? 24 : 56;
            var row = buffer + 4;
            for (var i = 0; i < count; i++)
            {
                if (family == AfInet)
                {
                    var state = (uint)Marshal.ReadInt32(row, 0);
                    var localAddr = (uint)Marshal.ReadInt32(row, 4);
                    var localPort = (uint)Marshal.ReadInt32(row, 8);
                    var remoteAddr = (uint)Marshal.ReadInt32(row, 12);
                    var remotePort = (uint)Marshal.ReadInt32(row, 16);
                    var pid = Marshal.ReadInt32(row, 20);
                    output.Add(new RawConnection(
                        NetProtocol.Tcp,
                        TcpStateName(state),
                        pid,
                        ToIPv4(localAddr),
                        ToPort(localPort),
                        NormalizeRemote(ToIPv4(remoteAddr)),
                        ToPort(remotePort),
                        rawState: state,
                        rawLocalAddr: localAddr,
                        rawLocalPort: localPort,
                        rawRemoteAddr: remoteAddr,
                        rawRemotePort: remotePort,
                        isIPv4: true));
                }
                else
                {
                    var localBytes = new byte[16];
                    var remoteBytes = new byte[16];
                    Marshal.Copy(row, localBytes, 0, 16);
                    Marshal.Copy(row + 24, remoteBytes, 0, 16);
                    var localPort = (uint)Marshal.ReadInt32(row, 20);
                    var remotePort = (uint)Marshal.ReadInt32(row, 44);
                    var state = (uint)Marshal.ReadInt32(row, 48);
                    var pid = Marshal.ReadInt32(row, 52);
                    output.Add(new RawConnection(
                        NetProtocol.Tcp,
                        TcpStateName(state),
                        pid,
                        ToIP(localBytes),
                        ToPort(localPort),
                        NormalizeRemote(ToIP(remoteBytes)),
                        ToPort(remotePort)));
                }

                row += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ReadUdp(int family, List<RawConnection> output)
    {
        if (!ReadTable(family, tcp: false, out var buffer, out var count))
            return;

        try
        {
            var rowSize = family == AfInet ? 12 : 28;
            var row = buffer + 4;
            for (var i = 0; i < count; i++)
            {
                if (family == AfInet)
                {
                    var localAddr = (uint)Marshal.ReadInt32(row, 0);
                    var localPort = (uint)Marshal.ReadInt32(row, 4);
                    var pid = Marshal.ReadInt32(row, 8);
                    output.Add(new RawConnection(
                        NetProtocol.Udp,
                        "BIND",
                        pid,
                        ToIPv4(localAddr),
                        ToPort(localPort),
                        "",
                        0));
                }
                else
                {
                    var localBytes = new byte[16];
                    Marshal.Copy(row, localBytes, 0, 16);
                    var localPort = (uint)Marshal.ReadInt32(row, 20);
                    var pid = Marshal.ReadInt32(row, 24);
                    output.Add(new RawConnection(
                        NetProtocol.Udp,
                        "BIND",
                        pid,
                        ToIP(localBytes),
                        ToPort(localPort),
                        "",
                        0));
                }

                row += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool ReadTable(int family, bool tcp, out IntPtr buffer, out int count)
    {
        buffer = IntPtr.Zero;
        count = 0;
        var size = 0;
        uint result;
        if (tcp)
            result = GetExtendedTcpTable(IntPtr.Zero, ref size, true, family, TcpTableOwnerPidAll, 0);
        else
            result = GetExtendedUdpTable(IntPtr.Zero, ref size, true, family, UdpTableOwnerPid, 0);

        if (result != ErrorInsufficientBuffer && result != NoError)
            return false;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            buffer = Marshal.AllocHGlobal(size);
            result = tcp
                ? GetExtendedTcpTable(buffer, ref size, true, family, TcpTableOwnerPidAll, 0)
                : GetExtendedUdpTable(buffer, ref size, true, family, UdpTableOwnerPid, 0);

            if (result == NoError)
            {
                count = Marshal.ReadInt32(buffer);
                return true;
            }

            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
            if (result != ErrorInsufficientBuffer)
                return false;
        }

        return false;
    }

    private static int ToPort(uint value) => (int)(((value & 0xFF) << 8) | ((value & 0xFF00) >> 8));

    private static string ToIPv4(uint addr)
    {
        var bytes = BitConverter.GetBytes(addr);
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
    }

    private static string ToIP(byte[] bytes)
    {
        var ip = new IPAddress(bytes);
        if (ip.IsIPv4MappedToIPv6)
            return ip.MapToIPv4().ToString();
        return ip.ToString();
    }

    private static string NormalizeRemote(string ip)
        => ip is "0.0.0.0" or "::" or "::0" ? "" : ip;

    private static string TcpStateName(uint state) => state switch
    {
        1 => "CLOSED",
        2 => "LISTEN",
        3 => "SYN_SENT",
        4 => "SYN_RECEIVED",
        5 => "ESTABLISHED",
        6 => "FIN_WAIT_1",
        7 => "FIN_WAIT_2",
        8 => "CLOSE_WAIT",
        9 => "CLOSING",
        10 => "LAST_ACK",
        11 => "TIME_WAIT",
        12 => "DELETE_TCB",
        _ => $"STATE_{state}"
    };
}
