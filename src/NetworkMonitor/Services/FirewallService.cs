using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace NetworkMonitor.Services;

internal static class FirewallService
{
    public const string Prefix = "NetworkMonitor";

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static string? LastError { get; private set; }

    public static bool BlockProgram(string path)
        => AddRule($"{Prefix}-App-{Hash(path)}", $"Bloqueio app {PathName(path)}", path, remoteIp: null);

    public static bool UnblockProgram(string path)
        => DeleteRule($"{Prefix}-App-{Hash(path)}");

    public static bool BlockAddress(string ip)
        => AddRule($"{Prefix}-IP-{ip}", $"Bloqueio IP {ip}", program: null, ip);

    public static bool UnblockAddress(string ip)
        => DeleteRule($"{Prefix}-IP-{ip}");

    public static bool BlockProgramAddress(string path, string ip)
        => AddRule($"{Prefix}-AppIP-{Hash(path)}-{ip}", $"Bloqueio {PathName(path)} → {ip}", path, ip);

    public static bool UnblockProgramAddress(string path, string ip)
        => DeleteRule($"{Prefix}-AppIP-{Hash(path)}-{ip}");

    private static bool AddRule(string name, string description, string? program, string? remoteIp)
    {
        LastError = null;
        DeleteRule(name);
        var args = new StringBuilder();
        args.Append("advfirewall firewall add rule ");
        args.Append("name=\"").Append(name).Append("\" ");
        args.Append("description=\"").Append(description.Replace("\"", "'")).Append("\" ");
        args.Append("dir=out action=block enable=yes profile=any ");
        if (!string.IsNullOrWhiteSpace(program))
            args.Append("program=\"").Append(program).Append("\" ");
        if (!string.IsNullOrWhiteSpace(remoteIp))
            args.Append("remoteip=").Append(remoteIp).Append(' ');
        return RunNetsh(args.ToString());
    }

    private static bool DeleteRule(string name)
    {
        LastError = null;
        return RunNetsh($"advfirewall firewall delete rule name=\"{name}\"", ignoreFailure: true);
    }

    private static bool RunNetsh(string arguments, bool ignoreFailure = false)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(info);
            if (process is null)
            {
                LastError = "Não foi possível iniciar o netsh.";
                return false;
            }
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(8000);
            if (process.ExitCode == 0 || ignoreFailure)
                return true;
            LastError = string.IsNullOrWhiteSpace(output) ? $"netsh saiu com código {process.ExitCode}." : output.Trim();
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static string Hash(string value)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..12];
    }

    private static string PathName(string path)
        => System.IO.Path.GetFileName(path);
}
