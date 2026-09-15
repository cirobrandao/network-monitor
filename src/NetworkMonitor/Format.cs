using System.Globalization;

namespace NetworkMonitor;

internal static class Format
{
    public static string Rate(double bytesPerSecond)
    {
        var value = Math.Max(0, bytesPerSecond);
        return value switch
        {
            >= 1024d * 1024d * 1024d => string.Format(CultureInfo.InvariantCulture, "{0:0.00} GB/s", value / (1024d * 1024d * 1024d)),
            >= 1024d * 1024d => string.Format(CultureInfo.InvariantCulture, "{0:0.00} MB/s", value / (1024d * 1024d)),
            >= 1024d => string.Format(CultureInfo.InvariantCulture, "{0:0.0} KB/s", value / 1024d),
            _ => string.Format(CultureInfo.InvariantCulture, "{0:0} B/s", value)
        };
    }

    public static string State(string state) => state switch
    {
        "ESTABLISHED" => "Estabelecida",
        "LISTEN" => "Escutando",
        "SYN_SENT" => "Conectando",
        "SYN_RECEIVED" => "Sincronizando",
        "FIN_WAIT_1" => "Encerrando",
        "FIN_WAIT_2" => "Encerrando",
        "CLOSE_WAIT" => "Close Wait",
        "CLOSING" => "Fechando",
        "LAST_ACK" => "Last Ack",
        "TIME_WAIT" => "Time Wait",
        "CLOSED" => "Fechada",
        "BIND" => "Bind",
        _ => state
    };

    public static string Scope(AddressScope scope) => scope switch
    {
        AddressScope.Public => "Internet",
        AddressScope.Private => "Rede local",
        AddressScope.Loopback => "Loopback",
        AddressScope.LinkLocal => "Link-local",
        AddressScope.Multicast => "Multicast",
        _ => "—"
    };

    public static AddressScope Classify(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip is "0.0.0.0" or "::" or "::0")
            return AddressScope.Unspecified;

        if (!System.Net.IPAddress.TryParse(ip, out var addr))
            return AddressScope.Public;

        if (System.Net.IPAddress.IsLoopback(addr))
            return AddressScope.Loopback;

        if (addr.IsIPv6LinkLocal)
            return AddressScope.LinkLocal;

        if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = addr.GetAddressBytes();
            if (b[0] == 10) return AddressScope.Private;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return AddressScope.Private;
            if (b[0] == 192 && b[1] == 168) return AddressScope.Private;
            if (b[0] == 169 && b[1] == 254) return AddressScope.LinkLocal;
            if (b[0] >= 224) return AddressScope.Multicast;
            return AddressScope.Public;
        }

        var bytes = addr.GetAddressBytes();
        if ((bytes[0] & 0xFE) == 0xFC) return AddressScope.Private;
        if (addr.IsIPv6Multicast) return AddressScope.Multicast;
        return AddressScope.Public;
    }
}
