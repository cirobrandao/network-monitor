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

public enum ChartKind
{
    Line,
    Area,
    Bar
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
    public string Flag { get; init; } = "";
    public string CountryCode { get; init; } = "";
    public ImageSource? FlagImage { get; init; }
    public string GeoText { get; init; } = "";
    public double DownBps { get; init; }
    public double UpBps { get; init; }

    public string ProtocolText => Protocol == NetProtocol.Tcp ? "TCP" : "UDP";
    public string LocalDisplay => $"{LocalAddress}:{LocalPort}";
    public string RemoteDisplay => string.IsNullOrEmpty(RemoteAddress) ? "—" : $"{RemoteAddress}:{RemotePort}";
    public string HostDisplay => string.IsNullOrWhiteSpace(HostName) ? "—" : HostName;
    public string RateText => Format.RatePair(DownBps, UpBps);
    public string Key => $"{Protocol}|{Pid}|{LocalDisplay}|{RemoteDisplay}";
}

public sealed class ProcessGroup : ObservableObject
{
    private bool _isExpanded;

    public required string Name { get; init; }
    public ImageSource? Icon { get; init; }
    public string? Path { get; init; }
    public required string PidSummary { get; init; }
    public required int ConnectionCount { get; init; }
    public required int IpCount { get; init; }
    public required IReadOnlyList<NetConnection> Connections { get; init; }
    public bool IsBlocked { get; init; }
    public double DownBps { get; init; }
    public double UpBps { get; init; }
    public string CountLabel => $"{ConnectionCount} conexões · {IpCount} IPs";
    public string RateText => Format.RatePair(DownBps, UpBps);
    public string BlockLabel => IsBlocked ? "Desbloquear aplicativo" : "Bloquear aplicativo";
    public string BlockGlyph => IsBlocked ? Glyphs.Unblock : Glyphs.Block;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }
}

public sealed class IpGroup : ObservableObject
{
    private bool _isExpanded;

    public required string Address { get; init; }
    public string? HostName { get; init; }
    public required AddressScope Scope { get; init; }
    public required int ConnectionCount { get; init; }
    public required string Apps { get; init; }
    public required IReadOnlyList<NetConnection> Connections { get; init; }
    public bool IsBlocked { get; init; }
    public string HostDisplay => string.IsNullOrWhiteSpace(HostName) ? "—" : HostName;
    public string Flag { get; init; } = "";
    public string CountryCode { get; init; } = "";
    public ImageSource? FlagImage { get; init; }
    public string GeoText { get; init; } = "";
    public double DownBps { get; init; }
    public double UpBps { get; init; }
    public string CountLabel => $"{ConnectionCount} conexões · {Apps}";
    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(GeoText)) parts.Add(GeoText);
            if (!string.IsNullOrWhiteSpace(HostName)) parts.Add(HostName);
            parts.Add(CountLabel);
            return string.Join(" · ", parts);
        }
    }
    public string RateText => Format.RatePair(DownBps, UpBps);
    public string BlockLabel => IsBlocked ? "Desbloquear IP" : "Bloquear IP";
    public string BlockGlyph => IsBlocked ? Glyphs.Unblock : Glyphs.Block;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }
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
    public string Flag { get; init; } = "";
    public string CountryCode { get; init; } = "";
    public ImageSource? FlagImage { get; init; }
    public string GeoText { get; init; } = "";
}

public sealed class DnsServerRow
{
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required string AverageText { get; init; }
    public required string Status { get; init; }
    public required string SamplesText { get; init; }
}

public sealed class BlockRule
{
    public required string Kind { get; init; }
    public required string Label { get; init; }
    public required string Key { get; init; }
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
    public long DownBytes { get; init; }
    public long UpBytes { get; init; }
    public required IReadOnlyList<AdapterRate> Adapters { get; init; }
    public string AdapterName { get; init; } = "—";
    public string InternalIp { get; init; } = "—";
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

public sealed class ProcessBandwidthItem
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required double DownBps { get; init; }
    public required double UpBps { get; init; }
}