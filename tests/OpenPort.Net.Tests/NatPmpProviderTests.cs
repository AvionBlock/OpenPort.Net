using System.Net;
using OpenPort.Net.Internal;
using OpenPort.Net.Models;
using OpenPort.Net.Providers;

namespace OpenPort.Net.Tests;

public class NatPmpProviderTests
{
    [Fact]
    public async Task GetExternalIPAddressAsync_ReadsAddressFromMockGateway()
    {
        await using var server = new UdpTestServer(request =>
        {
            Assert.Equal(NatPmpMessage.ExternalAddressOpcode, request[1]);
            return NatPmpExternalAddressResponse(IPAddress.Parse("203.0.113.10"));
        });
        var provider = new NatPmpProvider(TimeSpan.FromSeconds(1), server.EndPoint);

        var address = await provider.GetExternalIPAddressAsync();

        Assert.Equal(IPAddress.Parse("203.0.113.10"), address);
    }

    [Fact]
    public async Task OpenAsync_ReturnsExternalPortChangedWhenGatewayAssignsDifferentPort()
    {
        await using var server = new UdpTestServer(request =>
        {
            Assert.Equal(NatPmpMessage.UdpMapOpcode, request[1]);
            return NatPmpMapResponse(NatPmpMessage.UdpMapOpcode, internalPort: 19132, externalPort: 19133, lifetime: 3600);
        });
        var provider = new NatPmpProvider(TimeSpan.FromSeconds(1), server.EndPoint);

        var result = await provider.OpenAsync(new PortMapping
        {
            InternalPort = 19132,
            ExternalPort = 19132,
            Protocol = PortProtocol.Udp,
            Lifetime = TimeSpan.FromHours(1)
        });

        Assert.Equal(OpenPortStatus.ExternalPortChanged, result.Status);
        Assert.Equal(19133, result.ExternalPort);
        Assert.Equal(19133, result.Mapping!.ExternalPort);
    }

    [Fact]
    public async Task CloseAsync_SendsZeroLifetime()
    {
        byte[]? capturedRequest = null;
        await using var server = new UdpTestServer(request =>
        {
            capturedRequest = request;
            return NatPmpMapResponse(NatPmpMessage.TcpMapOpcode, internalPort: 8080, externalPort: 8080, lifetime: 0);
        });
        var provider = new NatPmpProvider(TimeSpan.FromSeconds(1), server.EndPoint);

        var result = await provider.CloseAsync(new PortMapping
        {
            InternalPort = 8080,
            ExternalPort = 8080,
            Protocol = PortProtocol.Tcp
        });

        Assert.Equal(OpenPortStatus.Success, result.Status);
        Assert.NotNull(capturedRequest);
        Assert.Equal([0x00, 0x00, 0x00, 0x00], capturedRequest![8..12]);
    }

    private static byte[] NatPmpExternalAddressResponse(IPAddress address)
    {
        var response = new byte[12];
        response[1] = NatPmpMessage.ResponseOffset + NatPmpMessage.ExternalAddressOpcode;
        Buffer.BlockCopy(address.GetAddressBytes(), 0, response, 8, 4);
        return response;
    }

    private static byte[] NatPmpMapResponse(byte opcode, int internalPort, int externalPort, uint lifetime)
    {
        var response = new byte[16];
        response[1] = (byte)(NatPmpMessage.ResponseOffset + opcode);
        NetworkUtils.WriteUInt16BigEndian(response, 8, internalPort);
        NetworkUtils.WriteUInt16BigEndian(response, 10, externalPort);
        NetworkUtils.WriteUInt32BigEndian(response, 12, lifetime);
        return response;
    }
}
