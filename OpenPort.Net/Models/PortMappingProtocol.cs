namespace OpenPort.Net.Models;

/// <summary>
/// NAT traversal protocol used by a provider.
/// </summary>
public enum PortMappingProtocol
{
    /// <summary>
    /// Port Control Protocol.
    /// </summary>
    Pcp,

    /// <summary>
    /// NAT Port Mapping Protocol.
    /// </summary>
    NatPmp,

    /// <summary>
    /// UPnP Internet Gateway Device.
    /// </summary>
    UpnpIgd
}
