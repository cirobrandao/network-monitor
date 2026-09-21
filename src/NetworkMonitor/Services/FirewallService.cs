using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace NetworkMonitor.Services;

internal static class FirewallService
{
    public const string Prefix = "NetworkMonitor";
    private const int ErrorCancelled = 1223;

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
        var add = new StringBuilder();
        add.Append("advfirewall firewall add rule ");
        add.Append("name=\"").Append(name).Append("\" ");
        add.Append("description=\"").Append(description.Replace("\"", "'")).Append("\" ");
        add.Append("dir=out action=block enable=yes profile=any ");
        if (!string.IsNullOrWhiteSpace(program))
            add.Append("program=\"").Append(program).Append("\" ");
        if (!string.IsNullOrWhiteSpace(remoteIp))
            add.Append("remoteip=").Append(remoteIp).Append(' ');

        // Um único processo elevado: delete (ignora falha) + add — evita dois prompts UAC.
        var deleteArgs = $"advfirewall firewall delete rule name=\"{name}\"";
        return RunNetshMutation(deleteArgs, add.ToString());
    }

    private static bool DeleteRule(string name)
    {
        LastError = null;
        return RunNetsh($"advfirewall firewall delete rule name=\"{name}\"", ignoreFailure: true);
    }

    /// <summary>
    /// Executa delete+add numa única elevação quando necessário.
    /// </summary>
    private static bool RunNetshMutation(string deleteArgs, string addArgs)
    {
        if (IsElevated)
        {
            RunNetsh(deleteArgs, ignoreFailure: true);
            return RunNetsh(addArgs, ignoreFailure: false);
        }

        var netsh = Quote(Path.Combine(Environment.SystemDirectory, "netsh.exe"));
        // delete pode falhar se a regra não existir; o exit code final é o do add.
        var cmd = $"{netsh} {deleteArgs} >nul 2>&1 & {netsh} {addArgs}";
        return RunElevatedCmd(cmd, ignoreFailure: false);
    }

    private static bool RunNetsh(string arguments, bool ignoreFailure = false)
    {
        if (IsElevated)
            return RunNetshDirect(arguments, ignoreFailure);

        var netsh = Quote(Path.Combine(Environment.SystemDirectory, "netsh.exe"));
        return RunElevatedCmd($"{netsh} {arguments}", ignoreFailure);
    }

    private static bool RunNetshDirect(string arguments, bool ignoreFailure)
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
            LastError = string.IsNullOrWhiteSpace(output)
                ? $"netsh saiu com código {process.ExitCode}."
                : output.Trim();
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static bool RunElevatedCmd(string commandLine, bool ignoreFailure)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/c " + commandLine,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                ErrorDialog = false
            };
            using var process = Process.Start(info);
            if (process is null)
            {
                LastError = "Não foi possível iniciar o comando elevado.";
                return false;
            }
            if (!process.WaitForExit(20000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                LastError = "Tempo esgotado ao aplicar a regra do firewall (UAC/netsh).";
                return false;
            }
            if (process.ExitCode == 0 || ignoreFailure)
                return true;
            LastError = $"Falha ao alterar o Firewall do Windows (código {process.ExitCode}).";
            return false;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            LastError = "Elevação cancelada no UAC. O bloqueio não foi aplicado.";
            return false;
        }
        catch (Win32Exception ex)
        {
            LastError = ex.NativeErrorCode switch
            {
                5 => "Acesso negado ao Firewall do Windows. Tente novamente e aceite o UAC.",
                _ => $"Falha na elevação (código {ex.NativeErrorCode}): {ex.Message}"
            };
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static string Quote(string path)
        => path.Contains(' ') ? $"\"{path}\"" : path;

    private static string Hash(string value)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..12];
    }

    private static string PathName(string path)
        => System.IO.Path.GetFileName(path);
}
