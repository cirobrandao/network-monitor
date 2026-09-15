using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace NetworkMonitor.Services;

internal sealed class DnsServerInfo
{
    public required string Name { get; init; }
    public required string Address { get; init; }
}

internal sealed class DnsProbeSample
{
    public required string Host { get; init; }
    public required bool Ok { get; init; }
    public required double Ms { get; init; }
    public required string Result { get; init; }
}

internal sealed class DnsServerResult
{
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required IReadOnlyList<DnsProbeSample> Samples { get; init; }
    public double? AverageMs { get; init; }
    public required string AverageText { get; init; }
    public required string Status { get; init; }
}

internal static class DnsProbeService
{
    public static IReadOnlyList<DnsServerInfo> DefaultServers()
    {
        var list = new List<DnsServerInfo>
        {
            new() { Name = "Cloudflare", Address = "1.1.1.1" },
            new() { Name = "Google", Address = "8.8.8.8" },
            new() { Name = "Quad9", Address = "9.9.9.9" },
            new() { Name = "OpenDNS", Address = "208.67.222.222" },
            new() { Name = "AdGuard", Address = "94.140.14.14" }
        };

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            foreach (var dns in nic.GetIPProperties().DnsAddresses)
            {
                if (dns.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                var ip = dns.ToString();
                if (list.Any(s => s.Address == ip))
                    continue;
                list.Add(new DnsServerInfo { Name = "Sistema", Address = ip });
            }
        }

        return list;
    }

    public static async Task<DnsServerResult> ProbeServerAsync(
        DnsServerInfo server,
        IReadOnlyList<string> hosts,
        CancellationToken token)
    {
        var samples = new List<DnsProbeSample>(hosts.Count);
        foreach (var host in hosts)
        {
            token.ThrowIfCancellationRequested();
            samples.Add(await ProbeAsync(server.Address, host, token).ConfigureAwait(false));
        }

        var ok = samples.Where(s => s.Ok).ToList();
        var avg = ok.Count > 0 ? ok.Average(s => s.Ms) : (double?)null;
        return new DnsServerResult
        {
            Name = server.Name,
            Address = server.Address,
            Samples = samples,
            AverageMs = avg,
            AverageText = avg is null ? "—" : $"{avg.Value:0} ms",
            Status = ok.Count == samples.Count ? "OK" : ok.Count == 0 ? "Falhou" : $"{ok.Count}/{samples.Count}"
        };
    }

    public static async Task<DnsProbeSample> ProbeAsync(string server, string host, CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 2500;
            udp.Connect(IPAddress.Parse(server), 53);
            var query = BuildQuery(host);
            await udp.SendAsync(query, token).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            linked.CancelAfter(2500);
            var response = await udp.ReceiveAsync(linked.Token).ConfigureAwait(false);
            sw.Stop();
            var ok = response.Buffer.Length >= 12 && (response.Buffer[3] & 0x0F) == 0;
            return new DnsProbeSample
            {
                Host = host,
                Ok = ok,
                Ms = sw.Elapsed.TotalMilliseconds,
                Result = ok ? $"{sw.Elapsed.TotalMilliseconds:0} ms" : "rcode"
            };
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return new DnsProbeSample { Host = host, Ok = false, Ms = 2500, Result = "timeout" };
        }
        catch
        {
            sw.Stop();
            return new DnsProbeSample { Host = host, Ok = false, Ms = sw.Elapsed.TotalMilliseconds, Result = "falhou" };
        }
    }

    private static byte[] BuildQuery(string host)
    {
        var id = (ushort)Random.Shared.Next(1, 65535);
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((byte)(id >> 8));
        writer.Write((byte)(id & 0xFF));
        writer.Write((byte)0x01);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x01);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        foreach (var label in host.Trim('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }
        writer.Write((byte)0);
        writer.Write((byte)0x00);
        writer.Write((byte)0x01);
        writer.Write((byte)0x00);
        writer.Write((byte)0x01);
        return ms.ToArray();
    }
}
