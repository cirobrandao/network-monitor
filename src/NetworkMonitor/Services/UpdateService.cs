using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace NetworkMonitor.Services;

internal sealed class UpdateService
{
    public const string RepoOwner = "cirobrandao";
    public const string RepoName = "network-monitor";
    public const string AssetFileName = "NetworkMonitor-win-x64.zip";

    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("NetworkMonitor", GetCurrentVersionLabel()));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static Version GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v ?? new Version(1, 0, 0, 0);
    }

    public static string GetCurrentVersionLabel()
    {
        var v = GetCurrentVersion();
        var build = Math.Max(v.Build, 0);
        return $"{v.Major}.{v.Minor}.{build}";
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(
                $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest",
                ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return UpdateCheckResult.UpToDate(Normalize(GetCurrentVersion()), null);

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, ct);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return UpdateCheckResult.Fail("Resposta invalida do GitHub.");

            if (!TryParseVersion(release.TagName, out var remote))
                return UpdateCheckResult.Fail($"Tag de versao invalida: {release.TagName}");

            var local = Normalize(GetCurrentVersion());
            remote = Normalize(remote);
            if (remote <= local)
                return UpdateCheckResult.UpToDate(local, release.HtmlUrl);

            var asset = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, AssetFileName, StringComparison.OrdinalIgnoreCase));
            if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                return UpdateCheckResult.Fail($"Release {release.TagName} sem o arquivo {AssetFileName}.");

            return UpdateCheckResult.Available(
                local,
                remote,
                release.TagName,
                release.Body ?? string.Empty,
                asset.BrowserDownloadUrl,
                release.HtmlUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Fail(ex.Message);
        }
    }

    public async Task ApplyAsync(
        UpdateCheckResult update,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!update.IsUpdateAvailable || string.IsNullOrWhiteSpace(update.DownloadUrl))
            throw new InvalidOperationException("Nenhuma atualizacao para aplicar.");

        var appDir = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var work = Path.Combine(Path.GetTempPath(), "network-monitor-update-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(work, AssetFileName);
        var extractDir = Path.Combine(work, "extract");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(extractDir);

        try
        {
            await DownloadAsync(update.DownloadUrl!, zipPath, progress, ct);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            var sourceDir = FindPayloadDirectory(extractDir);
            const string exeName = "NetworkMonitor.exe";
            if (!File.Exists(Path.Combine(sourceDir, exeName)))
                throw new InvalidOperationException("O pacote baixado nao contem NetworkMonitor.exe.");

            var scriptPath = Path.Combine(work, "apply-update.cmd");
            var pid = Environment.ProcessId;
            await File.WriteAllTextAsync(scriptPath, BuildApplyScript(pid, sourceDir, appDir, exeName), ct);

            Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                WorkingDirectory = work,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (Application.Current is App app)
                    app.ExitApp();
                else
                    Application.Current.Shutdown();
            });
        }
        catch
        {
            try
            {
                if (Directory.Exists(work))
                    Directory.Delete(work, recursive: true);
            }
            catch
            {
                // ignore cleanup errors
            }

            throw;
        }
    }

    private static async Task DownloadAsync(
        string url,
        string dest,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(dest);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            readTotal += read;
            if (total > 0)
                progress?.Report(readTotal / (double)total);
        }

        progress?.Report(1);
    }

    private static string FindPayloadDirectory(string extractRoot)
    {
        if (File.Exists(Path.Combine(extractRoot, "NetworkMonitor.exe")))
            return extractRoot;

        foreach (var dir in Directory.GetDirectories(extractRoot))
        {
            if (File.Exists(Path.Combine(dir, "NetworkMonitor.exe")))
                return dir;
        }

        return extractRoot;
    }

    private static string BuildApplyScript(int pid, string sourceDir, string targetDir, string exeName)
    {
        return $"""
            @echo off
            setlocal
            :wait
            tasklist /FI "PID eq {pid}" | find "{pid}" >nul
            if not errorlevel 1 (
              timeout /t 1 /nobreak >nul
              goto wait
            )
            timeout /t 1 /nobreak >nul
            xcopy /E /Y /I /Q "{sourceDir}\*" "{targetDir}\" >nul
            start "" "{targetDir}\{exeName}"
            endlocal
            """;
    }

    private static Version Normalize(Version v)
        => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static bool TryParseVersion(string tag, out Version version)
    {
        var t = tag.Trim();
        if (t.StartsWith('v') || t.StartsWith('V'))
            t = t[1..];
        if (Version.TryParse(t, out version!))
            return true;
        return Version.TryParse(t + ".0", out version!);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}

internal sealed class UpdateCheckResult
{
    public bool Ok { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public bool IsUpToDate { get; init; }
    public string? Error { get; init; }
    public Version? CurrentVersion { get; init; }
    public Version? LatestVersion { get; init; }
    public string? TagName { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ReleaseUrl { get; init; }

    public static UpdateCheckResult Available(
        Version current,
        Version latest,
        string tag,
        string notes,
        string downloadUrl,
        string? releaseUrl) => new()
    {
        Ok = true,
        IsUpdateAvailable = true,
        CurrentVersion = current,
        LatestVersion = latest,
        TagName = tag,
        ReleaseNotes = notes,
        DownloadUrl = downloadUrl,
        ReleaseUrl = releaseUrl
    };

    public static UpdateCheckResult UpToDate(Version current, string? releaseUrl) => new()
    {
        Ok = true,
        IsUpToDate = true,
        CurrentVersion = current,
        LatestVersion = current,
        ReleaseUrl = releaseUrl
    };

    public static UpdateCheckResult Fail(string message) => new()
    {
        Ok = false,
        Error = message
    };
}
