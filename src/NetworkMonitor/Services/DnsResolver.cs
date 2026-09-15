using System.Collections.Concurrent;
using System.Net;

namespace NetworkMonitor.Services;

internal sealed class DnsResolver
{
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _inflight = new(StringComparer.Ordinal);

    public string? Lookup(string ip)
        => _cache.TryGetValue(ip, out var host) ? host : null;

    public void Request(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return;
        if (_cache.ContainsKey(ip) || !_inflight.TryAdd(ip, 0))
            return;

        _ = ResolveAsync(ip);
    }

    private async Task ResolveAsync(string ip)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2.5));
            var entry = await Dns.GetHostEntryAsync(ip, cts.Token).ConfigureAwait(false);
            var host = entry.HostName;
            if (string.IsNullOrWhiteSpace(host) || string.Equals(host, ip, StringComparison.OrdinalIgnoreCase))
                _cache[ip] = null;
            else
                _cache[ip] = host;
        }
        catch
        {
            _cache[ip] = null;
        }
        finally
        {
            _inflight.TryRemove(ip, out _);
        }
    }
}
