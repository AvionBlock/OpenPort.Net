using System.Net;
using OpenPort.Net.Discovery;
using OpenPort.Net.Internal;
using OpenPort.Net.Models;

namespace OpenPort.Net.Providers;

/// <summary>
/// NAT-PMP provider for gateways that implement RFC 6886-style UDP port mapping.
/// </summary>
public sealed class NatPmpProvider : IPortMappingProvider
{
    private const int NatPmpPort = 5351;
    private readonly GatewayDiscovery _gatewayDiscovery = new();
    private readonly UdpRequester _udpRequester;
    private readonly IPEndPoint? _configuredGatewayEndPoint;
    private IPEndPoint? _gatewayEndPoint;

    /// <summary>
    /// Creates a NAT-PMP provider using automatic gateway discovery.
    /// </summary>
    public NatPmpProvider(TimeSpan timeout)
        : this(timeout, null)
    {
    }

    internal NatPmpProvider(TimeSpan timeout, IPEndPoint? gatewayEndPoint)
    {
        _udpRequester = new UdpRequester(timeout);
        _configuredGatewayEndPoint = gatewayEndPoint;
        _gatewayEndPoint = gatewayEndPoint;
    }

    /// <inheritdoc />
    public string Name => "NAT-PMP";

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

        var response = await SendAsync(NatPmpMessage.CreateExternalAddressRequest(), cancellationToken).ConfigureAwait(false);
        return response is not null &&
               NatPmpMessage.TryParseExternalAddressResponse(response, out var resultCode, out _) &&
               resultCode == 0;
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
    public async Task<IPAddress?> GetExternalIPAddressAsync(CancellationToken cancellationToken = default)
    {
        _gatewayEndPoint ??= GetGatewayEndPoint();
        if (_gatewayEndPoint is null)
        {
            return null;
        }

        var response = await SendAsync(NatPmpMessage.CreateExternalAddressRequest(), cancellationToken).ConfigureAwait(false);
        if (response is null ||
            !NatPmpMessage.TryParseExternalAddressResponse(response, out var resultCode, out var address) ||
            resultCode != 0)
        {
            return null;
        }

        return address;
    }

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

        var request = NatPmpMessage.CreateMapRequest(mapping.Protocol, mapping.InternalPort, mapping.ExternalPort, lifetimeSeconds);
        var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return Result(OpenPortStatus.Timeout, mapping, "NAT-PMP gateway did not respond.");
        }

        if (!NatPmpMessage.TryParseMapResponse(response, mapping.Protocol, out var mapResponse))
        {
            return Result(OpenPortStatus.InvalidRequest, mapping, "Invalid NAT-PMP response.");
        }

        if (mapResponse.ResultCode != 0)
        {
            return Result(
                NatPmpMessage.MapResultCode(mapResponse.ResultCode),
                mapping,
                $"NAT-PMP result code {mapResponse.ResultCode} ({NatPmpMessage.GetResultName(mapResponse.ResultCode)}).");
        }

        var actualMapping = PortMappingCopy.WithExternalPort(mapping, mapResponse.ExternalPort);
        var status = mapResponse.ExternalPort == mapping.ExternalPort ? OpenPortStatus.Success : OpenPortStatus.ExternalPortChanged;
        return Result(status, actualMapping, externalPort: mapResponse.ExternalPort);
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
        return gatewayAddress is null ? null : new IPEndPoint(gatewayAddress, NatPmpPort);
    }

    private PortMappingResult Result(OpenPortStatus status, PortMapping mapping, string? error = null, int? externalPort = null) =>
        new()
        {
            Status = status,
            Provider = Name,
            Mapping = mapping,
            ExternalPort = externalPort,
            ErrorMessage = error
        };
}
