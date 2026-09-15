using System.Net.NetworkInformation;

namespace NetworkMonitor.Services;

internal sealed class BandwidthMonitor
{
    private readonly Dictionary<string, Sample> _last = new(StringComparer.Ordinal);

    public BandwidthSnapshot Capture(IReadOnlyCollection<string> disabledIds)
    {
        var now = DateTime.UtcNow;
        var adapters = new List<AdapterRate>();
        double down = 0;
        double up = 0;

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

            if (enabled && isUp)
            {
                down += dbps;
                up += ubps;
            }
        }

        adapters.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return new BandwidthSnapshot { DownBps = down, UpBps = up, Adapters = adapters };
    }

    private readonly record struct Sample(long Rx, long Tx, DateTime At);
}
