using System.Net;
using System.Collections.Concurrent;
using OpenPort.Net.Discovery;
using OpenPort.Net.Internal;
using OpenPort.Net.Models;

namespace OpenPort.Net.Providers;

/// <summary>
/// PCP provider for gateways that implement PCP version 2 MAP and ANNOUNCE operations.
/// </summary>
public sealed class PcpProvider : IPortMappingProvider
{
    private const int PcpPort = 5351;
    private readonly GatewayDiscovery _gatewayDiscovery = new();
    private readonly UdpRequester _udpRequester;
    private readonly IPEndPoint? _configuredGatewayEndPoint;
    private readonly ConcurrentDictionary<string, byte[]> _mappingNonces = new();
    private IPEndPoint? _gatewayEndPoint;

    /// <summary>
    /// Creates a PCP provider using automatic gateway discovery.
    /// </summary>
    public PcpProvider(TimeSpan timeout)
        : this(timeout, (IPEndPoint?)null)
    {
    }

    /// <summary>
    /// Creates a PCP provider that targets a specific gateway address on UDP port 5351.
    /// </summary>
    public PcpProvider(TimeSpan timeout, IPAddress gatewayAddress)
        : this(timeout, new IPEndPoint(gatewayAddress, PcpPort))
    {
    }

    /// <summary>
    /// Creates a PCP provider that targets a specific gateway endpoint.
    /// </summary>
    public PcpProvider(TimeSpan timeout, IPEndPoint? gatewayEndPoint)
    {
        _udpRequester = new UdpRequester(timeout);
        _configuredGatewayEndPoint = gatewayEndPoint;
        _gatewayEndPoint = gatewayEndPoint;
    }

    /// <inheritdoc />
    public string Name => "PCP";

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _gatewayEndPoint is not null || await DiscoverAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        _gatewayEndPoint ??= GetGatewayEndPoint();
        if (_gatewayEndPoint is null)
        {
            return false;
        }

        var request = PcpMessage.CreateAnnounceRequest(GetClientAddress(null));
        var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response is not null && PcpMessage.IsSuccessAnnounceResponse(response);
    }

    /// <inheritdoc />
    public async Task<PortMappingResult> OpenAsync(PortMapping mapping, CancellationToken cancellationToken = default) =>
        await MapAsync(mapping, NetworkUtils.ToSeconds(mapping.Lifetime), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<PortMappingResult> CloseAsync(PortMapping mapping, CancellationToken cancellationToken = default) =>
        await MapAsync(mapping, 0, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<PortMappingResult> RenewAsync(PortMapping mapping, CancellationToken cancellationToken = default) =>
        OpenAsync(mapping, cancellationToken);

    /// <inheritdoc />
    public Task<IPAddress?> GetExternalIPAddressAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IPAddress?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<PortMapping>> GetMappingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PortMapping>>(Array.Empty<PortMapping>());

    private async Task<PortMappingResult> MapAsync(PortMapping mapping, uint lifetimeSeconds, CancellationToken cancellationToken)
    {
        _gatewayEndPoint ??= GetGatewayEndPoint();
        if (_gatewayEndPoint is null)
        {
            return Result(OpenPortStatus.GatewayNotFound, mapping, "Default gateway was not found.");
        }

        var nonceKey = CreateNonceKey(mapping);
        var nonce = lifetimeSeconds == 0 && _mappingNonces.TryGetValue(nonceKey, out var existingNonce)
            ? existingNonce
            : _mappingNonces.GetOrAdd(nonceKey, _ => PcpMessage.CreateNonce());

        var request = PcpMessage.CreateMapRequest(
            mapping.Protocol,
            mapping.InternalPort,
            mapping.ExternalPort,
            lifetimeSeconds,
            GetClientAddress(mapping.InternalAddress),
            nonce);
        var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return Result(OpenPortStatus.Timeout, mapping, "PCP gateway did not respond.");
        }

        if (!PcpMessage.TryParseMapResponse(response, out var mapResponse))
        {
            return Result(OpenPortStatus.InvalidRequest, mapping, "Invalid PCP MAP response.");
        }

        if (mapResponse.ResultCode != 0)
        {
            return Result(
                PcpMessage.MapResultCode(mapResponse.ResultCode),
                mapping,
                $"PCP result code {mapResponse.ResultCode} ({PcpMessage.GetResultName(mapResponse.ResultCode)}).");
        }

        var actualMapping = PortMappingCopy.WithExternalPort(mapping, mapResponse.ExternalPort);
        var status = mapResponse.ExternalPort == mapping.ExternalPort ? OpenPortStatus.Success : OpenPortStatus.ExternalPortChanged;
        if (lifetimeSeconds == 0)
        {
            _mappingNonces.TryRemove(nonceKey, out _);
        }
        else
        {
            _mappingNonces[CreateNonceKey(actualMapping)] = nonce;
            if (!string.Equals(nonceKey, CreateNonceKey(actualMapping), StringComparison.Ordinal))
            {
                _mappingNonces.TryRemove(nonceKey, out _);
            }
        }

        return new PortMappingResult
        {
            Status = status,
            Provider = Name,
            Mapping = actualMapping,
            ExternalPort = mapResponse.ExternalPort,
            ExternalAddress = mapResponse.ExternalAddress
        };
    }

    private Task<byte[]?> SendAsync(byte[] request, CancellationToken cancellationToken)
    {
        if (_gatewayEndPoint is null)
        {
            return Task.FromResult<byte[]?>(null);
        }

        return _udpRequester.SendAsync(_gatewayEndPoint, request, 4, cancellationToken);
    }

    private IPEndPoint? GetGatewayEndPoint()
    {
        if (_configuredGatewayEndPoint is not null)
        {
            return _configuredGatewayEndPoint;
        }

        var gatewayAddress = _gatewayDiscovery.DiscoverGatewayAddress();
        return gatewayAddress is null ? null : new IPEndPoint(gatewayAddress, PcpPort);
    }

    private IPAddress GetClientAddress(IPAddress? mappingAddress)
    {
        if (mappingAddress is not null)
        {
            return mappingAddress;
        }

        var gatewayAddress = _gatewayEndPoint?.Address;
        return gatewayAddress is null ? IPAddress.Any : NetworkUtils.GetLocalAddressForGateway(gatewayAddress) ?? IPAddress.Any;
    }

    private PortMappingResult Result(OpenPortStatus status, PortMapping mapping, string? error = null) =>
        new()
        {
            Status = status,
            Provider = Name,
            Mapping = mapping,
            ErrorMessage = error
        };

    private static string CreateNonceKey(PortMapping mapping) =>
        $"{mapping.Protocol}:{mapping.InternalAddress?.ToString() ?? "*"}:{mapping.InternalPort}:{mapping.ExternalPort}";
}
