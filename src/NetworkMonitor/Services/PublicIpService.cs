using System.Net;
using System.Net.Http;

namespace NetworkMonitor.Services;

internal sealed class PublicIpService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "NetworkMonitor/1.0");
        return client;
    }

    private DateTime _nextUtc = DateTime.MinValue;

    public string? Address { get; private set; }

    public async Task RefreshIfDueAsync(bool force = false, CancellationToken token = default)
    {
        if (!force && DateTime.UtcNow < _nextUtc)
            return;

        _nextUtc = DateTime.UtcNow.AddMinutes(5);
        var ip = await TryGetAsync("https://api.ipify.org", token)
                 ?? await TryGetAsync("https://ifconfig.me/ip", token)
                 ?? await TryGetAsync("https://icanhazip.com", token);
        if (ip is not null)
            Address = ip;
        else
            _nextUtc = DateTime.UtcNow.AddSeconds(15);
    }

    private static async Task<string?> TryGetAsync(string url, CancellationToken token)
    {
        try
        {
            var text = (await Http.GetStringAsync(url, token)).Trim();
            if (IPAddress.TryParse(text, out _))
                return text;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // try the next endpoint
        }

        return null;
    }
}
