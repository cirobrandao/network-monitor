using System.Windows.Media;

namespace NetworkMonitor;

public enum NetProtocol
{
    Tcp,
    Udp
}

public enum AddressScope
{
    Unspecified,
    Loopback,
    Private,
    LinkLocal,
    Multicast,
    Public
}

public enum ViewMode
{
    Connections,
    Processes,
    Addresses,
    Closed
}

public sealed class NetConnection
{
    public required NetProtocol Protocol { get; init; }
    public required string State { get; init; }
    public required int Pid { get; init; }
    public required string ProcessName { get; init; }
    public string? ProcessPath { get; init; }
    public ImageSource? Icon { get; init; }
    public required string LocalAddress { get; init; }
    public required int LocalPort { get; init; }
    public required string RemoteAddress { get; init; }
    public required int RemotePort { get; init; }
    public string? HostName { get; init; }
    public required AddressScope Scope { get; init; }

    public string ProtocolText => Protocol == NetProtocol.Tcp ? "TCP" : "UDP";
    public string LocalDisplay => $"{LocalAddress}:{LocalPort}";
    public string RemoteDisplay => string.IsNullOrEmpty(RemoteAddress) ? "—" : $"{RemoteAddress}:{RemotePort}";
    public string HostDisplay => string.IsNullOrWhiteSpace(HostName) ? "—" : HostName;
    public string Key => $"{Protocol}|{Pid}|{LocalDisplay}|{RemoteDisplay}";
}

public sealed class ProcessGroup
{
    public required string Name { get; init; }
    public ImageSource? Icon { get; init; }
    public required string PidSummary { get; init; }
    public required int ConnectionCount { get; init; }
    public required int IpCount { get; init; }
    public required IReadOnlyList<NetConnection> Connections { get; init; }
    public bool IsExpanded { get; set; }
    public string CountLabel => $"{ConnectionCount} conexões · {IpCount} IPs";
}

public sealed class IpGroup
{
    public required string Address { get; init; }
    public string? HostName { get; init; }
    public required AddressScope Scope { get; init; }
    public required int ConnectionCount { get; init; }
    public required string Apps { get; init; }
    public required IReadOnlyList<NetConnection> Connections { get; init; }
    public bool IsExpanded { get; set; }
    public string HostDisplay => string.IsNullOrWhiteSpace(HostName) ? "—" : HostName;
    public string CountLabel => $"{ConnectionCount} conexões · {Apps}";
}

public sealed class ClosedConnection
{
    public required string ProcessName { get; init; }
    public ImageSource? Icon { get; init; }
    public required int Pid { get; init; }
    public required string ProtocolText { get; init; }
    public required string LocalDisplay { get; init; }
    public required string RemoteDisplay { get; init; }
    public required string RemoteAddress { get; init; }
    public string? HostName { get; init; }
    public required AddressScope Scope { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime EndedAt { get; init; }
    public required string EndedLabel { get; init; }
    public required string DurationLabel { get; init; }
    public required string AgoLabel { get; init; }
    public string HostDisplay => string.IsNullOrWhiteSpace(HostName) ? "—" : HostName;
}

public sealed class AdapterRate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required double DownBps { get; init; }
    public required double UpBps { get; init; }
    public required bool Enabled { get; init; }
    public required bool IsUp { get; init; }
}

public sealed class BandwidthSnapshot
{
    public required double DownBps { get; init; }
    public required double UpBps { get; init; }
    public required IReadOnlyList<AdapterRate> Adapters { get; init; }
}

public sealed class AdapterOption : ObservableObject
{
    private bool _enabled = true;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
                AppState.Current.OnAdapterEnabledChanged();
        }
    }
}
