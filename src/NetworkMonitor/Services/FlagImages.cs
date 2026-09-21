using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NetworkMonitor.Services;

internal static class FlagImages
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, ImageSource?> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> InFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetworkMonitor", "flags");

    public static event Action? Changed;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("NetworkMonitor/1.1");
        return c;
    }

    public static ImageSource? Get(string? countryCode)
    {
        if (!IsCode(countryCode))
            return null;
        var cc = countryCode!.ToLowerInvariant();
        if (Memory.TryGetValue(cc, out var cached))
            return cached;

        var path = Path.Combine(Folder, cc + ".png");
        if (File.Exists(path))
        {
            var img = FromFile(path);
            Memory[cc] = img;
            return img;
        }

        EnsureDownload(cc, path);
        return null;
    }

    public static void Prefetch(string? countryCode) => Get(countryCode);

    private static void EnsureDownload(string cc, string path)
    {
        if (!InFlight.TryAdd(cc, 0))
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var url = $"https://flagcdn.com/w40/{cc}.png";
                using var resp = await Http.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return;
                var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length < 32)
                    return;
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                var img = FromFile(path);
                Memory[cc] = img;
                Changed?.Invoke();
            }
            catch
            {
                // keep letter fallback via emoji if download fails
            }
            finally
            {
                InFlight.TryRemove(cc, out _);
            }
        });
    }

    private static ImageSource? FromFile(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsCode(string? countryCode)
        => !string.IsNullOrWhiteSpace(countryCode)
           && countryCode.Length == 2
           && char.IsLetter(countryCode[0])
           && char.IsLetter(countryCode[1]);
}
