using System.Net;

namespace OpenPort.Net.Models;

/// <summary>
/// Describes the outcome of a port mapping operation.
/// </summary>
public sealed class PortMappingResult
{
    /// <summary>
    /// Gets the operation status.
    /// </summary>
    public OpenPortStatus Status { get; init; }

    /// <summary>
    /// Gets the provider that produced the result.
    /// </summary>
    public string Provider { get; init; } = "";

    /// <summary>
    /// Gets the mapping associated with the result. If the gateway changed the external port, this mapping contains the assigned port.
    /// </summary>
    public PortMapping? Mapping { get; init; }

    /// <summary>
    /// Gets the external address reported by the gateway when available.
    /// </summary>
    public IPAddress? ExternalAddress { get; init; }

    /// <summary>
    /// Gets the external port reported by the gateway when available.
    /// </summary>
    public int? ExternalPort { get; init; }

    /// <summary>
    /// Gets a diagnostic message for non-success results.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
