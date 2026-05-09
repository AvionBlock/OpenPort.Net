using System.Net;
using OpenPort.Net.Internal;
using OpenPort.Net.Models;
using OpenPort.Net.Providers;

namespace OpenPort.Net.Tests;

public class PcpProviderTests
{
    [Fact]
    public async Task DiscoverAsync_SendsAnnounceAndAcceptsSuccessResponse()
    {
        await using var server = new UdpTestServer(request =>
        {
            Assert.Equal(PcpMessage.Version, request[0]);
            Assert.Equal(PcpMessage.AnnounceOpcode, request[1]);
            return PcpAnnounceResponse();
        });
        var provider = new PcpProvider(TimeSpan.FromSeconds(1), server.EndPoint);

        var available = await provider.DiscoverAsync();

        Assert.True(available);
    }

    [Fact]
    public async Task OpenAsync_ReturnsAssignedExternalAddressAndChangedPort()
    {
        byte[]? capturedRequest = null;
        await using var server = new UdpTestServer(request =>
        {
            capturedRequest = request;
            return PcpMapResponse(externalPort: 25566, IPAddress.Parse("203.0.113.20"));
        });
        var provider = new PcpProvider(TimeSpan.FromSeconds(1), server.EndPoint);

        var result = await provider.OpenAsync(new PortMapping
        {
            InternalPort = 25565,
            ExternalPort = 25565,
            Protocol = PortProtocol.Tcp,
            Lifetime = TimeSpan.FromHours(2),
            InternalAddress = IPAddress.Parse("192.168.1.10")
        });

        Assert.Equal(OpenPortStatus.ExternalPortChanged, result.Status);
        Assert.Equal(25566, result.ExternalPort);
        Assert.Equal(IPAddress.Parse("203.0.113.20"), result.ExternalAddress);
        Assert.Equal(25566, result.Mapping!.ExternalPort);
        Assert.NotNull(capturedRequest);
        Assert.Equal(6, capturedRequest![36]);
        Assert.Equal([0x63, 0xdd], capturedRequest[40..42]);
    }

    [Fact]
    public async Task CloseAsync_ReusesMappingNonceFromOpen()
    {
        var nonces = new List<byte[]>();
        await using var server = new UdpTestServer(request =>
        {
            nonces.Add(request[24..36].ToArray());
            return PcpMapResponse(externalPort: 30000, IPAddress.Parse("203.0.113.21"));
        });
        var provider = new PcpProvider(TimeSpan.FromSeconds(1), server.EndPoint);
        var mapping = new PortMapping
        {
            InternalPort = 30000,
            ExternalPort = 30000,
            Protocol = PortProtocol.Udp,
            Lifetime = TimeSpan.FromMinutes(30),
            InternalAddress = IPAddress.Parse("192.168.1.11")
        };

        var openResult = await provider.OpenAsync(mapping);
        var closeResult = await provider.CloseAsync(openResult.Mapping!);

        Assert.Equal(OpenPortStatus.Success, closeResult.Status);
        Assert.Equal(2, nonces.Count);
        Assert.Equal(nonces[0], nonces[1]);
    }

    private static byte[] PcpAnnounceResponse()
    {
        var response = new byte[24];
        response[0] = PcpMessage.Version;
        response[1] = PcpMessage.ResponseMask | PcpMessage.AnnounceOpcode;
        return response;
    }

    private static byte[] PcpMapResponse(int externalPort, IPAddress externalAddress)
    {
        var response = new byte[60];
        response[0] = PcpMessage.Version;
        response[1] = PcpMessage.ResponseMask | PcpMessage.MapOpcode;
        NetworkUtils.WriteUInt32BigEndian(response, 4, 7200);
        NetworkUtils.WriteUInt16BigEndian(response, 42, externalPort);
        var mappedAddress = externalAddress.MapToIPv6().GetAddressBytes();
        Buffer.BlockCopy(mappedAddress, 0, response, 44, mappedAddress.Length);
        return response;
    }
}
