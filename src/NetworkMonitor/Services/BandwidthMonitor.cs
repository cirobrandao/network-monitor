using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkMonitor.Services;

internal sealed class BandwidthMonitor
{
    private readonly Dictionary<string, Sample> _last = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Sample> _sessionStart = new(StringComparer.Ordinal);

    public BandwidthSnapshot Capture(IReadOnlyCollection<string> disabledIds)
    {
        var now = DateTime.UtcNow;
        var adapters = new List<AdapterRate>();
        double down = 0;
        double up = 0;
        long downBytes = 0;
        long upBytes = 0;
        string adapterName = "—";
        string internalIp = "—";
        var bestTraffic = -1d;

        NetworkInterface[] nics;
        try
        {
            nics = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            return new BandwidthSnapshot { DownBps = 0, UpBps = 0, Adapters = Array.Empty<AdapterRate>() };
        }

        foreach (var nic in nics)
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            IPInterfaceStatistics stats;
            try
            {
                stats = nic.GetIPStatistics();
            }
            catch
            {
                continue;
            }

            var rx = stats.BytesReceived;
            var tx = stats.BytesSent;
            double dbps = 0;
            double ubps = 0;

            if (_last.TryGetValue(nic.Id, out var prev))
            {
                var dt = (now - prev.At).TotalSeconds;
                if (dt > 0.05)
                {
                    dbps = Math.Max(0, (rx - prev.Rx) / dt);
                    ubps = Math.Max(0, (tx - prev.Tx) / dt);
                }
            }

            _last[nic.Id] = new Sample(rx, tx, now);
            var enabled = !disabledIds.Contains(nic.Id);
            var isUp = nic.OperationalStatus == OperationalStatus.Up;
            adapters.Add(new AdapterRate
            {
                Id = nic.Id,
                Name = nic.Name,
                Description = nic.Description,
                DownBps = dbps,
                UpBps = ubps,
                Enabled = enabled,
                IsUp = isUp
            });

            if (!enabled || !isUp)
                continue;

            down += dbps;
            up += ubps;
            var traffic = dbps + ubps;
            if (traffic >= bestTraffic)
            {
                bestTraffic = traffic;
                adapterName = nic.Name;
                internalIp = FirstPrivateIpv4(nic) ?? FirstIpv4(nic) ?? "—";
            }

            if (_sessionStart.TryGetValue(nic.Id, out var start) && rx >= start.Rx && tx >= start.Tx)
            {
                downBytes += rx - start.Rx;
                upBytes += tx - start.Tx;
            }
            else
            {
                _sessionStart[nic.Id] = new Sample(rx, tx, now);
            }
        }

        adapters.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return new BandwidthSnapshot
        {
            DownBps = down,
            UpBps = up,
            DownBytes = downBytes,
            UpBytes = upBytes,
            Adapters = adapters,
            AdapterName = adapterName,
            InternalIp = internalIp
        };
    }

    private static string? FirstPrivateIpv4(NetworkInterface nic)
    {
        foreach (var text in UnicastIpv4(nic))
        {
            if (Format.Classify(text) == AddressScope.Private)
                return text;
        }

        return null;
    }

    private static string? FirstIpv4(NetworkInterface nic)
        => UnicastIpv4(nic).FirstOrDefault();

    private static IEnumerable<string> UnicastIpv4(NetworkInterface nic)
    {
        UnicastIPAddressInformationCollection addresses;
        try
        {
            addresses = nic.GetIPProperties().UnicastAddresses;
        }
        catch
        {
            yield break;
        }

        foreach (var address in addresses)
        {
            if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                continue;
            yield return address.Address.ToString();
        }
    }

    private readonly record struct Sample(long Rx, long Tx, DateTime At);
}
