using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetworkMonitor.Services;

internal sealed class GeoInfo
{
    public string Ip { get; init; } = "";
    public string Country { get; init; } = "";
    public string CountryCode { get; init; } = "";
    public string City { get; init; } = "";
    public string Asn { get; init; } = "";
    public string Org { get; init; } = "";
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;

    public string Display
    {
        get
        {
            var loc = string.Join(", ", new[] { City, CountryCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(loc) && string.IsNullOrWhiteSpace(Asn))
                return "";
            if (string.IsNullOrWhiteSpace(Asn))
                return loc;
            if (string.IsNullOrWhiteSpace(loc))
                return Asn;
            return loc + " | " + Asn;
        }
    }
}

internal static class GeoIpService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, GeoInfo> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<GeoInfo?>> InFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object DiskLock = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetworkMonitor", "geo-cache.json");

    private static bool _loaded;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("NetworkMonitor/1.2");
        return c;
    }

    public static GeoInfo? TryGetCached(string ip)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(ip) || IsNonPublic(ip))
            return null;
        if (Memory.TryGetValue(ip, out var info) && DateTimeOffset.UtcNow - info.CachedAt < Ttl)
            return info;
        return null;
    }

    public static Task<GeoInfo?> LookupAsync(string ip, CancellationToken ct = default)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(ip) || IsNonPublic(ip))
            return Task.FromResult<GeoInfo?>(null);

        if (Memory.TryGetValue(ip, out var cached) && DateTimeOffset.UtcNow - cached.CachedAt < Ttl)
            return Task.FromResult<GeoInfo?>(cached);

        return InFlight.GetOrAdd(ip, key => LookupCoreAsync(key, ct).ContinueWith(t =>
        {
            InFlight.TryRemove(key, out _);
            return t.Status == TaskStatus.RanToCompletion ? t.Result : null;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
    }

    private static async Task<GeoInfo?> LookupCoreAsync(string ip, CancellationToken ct)
    {
        try
        {
            // ip-api.com: HTTP, sem chave, ate 45 req/min
            var url = $"http://ip-api.com/json/{Uri.EscapeDataString(ip)}?fields=status,message,country,countryCode,city,as,org,query";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var dto = await JsonSerializer.DeserializeAsync<IpApiDto>(stream, cancellationToken: ct).ConfigureAwait(false);
            if (dto is null || !string.Equals(dto.Status, "success", StringComparison.OrdinalIgnoreCase))
                return null;

            var info = new GeoInfo
            {
                Ip = ip,
                Country = dto.Country ?? "",
                CountryCode = dto.CountryCode ?? "",
                City = dto.City ?? "",
                Asn = dto.As ?? "",
                Org = dto.Org ?? "",
                CachedAt = DateTimeOffset.UtcNow
            };
            Memory[ip] = info;
            Persist();
            return info;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (DiskLock)
        {
            if (_loaded) return;
            try
            {
                if (File.Exists(CachePath))
                {
                    var json = File.ReadAllText(CachePath);
                    var items = JsonSerializer.Deserialize<List<GeoInfo>>(json) ?? [];
                    var now = DateTimeOffset.UtcNow;
                    foreach (var item in items)
                    {
                        if (now - item.CachedAt < Ttl && !string.IsNullOrWhiteSpace(item.Ip))
                            Memory[item.Ip] = item;
                    }
                }
            }
            catch { /* ignore */ }
            _loaded = true;
        }
    }

    private static void Persist()
    {
        try
        {
            lock (DiskLock)
            {
                var dir = Path.GetDirectoryName(CachePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var items = Memory.Values
                    .Where(v => DateTimeOffset.UtcNow - v.CachedAt < Ttl)
                    .OrderByDescending(v => v.CachedAt)
                    .Take(2000)
                    .ToList();
                File.WriteAllText(CachePath, JsonSerializer.Serialize(items));
            }
        }
        catch { /* ignore */ }
    }

    public static bool IsNonPublic(string ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var addr))
            return true;
        if (System.Net.IPAddress.IsLoopback(addr))
            return true;
        if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = addr.GetAddressBytes();
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            if (b[0] == 0 || b[0] >= 224) return true;
        }
        return false;
    }

    private sealed class IpApiDto
    {
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("countryCode")] public string? CountryCode { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("as")] public string? As { get; set; }
        [JsonPropertyName("org")] public string? Org { get; set; }
        [JsonPropertyName("query")] public string? Query { get; set; }
    }
}