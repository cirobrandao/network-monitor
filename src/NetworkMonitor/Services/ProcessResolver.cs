using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NetworkMonitor.Services;

internal sealed class ProcessInfo
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public string? Path { get; init; }
}

internal sealed class ProcessResolver
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private readonly ConcurrentDictionary<int, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    public ProcessInfo Resolve(int pid)
    {
        if (pid <= 0)
            return new ProcessInfo { Pid = pid, Name = "Idle" };
        if (pid == 4)
            return new ProcessInfo { Pid = pid, Name = "System" };

        if (_cache.TryGetValue(pid, out var cached) && (DateTime.UtcNow - cached.At) < TimeSpan.FromSeconds(8))
            return cached.Info;

        var path = QueryPath(pid);
        var name = !string.IsNullOrWhiteSpace(path)
            ? System.IO.Path.GetFileNameWithoutExtension(path)!
            : TryProcessName(pid) ?? $"pid {pid}";

        var info = new ProcessInfo { Pid = pid, Name = name, Path = path };
        _cache[pid] = new CacheEntry(info, DateTime.UtcNow);
        return info;
    }

    public ImageSource? GetIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return _icons.GetOrAdd(path, LoadIcon);
    }

    private static string? TryProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string? QueryPath(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var size = 1024;
            var buffer = new StringBuilder(size);
            if (QueryFullProcessImageName(handle, 0, buffer, ref size))
                return buffer.ToString();
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static ImageSource? LoadIcon(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private readonly record struct CacheEntry(ProcessInfo Info, DateTime At);
}
