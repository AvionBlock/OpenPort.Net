using System.Net;
using System.Collections.Concurrent;
using System.Net.Sockets;
using OpenPort.Net.Models;
using OpenPort.Net.Providers;

namespace OpenPort.Net;

/// <summary>
/// High-level client for discovering NAT gateways and opening, renewing, closing and inspecting port mappings.
/// </summary>
public sealed class OpenPortClient
{
    private readonly OpenPortOptions _options;
    private readonly List<IPortMappingProvider> _providers;
    private readonly Dictionary<PortMappingProtocol, IPortMappingProvider> _providerByProtocol;
    private readonly ConcurrentDictionary<string, IPortMappingProvider> _activeMappings = new();

    /// <summary>
    /// Creates a client using the default provider order: PCP, NAT-PMP, then UPnP IGD.
    /// </summary>
    public OpenPortClient()
        : this(new OpenPortOptions())
    {
    }

    /// <summary>
    /// Creates a client using the supplied options.
    /// </summary>
    public OpenPortClient(OpenPortOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.Providers is not null)
        {
            _providers = options.Providers.ToList();
            _providerByProtocol = new Dictionary<PortMappingProtocol, IPortMappingProvider>();
            return;
        }

        var gatewayEndPoint = options.GatewayAddress is null ? null : new IPEndPoint(options.GatewayAddress, 5351);
        _providerByProtocol = new Dictionary<PortMappingProtocol, IPortMappingProvider>
        {
            [PortMappingProtocol.Pcp] = new PcpProvider(options.Timeout, gatewayEndPoint),
            [PortMappingProtocol.NatPmp] = new NatPmpProvider(options.Timeout, gatewayEndPoint),
            [PortMappingProtocol.UpnpIgd] = new UpnpIgdProvider(options.Timeout, options.UpnpRootDeviceUris)
        };

        _providers = _options.PreferredProtocols
            .Where(_providerByProtocol.ContainsKey)
            .Select(protocol => _providerByProtocol[protocol])
            .ToList();
    }

    /// <summary>
    /// Creates a client with explicit providers. This is useful for tests and custom provider implementations.
    /// </summary>
    public OpenPortClient(IEnumerable<IPortMappingProvider> providers)
        : this(new OpenPortOptions(), providers)
    {
    }

    /// <summary>
    /// Creates a client with explicit providers and client options.
    /// </summary>
    public OpenPortClient(OpenPortOptions options, IEnumerable<IPortMappingProvider> providers)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        _providerByProtocol = new Dictionary<PortMappingProtocol, IPortMappingProvider>();
    }

    /// <summary>
    /// Discovers all currently available providers in configured order.
    /// </summary>
    public async Task<IReadOnlyList<IPortMappingProvider>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CreateTimeout(cancellationToken);
        var available = new List<IPortMappingProvider>();

        foreach (var provider in _providers)
        {
            try
            {
                if (await provider.DiscoverAsync(timeout.Token).ConfigureAwait(false))
                {
                    available.Add(provider);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or SocketException)
            {
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (OperationCanceledException) when (timeout.Token.IsCancellationRequested)
            {
                break;
            }
        }

        return available;
    }

    /// <summary>
    /// Opens a port mapping using the first available provider that succeeds.
    /// </summary>
    public async Task<PortMappingResult> OpenAsync(PortMapping mapping, CancellationToken cancellationToken = default)
    {
        ValidateMapping(mapping);
        using var timeout = CreateTimeout(cancellationToken);
        PortMappingResult? lastResult = null;

        foreach (var provider in _providers)
        {
            lastResult = await TryProviderAsync(provider, mapping, p => p.OpenAsync(mapping, timeout.Token), timeout.Token)
                .ConfigureAwait(false);

            if (lastResult.Status is OpenPortStatus.Success or OpenPortStatus.ExternalPortChanged)
            {
                if (lastResult.Mapping is not null)
                {
                    BindMapping(provider, mapping, lastResult.Mapping);
                }

                return lastResult;
            }
        }

        return lastResult ?? Failure(OpenPortStatus.GatewayNotFound, "No port mapping providers are configured.");
    }

    /// <summary>
    /// Opens a mapping and returns a lease that can renew and close it automatically.
    /// </summary>
    public async Task<OpenPortLease> OpenLeaseAsync(
        PortMapping mapping,
        bool autoRenew = true,
        CancellationToken cancellationToken = default)
    {
        var result = await OpenAsync(mapping, cancellationToken).ConfigureAwait(false);
        if (result.Status is not (OpenPortStatus.Success or OpenPortStatus.ExternalPortChanged))
        {
            throw new InvalidOperationException(result.ErrorMessage ?? $"Port mapping failed with status {result.Status}.");
        }

        return new OpenPortLease(this, result, autoRenew);
    }

    /// <summary>
    /// Closes a port mapping. Mappings opened by this client are closed through the same provider that opened them.
    /// </summary>
    public async Task<PortMappingResult> CloseAsync(PortMapping mapping, CancellationToken cancellationToken = default)
    {
        ValidateMapping(mapping);
        using var timeout = CreateTimeout(cancellationToken);
        var key = CreateKey(mapping);

        if (_activeMappings.TryGetValue(key, out var activeProvider))
        {
            var activeResult = await TryProviderAsync(activeProvider, mapping, p => p.CloseAsync(mapping, timeout.Token), timeout.Token)
                .ConfigureAwait(false);
            if (activeResult.Status == OpenPortStatus.Success)
            {
                _activeMappings.TryRemove(key, out _);
            }

            return activeResult;
        }

        PortMappingResult? lastResult = null;

        foreach (var provider in _providers)
        {
            lastResult = await TryProviderAsync(provider, mapping, p => p.CloseAsync(mapping, timeout.Token), timeout.Token)
                .ConfigureAwait(false);

            if (lastResult.Status == OpenPortStatus.Success)
            {
                _activeMappings.TryRemove(key, out _);
                return lastResult;
            }
        }

        return lastResult ?? Failure(OpenPortStatus.GatewayNotFound, "No port mapping providers are configured.");
    }

    /// <summary>
    /// Closes the mapping contained in a previous successful result.
    /// </summary>
    public Task<PortMappingResult> CloseAsync(PortMappingResult result, CancellationToken cancellationToken = default)
    {
        if (result.Mapping is null)
        {
            throw new ArgumentException("The result does not contain a mapping.", nameof(result));
        }

        return CloseAsync(result.Mapping, cancellationToken);
    }

    /// <summary>
    /// Renews a port mapping. Mappings opened by this client are renewed through the same provider that opened them.
    /// </summary>
    public async Task<PortMappingResult> RenewAsync(PortMapping mapping, CancellationToken cancellationToken = default)
    {
        ValidateMapping(mapping);
        using var timeout = CreateTimeout(cancellationToken);
        var key = CreateKey(mapping);

        if (_activeMappings.TryGetValue(key, out var activeProvider))
        {
            var activeResult = await TryProviderAsync(activeProvider, mapping, p => p.RenewAsync(mapping, timeout.Token), timeout.Token)
                .ConfigureAwait(false);
            if (activeResult.Status is OpenPortStatus.Success or OpenPortStatus.ExternalPortChanged)
            {
                if (activeResult.Mapping is not null)
                {
                    BindMapping(activeProvider, mapping, activeResult.Mapping);
                }
            }

            return activeResult;
        }

        PortMappingResult? lastResult = null;

        foreach (var provider in _providers)
        {
            lastResult = await TryProviderAsync(provider, mapping, p => p.RenewAsync(mapping, timeout.Token), timeout.Token)
                .ConfigureAwait(false);

            if (lastResult.Status is OpenPortStatus.Success or OpenPortStatus.ExternalPortChanged)
            {
                if (lastResult.Mapping is not null)
                {
                    BindMapping(provider, mapping, lastResult.Mapping);
                }

                return lastResult;
            }
        }

        return lastResult ?? Failure(OpenPortStatus.GatewayNotFound, "No port mapping providers are configured.");
    }

    /// <summary>
    /// Renews the mapping contained in a previous successful result.
    /// </summary>
    public Task<PortMappingResult> RenewAsync(PortMappingResult result, CancellationToken cancellationToken = default)
    {
        if (result.Mapping is null)
        {
            throw new ArgumentException("The result does not contain a mapping.", nameof(result));
        }

        return RenewAsync(result.Mapping, cancellationToken);
    }

    /// <summary>
    /// Gets the external IP address reported by the first provider that can supply it.
    /// </summary>
    public async Task<IPAddress?> GetExternalIPAddressAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CreateTimeout(cancellationToken);

        foreach (var provider in _providers)
        {
            try
            {
                if (!await provider.IsAvailableAsync(timeout.Token).ConfigureAwait(false))
                {
                    continue;
                }

                var address = await provider.GetExternalIPAddressAsync(timeout.Token).ConfigureAwait(false);
                if (address is not null)
                {
                    return address;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or SocketException)
            {
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            catch (OperationCanceledException) when (timeout.Token.IsCancellationRequested)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets known port mappings when the selected provider supports enumeration.
    /// </summary>
    public async Task<IReadOnlyList<PortMapping>> GetMappingsAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CreateTimeout(cancellationToken);

        foreach (var provider in _providers)
        {
            try
            {
                if (!await provider.IsAvailableAsync(timeout.Token).ConfigureAwait(false))
                {
                    continue;
                }

                var mappings = await provider.GetMappingsAsync(timeout.Token).ConfigureAwait(false);
                if (mappings.Count > 0)
                {
                    return mappings;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or SocketException)
            {
            }
            catch (TaskCanceledException)
            {
                return Array.Empty<PortMapping>();
            }
            catch (OperationCanceledException) when (timeout.Token.IsCancellationRequested)
            {
                return Array.Empty<PortMapping>();
            }
        }

        return Array.Empty<PortMapping>();
    }

    private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(_options.Timeout);
        return source;
    }

    private static async Task<PortMappingResult> TryProviderAsync(
        IPortMappingProvider provider,
        PortMapping mapping,
        Func<IPortMappingProvider, Task<PortMappingResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
            {
                return Failure(OpenPortStatus.NotSupported, $"{provider.Name} is not available.", provider.Name, mapping);
            }

            return await operation(provider).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(OpenPortStatus.Timeout, $"{provider.Name} timed out.", provider.Name, mapping);
        }
        catch (TaskCanceledException)
        {
            return Failure(OpenPortStatus.Timeout, $"{provider.Name} timed out.", provider.Name, mapping);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or SocketException)
        {
            return Failure(OpenPortStatus.Failed, ex.Message, provider.Name, mapping);
        }
    }

    private static void ValidateMapping(PortMapping mapping)
    {
        if (mapping is null)
        {
            throw new ArgumentNullException(nameof(mapping));
        }

        ValidatePort(mapping.InternalPort, nameof(mapping.InternalPort));
        ValidatePort(mapping.ExternalPort, nameof(mapping.ExternalPort));

        if (mapping.Lifetime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(mapping), "Lifetime cannot be negative.");
        }
    }

    private static void ValidatePort(int port, string name)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(name, "Port must be in range 1..65535.");
        }
    }

    private static PortMappingResult Failure(OpenPortStatus status, string message, string provider = "", PortMapping? mapping = null) =>
        new()
        {
            Status = status,
            Provider = provider,
            Mapping = mapping,
            ErrorMessage = message
        };

    private static string CreateKey(PortMapping mapping) =>
        $"{mapping.Protocol}:{mapping.InternalAddress?.ToString() ?? "*"}:{mapping.InternalPort}:{mapping.ExternalPort}";

    private void BindMapping(IPortMappingProvider provider, PortMapping requestedMapping, PortMapping actualMapping)
    {
        _activeMappings[CreateKey(requestedMapping)] = provider;
        _activeMappings[CreateKey(actualMapping)] = provider;
    }
}
