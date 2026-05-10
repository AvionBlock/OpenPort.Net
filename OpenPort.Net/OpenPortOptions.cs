using OpenPort.Net.Models;
using OpenPort.Net.Providers;
using System.Net;

namespace OpenPort.Net;

/// <summary>
/// Configures provider order, timeout and optional network hints for <see cref="OpenPortClient"/>.
/// </summary>
public sealed class OpenPortOptions
{
    /// <summary>
    /// Gets explicit provider instances used by <see cref="OpenPortClient"/>. When set, this list defines both provider order and behavior.
    /// </summary>
    public IReadOnlyList<IPortMappingProvider>? Providers { get; init; }

    /// <summary>
    /// Gets the protocol order used to create built-in providers when <see cref="Providers"/> is not set.
    /// </summary>
    public IReadOnlyList<PortMappingProtocol> PreferredProtocols { get; init; } =
    [
        PortMappingProtocol.Pcp,
        PortMappingProtocol.NatPmp,
        PortMappingProtocol.UpnpIgd
    ];

    /// <summary>
    /// Gets the per-operation timeout used for discovery and mapping requests.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets an optional default gateway address. When omitted, OpenPort.Net uses the operating system routing table.
    /// </summary>
    public IPAddress? GatewayAddress { get; init; }

    /// <summary>
    /// Gets optional known UPnP root-device description URLs. These are tried before SSDP discovery.
    /// </summary>
    public IReadOnlyList<Uri> UpnpRootDeviceUris { get; init; } = Array.Empty<Uri>();
}
