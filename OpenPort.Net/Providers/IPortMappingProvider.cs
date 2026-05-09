using System.Net;
using OpenPort.Net.Models;

namespace OpenPort.Net.Providers;

/// <summary>
/// Contract implemented by a NAT port mapping provider.
/// </summary>
public interface IPortMappingProvider
{
    /// <summary>
    /// Gets the display name of the provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns whether the provider is available on the current network.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a port mapping.
    /// </summary>
    Task<PortMappingResult> OpenAsync(PortMapping mapping, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a port mapping.
    /// </summary>
    Task<PortMappingResult> CloseAsync(PortMapping mapping, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews an existing port mapping.
    /// </summary>
    Task<PortMappingResult> RenewAsync(PortMapping mapping, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the external IP address when the provider supports it.
    /// </summary>
    Task<IPAddress?> GetExternalIPAddressAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets known mappings when the provider supports enumeration.
    /// </summary>
    Task<IReadOnlyList<PortMapping>> GetMappingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs provider discovery and caches enough state for later operations.
    /// </summary>
    Task<bool> DiscoverAsync(CancellationToken cancellationToken = default);
}
