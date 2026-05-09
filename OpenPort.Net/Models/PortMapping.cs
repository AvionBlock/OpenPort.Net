using System.Net;

namespace OpenPort.Net.Models;

/// <summary>
/// Describes a requested or established router port mapping.
/// </summary>
public sealed class PortMapping
{
    /// <summary>
    /// Gets the local port on the internal client.
    /// </summary>
    public int InternalPort { get; init; }

    /// <summary>
    /// Gets the requested or assigned external router port.
    /// </summary>
    public int ExternalPort { get; init; }

    /// <summary>
    /// Gets the transport protocol for the mapping.
    /// </summary>
    public PortProtocol Protocol { get; init; }

    /// <summary>
    /// Gets a human-readable mapping description sent to gateways that support it.
    /// </summary>
    public string Description { get; init; } = "OpenPort.Net";

    /// <summary>
    /// Gets the requested mapping lifetime. Zero means delete for protocols that support lifetime-based deletion.
    /// </summary>
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets the internal client address. When omitted, OpenPort.Net attempts to infer it from the default route.
    /// </summary>
    public IPAddress? InternalAddress { get; init; }
}
